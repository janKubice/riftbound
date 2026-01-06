using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using RogueDeckCoop.Networking;
using System.Collections.Generic;
using System.Linq;

public class LobbyUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private LobbyRoomManager _roomManager;
    [SerializeField] private Transform _playerContainer;
    [SerializeField] private GameObject _playerSlotPrefab;
    [SerializeField] private Button _startGameButton;
    [SerializeField] private TextMeshProUGUI _readyButtonText;

    [Header("Character Selection")]
    [SerializeField] private Button _btnCharGuy;

    private readonly Dictionary<ulong, GameObject> _playerSlots = new Dictionary<ulong, GameObject>();

    private void Start()
    {
        _roomManager.LobbyPlayers.OnListChanged += HandleLobbyPlayersChanged;

        if (NetworkManager.Singleton.IsClient)
        {
            _roomManager.SetPlayerNameServerRpc(SteamManager.Instance.PlayerName);
        }

        _btnCharGuy.onClick.AddListener(() => _roomManager.SelectCharacterServerRpc(0));

        InitialRefresh();
    }

    private void OnDestroy()
    {
        if (_roomManager != null && _roomManager.LobbyPlayers != null)
        {
            _roomManager.LobbyPlayers.OnListChanged -= HandleLobbyPlayersChanged;
        }
    }

    private void HandleLobbyPlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
    {
        UpdateUI();
    }

    private void InitialRefresh()
    {
        foreach (Transform child in _playerContainer) child.gameObject.NetDestroy();
        _playerSlots.Clear();
        UpdateUI();
    }

    private void UpdateUI()
    {
        // 1. Synchronizace slotů (přidání nových a aktualizace stávajících)
        HashSet<ulong> currentClientIds = new HashSet<ulong>();

        foreach (var player in _roomManager.LobbyPlayers)
        {
            currentClientIds.Add(player.ClientId);

            if (!_playerSlots.ContainsKey(player.ClientId))
            {
                GameObject newSlot = Instantiate(_playerSlotPrefab, _playerContainer);
                _playerSlots.Add(player.ClientId, newSlot);
            }

            UpdateSlotVisuals(_playerSlots[player.ClientId], player);
        }

        // 2. Odstranění odpojených hráčů
        List<ulong> idsToRemove = _playerSlots.Keys.Where(id => !currentClientIds.Contains(id)).ToList();
        foreach (ulong id in idsToRemove)
        {
            _playerSlots[id].NetDestroy();
            _playerSlots.Remove(id);
        }

        // 3. Logika pro Hostitelské tlačítko Start
        bool allReady = true;

        // Manuální kontrola stavu Ready pro odstranění chyb inference
        foreach (LobbyPlayerData p in _roomManager.LobbyPlayers)
        {
            if (!p.IsReady)
            {
                allReady = false;
                break;
            }
        }
        if (NetworkManager.Singleton.IsHost && _startGameButton != null)
        {
            _startGameButton.gameObject.SetActive(true);
            _startGameButton.interactable = allReady && _roomManager.LobbyPlayers.Count > 0;
        }

        UpdateReadyButtonState();
    }

    private void UpdateSlotVisuals(GameObject slot, LobbyPlayerData data)
    {
        var texts = slot.GetComponentsInChildren<TextMeshProUGUI>();
        if (texts.Length < 3) return;

        bool isLocal = data.ClientId == NetworkManager.Singleton.LocalClientId;

        // Jméno (Zvýraznění lokálního hráče)
        texts[0].text = isLocal ? $"<b>{data.PlayerName} (YOU)</b>" : data.PlayerName.ToString();

        // Postava (Zatím fixní Guy)
        texts[1].text = "Character: Guy";

        // Status (Barevné rozlišení)
        texts[2].text = data.IsReady
            ? "<color=#00FF00>READY</color>"
            : "<color=#FF4444>WAITING...</color>";

        // Vizuální zpětná vazba pro řádek (pokud má slot Image komponentu)
        if (slot.TryGetComponent<Image>(out var bg))
        {
            bg.color = isLocal ? new Color(0.2f, 0.4f, 0.2f, 0.5f) : new Color(0.1f, 0.1f, 0.1f, 0.5f);
        }
    }

    private void UpdateReadyButtonState()
    {
        ulong myId = NetworkManager.Singleton.LocalClientId;

        // Iterace přes NetworkList s explicitním typem pro odstranění chyby inference
        foreach (LobbyPlayerData player in _roomManager.LobbyPlayers)
        {
            if (player.ClientId == myId)
            {
                _readyButtonText.text = player.IsReady ? "CANCEL READY" : "SET READY";
                return;
            }
        }
    }

    public void OnReadyClicked() => _roomManager.ToggleReadyServerRpc();
    public void OnStartGameClicked() => _roomManager.StartGameServerRpc();
    public void OnLeaveClicked()
    {
        SteamManager.Instance.LeaveLobby();
        AppManager.Instance.GoToMainMenu();
    }
}