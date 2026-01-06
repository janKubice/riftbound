using UnityEngine;
using Steamworks;
using System.Collections.Generic;
using System;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP; // Pokud používáš UnityTransport, jinak Netcode.Transports
using RogueDeckCoop.Networking;

namespace RogueDeckCoop.Networking
{
    public class SteamManager : PersistentSingleton<SteamManager>
    {
        // --- KONFIGURACE ---
        private const string KEY_LOBBY_NAME = "LobbyName";
        private const string KEY_GAME_ID = "GameId";
        private const string KEY_IS_SECURED = "IsSecured"; // "1" pokud je heslo, "0" pokud ne
        private const string GAME_ID_VALUE = "RogueDeckCoop";

        // --- STAV ---
        private bool _isSteamInitialized = false;
        public bool IsSteamInitialized => _isSteamInitialized;
        // Mapa: ClientID -> CharacterID (uchová výběr při přechodu do hry)
        public Dictionary<ulong, int> FinalCharacterSelections = new Dictionary<ulong, int>();

        public CSteamID PlayerSteamId { get; private set; }
        public string PlayerName { get; private set; }

        public CSteamID CurrentLobbyId { get; private set; }
        public CSteamID HostId { get; private set; }

        // --- DATA PRO ZAKLÁDÁNÍ ---
        private string _targetLobbyName;
        private string _hostingPassword; // Heslo, které nastavil Hostitel
        private string _clientPassword;  // Heslo, které zadal Klient při připojování

        // --- CALLBACKY ---
        protected Callback<LobbyCreated_t> _lobbyCreated;
        protected Callback<LobbyMatchList_t> _lobbyMatchList;
        protected Callback<LobbyEnter_t> _lobbyEnter;
        protected Callback<LobbyChatUpdate_t> _lobbyChatUpdate;
        protected Callback<GameRichPresenceJoinRequested_t> _joinRequest; // NOVÉ: Reakce na pozvánku
        // UI se k tomuto přihlásí, aby zobrazilo chybu
        public static event Action<string> OnConnectionFailed;

        // --- EVENTY ---
        public static event Action<List<CSteamID>> OnLobbyListUpdated;

        [Header("Transport")]
        [SerializeField] private Netcode.Transports.SteamNetworkingSocketsTransport _steamTransport;

        protected override void Awake()
        {
            base.Awake();
            if (Instance != this)
            {
                Debug.LogWarning("[SteamManager] Duplicitní instance zničena.");
                return;
            }
            InitializeSteam();
        }

        private void InitializeSteam()
        {
            if (_isSteamInitialized) return;

            try
            {
                if (SteamAPI.Init())
                {
                    _isSteamInitialized = true;
                    PlayerSteamId = SteamUser.GetSteamID();
                    PlayerName = SteamFriends.GetPersonaName();

                    _lobbyCreated = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
                    _lobbyMatchList = Callback<LobbyMatchList_t>.Create(OnLobbyMatchList);
                    _lobbyEnter = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
                    _lobbyChatUpdate = Callback<LobbyChatUpdate_t>.Create(OnLobbyChatUpdate);

                    // NOVÉ: Callback pro přijetí pozvánky ze Steam Chatu / Overlaye
                    _joinRequest = Callback<GameRichPresenceJoinRequested_t>.Create(OnJoinRequest);

                    Debug.Log($"[SteamManager] Init OK. Hráč: {PlayerName}");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[SteamManager] Chyba init: {e.Message}");
            }
        }

        private void Start()
        {
            if (Instance != this) return;
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.NetworkConfig.EnableSceneManagement = true;
            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;

            // Nastavení ověřování hesla (Connection Approval)
            NetworkManager.Singleton.ConnectionApprovalCallback = ApprovalCheck;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnect;
        }

        private void OnDestroy() // Nebo OnApplicationQuit, záleží na lifecycle
        {
            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnect;
            }
        }

        private void Update()
        {
            if (_isSteamInitialized)
            {
                SteamAPI.RunCallbacks();
            }
        }

        private void OnApplicationQuit()
        {
            if (_isSteamInitialized) SteamAPI.Shutdown();
        }

        // --- VEŘEJNÉ METODY PRO UI ---

        /// <summary>
        /// Založí lobby s parametry.
        /// </summary>
        public void HostLobby(string lobbyName, LobbyPrivacy privacy, string password = "")
        {
            Debug.Log($"[SteamManager] Volání HostLobby. IsSteamInitialized: {_isSteamInitialized}");
            if (!_isSteamInitialized) return;

            _targetLobbyName = string.IsNullOrEmpty(lobbyName) ? $"{PlayerName}'s Game" : lobbyName;
            _hostingPassword = password;

            ELobbyType steamPrivacy = privacy switch
            {
                LobbyPrivacy.Private => ELobbyType.k_ELobbyTypePrivate,
                LobbyPrivacy.FriendsOnly => ELobbyType.k_ELobbyTypeFriendsOnly,
                _ => ELobbyType.k_ELobbyTypePublic
            };

            SteamAPICall_t handle = SteamMatchmaking.CreateLobby(steamPrivacy, 4);

            if (handle == SteamAPICall_t.Invalid)
            {
                Debug.LogError("[SteamManager] SteamMatchmaking.CreateLobby vrátil INVALID HANDLE!");
            }
            else
            {
                Debug.Log($"[SteamManager] Požadavek na lobby odeslán pod handlem: {handle}");
            }
        }

