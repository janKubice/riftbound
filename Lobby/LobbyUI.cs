using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using RogueDeckCoop.Networking;
using Steamworks;
using System.Collections.Generic;

public class LobbyUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LobbyRoomManager _roomManager;
    [SerializeField] private CharacterDatabase _charDatabase;
    [SerializeField] private LobbyCharacterVisuals _visuals;

    [Header("Left Panel - Player Slots")]
    [SerializeField] private LobbySlotUI[] _slots; // Vytvoř 4 sloty natvrdo v Canvasu

    [Header("Center Panel - Selection")]
    [SerializeField] private TextMeshProUGUI _charNameText;
    [SerializeField] private Button _arrowLeft;
    [SerializeField] private Button _arrowRight;

    [Header("Right Panel - Stats")]
    [SerializeField] private TextMeshProUGUI _statsDescription;
    // Zde by byly slidery pro staty (Damage, Speed...)

    [Header("Bottom Panel - Actions")]
    [SerializeField] private Button _btnReady;
    [SerializeField] private TextMeshProUGUI _txtReady;
    [SerializeField] private Button _btnStart;
    [SerializeField] private Button _btnLeave;

    private int _localSelectionIndex = 0;

    private void Start()
    {
        // 1. Ošetření, pokud manager ještě není ready (pro jistotu)
        if (_roomManager == null)
        {
            Debug.LogError("LobbyRoomManager is not assigned in LobbyUI!");
            return;
        }
        _roomManager.LobbyPlayers.OnListChanged += HandleLobbyPlayersChanged;

        // Button Listeners
        _arrowLeft.onClick.AddListener(() => ChangeCharacter(-1));
        _arrowRight.onClick.AddListener(() => ChangeCharacter(1));

        _btnReady.onClick.AddListener(OnReadyClicked);
        _btnLeave.onClick.AddListener(OnLeaveClicked);
        _btnStart.onClick.AddListener(() => _roomManager.StartGameServerRpc());

        // Inicializace výběru
        ChangeCharacter(0); // Nastaví výchozí nulu a pošle RPC

        UpdatePlayerSlots();
        UpdateReadyButton();
        UpdateStartButton();
    }

    private void OnDestroy()
    {
        if (_roomManager != null && _roomManager.LobbyPlayers != null)
            _roomManager.LobbyPlayers.OnListChanged -= HandleLobbyPlayersChanged;
    }

    // --- Character Selection Logic ---

    private void ChangeCharacter(int direction)
    {
        _localSelectionIndex += direction;

        // Cyklování (Looping)
        if (_localSelectionIndex < 0) _localSelectionIndex = _charDatabase.Characters.Count - 1;
        if (_localSelectionIndex >= _charDatabase.Characters.Count) _localSelectionIndex = 0;

        // 1. Update Visuals (Okamžitá odezva pro hráče)
        UpdateCenterPanel(_localSelectionIndex);

        // 2. Send RPC (Zápis do sítě)
        _roomManager.SelectCharacterServerRpc(_localSelectionIndex);
    }

    private void UpdateCenterPanel(int charId)
    {
        CharacterData data = _charDatabase.GetCharacter(charId);
        if (data == null) return;

        // Update 3D Model
        _visuals.ShowCharacter(data);

        // Update Texts
        _charNameText.text = data.CharacterName;
        _statsDescription.text = data.Description;

        // Update Stats Sliders... (pokud implementuješ)
    }

    // --- Player List Logic (Left Panel) ---

    private void HandleLobbyPlayersChanged(NetworkListEvent<LobbyPlayerData> changeEvent)
    {
        UpdatePlayerSlots();
        UpdateReadyButton();
        UpdateStartButton();
    }

    private void UpdatePlayerSlots()
    {
        // Projdeme fixní sloty (0 až 3)
        for (int i = 0; i < _slots.Length; i++)
        {
            if (i < _roomManager.LobbyPlayers.Count)
            {
                // Slot je obsazen hráčem
                LobbyPlayerData player = _roomManager.LobbyPlayers[i];
                _slots[i].SetOccupied(player);
            }
            else
            {
                // Slot je prázdný -> Zobrazit "Invite"
                _slots[i].SetEmpty();
            }
        }
    }

    // --- Action Buttons ---

    private void OnReadyClicked()
    {
        Debug.Log("Ready clicked");
        _roomManager.ToggleReadyServerRpc();
    }

    private void UpdateReadyButton()
    {
        // Najdi sebe v listu
        ulong myId = NetworkManager.Singleton.LocalClientId;
        foreach (var p in _roomManager.LobbyPlayers)
        {
            if (p.ClientId == myId)
            {
                _txtReady.text = p.IsReady ? "NOT READY" : "READY";
                _btnReady.image.color = p.IsReady ? Color.green : Color.white;
                return;
            }
        }
    }

    private void UpdateStartButton()
    {
        // 1. Zjistit, jestli jsem Host (Server)
        if (!NetworkManager.Singleton.IsHost)
        {
            _btnStart.gameObject.SetActive(false);
            return;
        }

        _btnStart.gameObject.SetActive(true);

        // 2. Kontrola: Jsou všichni ready?
        bool allReady = true;
        foreach (var p in _roomManager.LobbyPlayers)
        {
            if (!p.IsReady)
            {
                allReady = false;
                break;
            }
        }

        // 3. Host může kliknout jen když jsou všichni ready
        _btnStart.interactable = allReady && _roomManager.LobbyPlayers.Count > 0;
    }

    private void OnLeaveClicked()
    {
        SteamManager.Instance.LeaveLobby();
        AppManager.Instance.GoToMainMenu();
    }
}