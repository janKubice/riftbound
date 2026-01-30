using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using Unity.Collections;
using RogueDeckCoop.Networking;

public class LobbyRoomManager : NetworkBehaviour
{
    // Synchronizovaný seznam hráčů. Každá změna se automaticky pošle všem.
    public NetworkList<LobbyPlayerData> LobbyPlayers;
    private void Awake()
    {
        // NetworkList musíme inicializovat v Awake
        LobbyPlayers = new NetworkList<LobbyPlayerData>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
            AddPlayerToLobby(NetworkManager.Singleton.LocalClientId);
        }

        LoadingScreenManager.Instance.Hide();
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        }
    }


    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    // --- Server Logic ---

    private void OnClientConnected(ulong clientId)
    {
        AddPlayerToLobby(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        // Najdeme index hráče v listu a odstraníme ho
        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == clientId)
            {
                LobbyPlayers.RemoveAt(i);
                break;
            }
        }
    }

    private void AddPlayerToLobby(ulong clientId)
    {
        // Výchozí hodnoty nového hráče
        LobbyPlayers.Add(new LobbyPlayerData
        {
            ClientId = clientId,
            PlayerName = $"Player {clientId}", // Jméno se aktualizuje později přes RPC
            CharacterId = 0, // Default postava
            IsReady = false
        });
    }

    // --- RPCs (Akce od Klientů) ---

    [ServerRpc(RequireOwnership = false)]
    public void SetPlayerNameServerRpc(string name, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        int index = FindPlayerIndex(senderId);

        if (index != -1)
        {
            LobbyPlayerData data = LobbyPlayers[index];
            data.PlayerName = new FixedString64Bytes(name);
            LobbyPlayers[index] = data; // Přepsáním v listu se změna rozešle všem
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void SelectCharacterServerRpc(int charId, ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        int index = FindPlayerIndex(senderId);

        if (index != -1)
        {
            LobbyPlayerData data = LobbyPlayers[index];
            data.CharacterId = charId;
            // Pokud změní postavu, zrušíme Ready status
            data.IsReady = false;
            LobbyPlayers[index] = data;
        }
    }

    [ServerRpc(RequireOwnership = false)] // Důležité: false, aby to mohl zavolat klient
    public void ToggleReadyServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;

        // Najít hráče v seznamu a změnit mu stav
        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == senderId)
            {
                // Structy jsou hodnotové typy, musíme je zkopírovat, upravit a vrátit
                LobbyPlayerData data = LobbyPlayers[i];
                data.IsReady = !data.IsReady;
                LobbyPlayers[i] = data; // Tímto se vyvolá OnListChanged

                Debug.Log($"Player {senderId} ready state toggled to: {data.IsReady}");
                return;
            }
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void StartGameServerRpc(ServerRpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        // Jen host (ClientId 0) může spustit hru
        if (senderId != NetworkManager.Singleton.LocalClientId) return;

        // Kontrola: Jsou všichni Ready?
        bool allReady = true;
        foreach (var player in LobbyPlayers)
        {
            if (!player.IsReady)
            {
                allReady = false;
                break;
            }
        }

        if (allReady)
        {
            // 1. Uložíme výběry do SteamManageru (aby přežily změnu scény)
            SteamManager.Instance.FinalCharacterSelections.Clear();
            foreach (var player in LobbyPlayers)
            {
                SteamManager.Instance.FinalCharacterSelections[player.ClientId] = player.CharacterId;
            }

            // 2. Uzamkneme lobby na Steamu (nikdo další se nepřipojí)
            Steamworks.SteamMatchmaking.SetLobbyJoinable(SteamManager.Instance.CurrentLobbyId, false);

            // 3. Načteme herní scénu
            NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    private int FindPlayerIndex(ulong clientId)
    {
        for (int i = 0; i < LobbyPlayers.Count; i++)
        {
            if (LobbyPlayers[i].ClientId == clientId) return i;
        }
        return -1;
    }
}