        /// <summary>
        /// Vyhledá lobby.
        /// </summary>
        public void RequestLobbyList()
        {
            if (!_isSteamInitialized) return;

            // 1. Nastav filtr na celosvětové hledání (nebo Far)
            SteamMatchmaking.AddRequestLobbyListDistanceFilter(ELobbyDistanceFilter.k_ELobbyDistanceFilterWorldwide);

            // 2. Filtrujeme pouze naše hry
            SteamMatchmaking.AddRequestLobbyListStringFilter(KEY_GAME_ID, GAME_ID_VALUE, ELobbyComparison.k_ELobbyComparisonEqual);

            // 3. Odeslání požadavku
            SteamMatchmaking.RequestLobbyList();
        }

        /// <summary>
        /// Připojí se k lobby (volitelně s heslem).
        /// </summary>
        public void JoinLobby(CSteamID lobbyId, string password = "")
        {
            if (!_isSteamInitialized) return;

            _clientPassword = password; // Uložíme heslo pro payload
            Debug.Log($"[SteamManager] Připojuji se k {lobbyId}...");
            SteamMatchmaking.JoinLobby(lobbyId);
        }

        public void LeaveLobby()
        {
            if (!_isSteamInitialized || CurrentLobbyId.m_SteamID == 0) return;
            SteamMatchmaking.LeaveLobby(CurrentLobbyId);
            CurrentLobbyId = CSteamID.Nil;

            if (NetworkManager.Singleton != null)
            {
                NetworkManager.Singleton.Shutdown();
            }
        }

        // --- CALLBACKY ---

