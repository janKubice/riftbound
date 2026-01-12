using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;

public class UpgradeShopUI : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private GameObject _shopWindow; // Celý panel (aktivace/deaktivace)
    [SerializeField] private Transform _slotsContainer; // Grid Layout Group, kam se sypou tlačítka
    [SerializeField] private GameObject _slotPrefab; // Prefab s UpgradeSlotUI
    [SerializeField] private TMP_Text _totalXPText; // "XP: 1500"

    private PlayerProgression _localPlayerProgression;
    private List<UpgradeSlotUI> _spawnedSlots = new List<UpgradeSlotUI>();
    private PlayerController _localPlayerController;
    private bool _isOpen = false;

    private void Start()
    {
        // Na startu schováme okno
        _shopWindow.SetActive(false);
    }

    private void Update()
    {
        // Klávesa 'B' pro otevření obchodu (Shop/Buy)
        if (UnityEngine.InputSystem.Keyboard.current.bKey.wasPressedThisFrame)
        {
            ToggleShop();
        }
    }

    private void ToggleShop()
{
    _isOpen = !_isOpen;
    _shopWindow.SetActive(_isOpen);

    if (_localPlayerController == null) FindLocalPlayer();

    if (_isOpen)
    {
        // OTEVŘENO: Odemknout myš a ZAKÁZAT pohyb/rotaci hráče
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        
        if (_localPlayerController != null) 
        {
            _localPlayerController.enabled = false; // Vypne Update() v PlayerControlleru
        }
    }
    else
    {
        // ZAVŘENO: Zamknout myš a POVOLIT pohyb
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (_localPlayerController != null) 
        {
            _localPlayerController.enabled = true; // Zapne Update() v PlayerControlleru
        }
    }
}

    private void FindLocalPlayer()
    {
        // Najdeme lokálního klienta
        var localClient = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localClient != null)
        {
            _localPlayerProgression = localClient.GetComponent<PlayerProgression>();
            _localPlayerController = localClient.GetComponent<PlayerController>(); 

            if (_localPlayerProgression != null)
            {
                // Napojíme se na eventy změny (aby se UI samo překreslilo)
                _localPlayerProgression.OnXPChanged += RefreshAllSlots;
                _localPlayerProgression.OnUpgradePurchased += RefreshAllSlots;

                GenerateSlots();
            }
        }
    }

    private void GenerateSlots()
    {
        // Vyčistit staré (pokud nějaké byly)
        foreach (Transform child in _slotsContainer) Destroy(child.gameObject);
        _spawnedSlots.Clear();

        int count = _localPlayerProgression.GetUpgradesCount();

        for (int i = 0; i < count; i++)
        {
            StatUpgradeData data = _localPlayerProgression.GetData(i);

            GameObject newSlotObj = Instantiate(_slotPrefab, _slotsContainer);
            UpgradeSlotUI slotUI = newSlotObj.GetComponent<UpgradeSlotUI>();

            slotUI.Initialize(i, _localPlayerProgression, data);
            _spawnedSlots.Add(slotUI);
        }

        RefreshAllSlots();
    }

    private void RefreshAllSlots()
    {
        if (_localPlayerProgression == null) return;

        // Update celkových XP
        _totalXPText.text = $"XP: {_localPlayerProgression.CurrentXP.Value}";

        // Update všech tlačítek
        foreach (var slot in _spawnedSlots)
        {
            slot.Refresh();
        }
    }

    private void OnDestroy()
    {
        // Úklid eventů
        if (_localPlayerProgression != null)
        {
            _localPlayerProgression.OnXPChanged -= RefreshAllSlots;
            _localPlayerProgression.OnUpgradePurchased -= RefreshAllSlots;
        }
    }
}