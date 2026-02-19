using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using RogueDeckCoop.Networking;
using System.Collections.Generic;
using UnityEngine.InputSystem;

public class PauseMenuUI : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject _menuRoot;
    [SerializeField] private GameObject _buttonsPanel;
    [SerializeField] private GameObject _settingsPanel;
    [SerializeField] private GameObject _confirmationPanel;
    [SerializeField] private GameObject _adminPanel; // Panel se seznamem hráčů

    [Header("Confirmation UI")]
    [SerializeField] private TextMeshProUGUI _confirmationText;
    [SerializeField] private Button _confirmYesButton;
    [SerializeField] private Button _confirmNoButton;

    [Header("Admin UI")]
    [SerializeField] private Transform _playerListContent;
    [SerializeField] private GameObject _kickButtonPrefab; // Prefab tlačítka se jménem hráče a "X"

    private bool _isOpen = false;
    private bool _isHost = false;

    private void Start()
    {
        _menuRoot.SetActive(false);
        _settingsPanel.SetActive(false);
        _confirmationPanel.SetActive(false);

        _isHost = NetworkManager.Singleton.IsHost;
        if (_adminPanel != null) _adminPanel.SetActive(_isHost); // Admin panel jen pro hosta
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
        {
            ToggleMenu();
        }
    }

    public void ToggleMenu()
    {
        if (_settingsPanel.activeSelf || _confirmationPanel.activeSelf)
        {
            ShowMainButtons();
            return;
        }

        _isOpen = !_isOpen;
        _menuRoot.SetActive(_isOpen);

        Cursor.lockState = _isOpen ? CursorLockMode.None : CursorLockMode.Locked;
        Cursor.visible = _isOpen;

        if (_isOpen)
        {
            ShowMainButtons();
            // RefreshPlayerList voláme jen pokud jsme připojení a jsme Host
            if (_isHost && NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer)
            {
                RefreshPlayerList();
            }
        }

        // --- OPRAVA CHYBY ZDE ---
        // Musíme zkontrolovat IsSpawned, jinak RPC vyhodí výjimku
        if (GameManager.Instance != null && GameManager.Instance.IsSpawned)
        {
            GameManager.Instance.SetPlayerInMenuServerRpc(_isOpen);
        }
        else
        {
            // Pokud hrajeme offline nebo se teprve připojujeme, jen vypíšeme log
            // (Hra se síťově nepauzne, což je v pořádku, protože nejsme na síti)
            Debug.LogWarning("GameManager is not spawned yet via Netcode. RPC skipped.");
        }
    }

    private void ShowMainButtons()
    {
        _buttonsPanel.SetActive(true);
        _settingsPanel.SetActive(false);
        _confirmationPanel.SetActive(false);
    }

    // --- BUTTON CALLBACKS ---

    public void OnResumeClicked() => ToggleMenu();

    public void OnSettingsClicked()
    {
        _buttonsPanel.SetActive(false);
        _settingsPanel.SetActive(true);
    }

    public void OnExitClicked()
    {
        _buttonsPanel.SetActive(false);
        _confirmationPanel.SetActive(true);

        if (_isHost)
        {
            _confirmationText.text = "Jsi HOST. Ukončením hry vykopneš všechny hráče.\nOpravdu ukončit?";
            _confirmYesButton.onClick.RemoveAllListeners();
            _confirmYesButton.onClick.AddListener(ConfirmExitAsHost);
        }
        else
        {
            _confirmationText.text = "Opravdu chceš opustit hru?\nTvůj postup bude uložen.";
            _confirmYesButton.onClick.RemoveAllListeners();
            _confirmYesButton.onClick.AddListener(ConfirmExitAsClient);
        }

        _confirmNoButton.onClick.RemoveAllListeners();
        _confirmNoButton.onClick.AddListener(ShowMainButtons);
    }

    // --- EXIT LOGIC ---

    private void ConfirmExitAsClient()
    {
        // 1. Uložit postavu (lokálně nebo request na server)
        if (PlayerAttributes.LocalInstance != null)
        {
            PlayerAttributes.LocalInstance.SavePlayerData();
        }

        // 2. Opustit lobby a síť
        GameManager.Instance.SetPlayerInMenuServerRpc(false); // Aby se hra odpauzla po mém odchodu
        SteamManager.Instance.LeaveLobby();
        AppManager.Instance.GoToMainMenu();
    }

    private void ConfirmExitAsHost()
    {
        // Host ukončuje celou session
        GameManager.Instance.RequestEndGameServerRpc();
    }

    // --- ADMIN LOGIC (Kick) ---

    private void RefreshPlayerList()
    {
        // Vyčistit starý list
        foreach (Transform child in _playerListContent) Destroy(child.gameObject);

        // Naplnit aktuálními hráči
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            ulong id = client.ClientId;

            // Nevypisovat sebe (Hosta) v seznamu na vyhození
            if (id == NetworkManager.Singleton.LocalClientId) continue;

            GameObject btnObj = Instantiate(_kickButtonPrefab, _playerListContent);

            // Nastavení textu (Jméno zatím Player ID, dokud nebudeš mít sync jmen v GameScene)
            var textComp = btnObj.GetComponentInChildren<TextMeshProUGUI>();
            if (textComp) textComp.text = $"Player {id}";

            // Nastavení tlačítka
            var btnComp = btnObj.GetComponentInChildren<Button>();
            if (btnComp)
            {
                btnComp.onClick.AddListener(() => OnKickPlayerClicked(id));
            }
        }
    }

    private void OnKickPlayerClicked(ulong targetId)
    {
        GameManager.Instance.KickPlayerServerRpc(targetId);
        // Refresh listu po krátké prodlevě
        Invoke(nameof(RefreshPlayerList), 0.5f);
    }
}