        private void OnLobbyCreated(LobbyCreated_t callback)
        {
            Debug.Log($"[SteamManager] OnLobbyCreated callback přijat. Výsledek: {callback.m_eResult}");
            if (callback.m_eResult != EResult.k_EResultOK)
            {
                Debug.LogError("Lobby se nepodařilo vytvořit.");
                return;
            }

            CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            HostId = PlayerSteamId;

            // 1. Nastavíme data lobby (viditelná pro vyhledávání)
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, KEY_GAME_ID, GAME_ID_VALUE);
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, KEY_LOBBY_NAME, _targetLobbyName);
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, "HostName", PlayerName);

            // Nastavíme flag, zda je lobby zaheslované (nehrajeme si na bezpečnost Steamu, jen info)
            bool isSecured = !string.IsNullOrEmpty(_hostingPassword);
            SteamMatchmaking.SetLobbyData(CurrentLobbyId, KEY_IS_SECURED, isSecured ? "1" : "0");

            // 2. Nastavíme Transport
            if (_steamTransport != null)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = _steamTransport;
            }

            // 3. Spustíme Host
            bool success = NetworkManager.Singleton.StartHost();
            Debug.Log($"[SteamManager] StartHost result: {success}");
        }

        private void OnLobbyEntered(LobbyEnter_t callback)
        {
            // EChatRoomEnterResponse check
            if (callback.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                string errorMsg = "Failed to join Steam Lobby: ";
                errorMsg += ((EChatRoomEnterResponse)callback.m_EChatRoomEnterResponse).ToString();

                Debug.LogError(errorMsg);
                OnConnectionFailed?.Invoke(errorMsg);
                return;
            }

            if (NetworkManager.Singleton.IsHost) return;

            CurrentLobbyId = new CSteamID(callback.m_ulSteamIDLobby);
            HostId = SteamMatchmaking.GetLobbyOwner(CurrentLobbyId);

            if (_steamTransport != null)
            {
                NetworkManager.Singleton.NetworkConfig.NetworkTransport = _steamTransport;
                _steamTransport.ConnectToSteamID = HostId.m_SteamID;
            }

            // --- PAYLOAD (HESLO) ---
            // Pošleme heslo serveru v ConnectionData
            string payload = _clientPassword ?? "";
            NetworkManager.Singleton.NetworkConfig.ConnectionData = Encoding.ASCII.GetBytes(payload);

            NetworkManager.Singleton.StartClient();
            LoadingScreenManager.Instance.HookIntoNetworkEvents();
            LoadingScreenManager.Instance.Show("Joining Lobby...");
        }

        private void OnJoinRequest(GameRichPresenceJoinRequested_t callback)
        {
            // Někdo kliknul na "Join Game" ve Steam Friends nebo přijal pozvánku
            Debug.Log($"[SteamManager] Pozvánka přijata! Připojuji se k: {callback.m_rgchConnect}");

            // Connect string je obvykle "+connect_lobby <LobbyID>"
            // SteamMatchmaking to umí parsovat, nebo prostě vytáhneme ID
            CSteamID lobbyId = new CSteamID(ulong.Parse(callback.m_rgchConnect)); // Zjednodušené, Steam často posílá jen ID v parametru m_steamIDLobby pokud je to rich presence

            // POZNÁMKA: Callback struktura má m_steamIDFriend a m_rgchConnect.
            // Pokud m_rgchConnect obsahuje ID lobby, použijeme ho.
            // Pro jistotu zkusíme JoinLobby přímo s tím, co nám Steam dal, pokud je to validní ulong.

            // Lepší varianta pro Steamworks.NET (často stačí joinout přítele):
            // Ale GameRichPresenceJoinRequested_t obvykle nese string parametrů. 
            // Většinou stačí použít ID lobby, které ale callback přímo nedává jako ulong.
            // Zde je hack pro jednoduchost, většinou stačí JoinLobby na ID kamaráda pokud je v lobby? Ne.
            // Musíme parsovat string. Pro teď předpokládejme, že pozvánka funguje přes UI,
            // tohle dořešíme, pokud to bude zlobit.

            // PRO TYPICKÉ POUŽITÍ: Když přijmeš invite, Steam tě hodí do hry.
            // Zde bychom měli zavolat JoinLobby(id).
        }

        private void OnLobbyMatchList(LobbyMatchList_t callback)
        {
            List<CSteamID> lobbyIds = new List<CSteamID>();
            for (int i = 0; i < callback.m_nLobbiesMatching; i++)
            {
                lobbyIds.Add(SteamMatchmaking.GetLobbyByIndex(i));
            }
            OnLobbyListUpdated?.Invoke(lobbyIds);
        }

        private void OnLobbyChatUpdate(LobbyChatUpdate_t callback)
        {
            // Logika pro refresh seznamu hráčů v lobby (bude řešit LobbyManager)
        }

        private void HandleServerStarted()
        {
            // ... existující kód ...

            // HOOK: Aktivace automatického loadingu
            LoadingScreenManager.Instance.HookIntoNetworkEvents();

            if (NetworkManager.Singleton.IsHost)
            {
                // Před načtením Lobby scény manuálně vyvoláme loading (protože LoadScene event se teprve odpálí)
                LoadingScreenManager.Instance.Show("Creating Lobby...");
                NetworkManager.Singleton.SceneManager.LoadScene("LobbyScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
            }
        }

        // --- CONNECTION APPROVAL (Ověření Hesla) ---

        private void ApprovalCheck(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            // 1. Získáme payload (heslo) od klienta
            string passwordSent = Encoding.ASCII.GetString(request.Payload);

            // 2. Ověříme heslo
            bool approved = true;

            // Pokud máme nastavené heslo, porovnáme ho
            if (!string.IsNullOrEmpty(_hostingPassword))
            {
                if (passwordSent != _hostingPassword)
                {
                    approved = false;
                    response.Reason = "Nesprávné heslo!";
                    Debug.Log($"[SteamManager] Odmítnuto připojení {request.ClientNetworkId}: Špatné heslo.");
                }
            }

            // 3. Schválení
            response.Approved = approved;
            response.CreatePlayerObject = false; // Zatím true, v LobbyScene to možná budeme chtít false a spawnovat manuálně

            // Pozice spawnu (volitelné)
            response.Position = Vector3.zero;
            response.Rotation = Quaternion.identity;

            // Pending: Pokud bys chtěl mapovat PREFAB podle výběru postavy, dělá se to tady (PlayerPrefabHash).
            // To ale uděláme až ve Fázi 4.
        }

        // --- NOVÁ METODA: Zpracování odpojení ---
        private void OnClientDisconnect(ulong clientId)
        {
            // Zajímá nás jen, když se odpojíme MY (jako klient), ne když se odpojí někdo jiný
            if (clientId == NetworkManager.Singleton.LocalClientId)
            {
                // Pokud jsme Host a vypínáme hru, není to chyba
                if (NetworkManager.Singleton.IsHost) return;

                // Získáme důvod odpojení (který jsme nastavili v ApprovalCheck jako response.Reason)
                string reason = NetworkManager.Singleton.DisconnectReason;

                if (string.IsNullOrEmpty(reason))
                {
                    reason = "Connection lost or failed.";
                }

                Debug.LogWarning($"[SteamManager] Disconnected: {reason}");

                // Vyvoláme event pro UI, zároveň opustíme Steam lobby, abychom nezůstali viset
                LeaveLobby();
                OnConnectionFailed?.Invoke(reason);
            }
        }
    }
}