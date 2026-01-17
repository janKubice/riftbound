using UnityEngine;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;
using Unity.Netcode.Components;

public class UpgradeShopUI : MonoBehaviour
{
    [Header("UI Reference")]
    [SerializeField] private GameObject _shopWindow;
    [SerializeField] private Transform _slotsContainer;
    [SerializeField] private GameObject _slotPrefab;
    [SerializeField] private TMP_Text _totalXPText;

    private PlayerProgression _localPlayerProgression;
    private List<UpgradeSlotUI> _spawnedSlots = new List<UpgradeSlotUI>();

    // Reference na komponenty hráče
    private PlayerController _localPlayerController;
    private PlayerVFX _localPlayerVFX; // <--- PŘIDÁNO PRO EFEKT
    private Animator _localAnimator;   // <--- PŘIDÁNO PRO ZASTAVENÍ ANIMACÍ

    private bool _isOpen = false;

    private void Start()
    {
        _shopWindow.SetActive(false);
    }

    private void Update()
    {
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

        if (_localPlayerController != null)
        {
            if (_isOpen)
            {
                // OTEVŘENO
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // Zapneme Shop Mode (zajistí levitaci + zámek pohybu)
                _localPlayerController.SetShopMode(true);

                // Zapneme VFX efekty
                if (_localPlayerVFX != null) _localPlayerVFX.ToggleLevitationVFXServerRpc(true);
            }
            else
            {
                // ZAVŘENO
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;

                // Vypneme Shop Mode (vrátí ovládání a gravitaci)
                _localPlayerController.SetShopMode(false);

                // Vypneme VFX
                if (_localPlayerVFX != null) _localPlayerVFX.ToggleLevitationVFXServerRpc(false);
            }
        }
    }

    private void FindLocalPlayer()
    {
        var localClient = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localClient != null)
        {
            _localPlayerProgression = localClient.GetComponent<PlayerProgression>();
            _localPlayerController = localClient.GetComponent<PlayerController>();
            _localPlayerVFX = localClient.GetComponent<PlayerVFX>(); // <--- Najdeme VFX
            _localAnimator = localClient.GetComponent<Animator>();   // <--- Najdeme Animator

            if (_localPlayerProgression != null)
            {
                _localPlayerProgression.OnResourcesChanged += RefreshAllSlots;
                _localPlayerProgression.OnUpgradePurchased += RefreshAllSlots;
                GenerateSlots();
            }
        }
    }

    // ... Zbytek kódu (GenerateSlots, RefreshAllSlots, OnDestroy) zůstává stejný ...
    private void GenerateSlots()
    {
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
        _totalXPText.text = $"XP: {_localPlayerProgression.CurrentXP.Value}";
        foreach (var slot in _spawnedSlots) slot.Refresh();
    }

    private void OnDestroy()
    {
        if (_localPlayerProgression != null)
        {
            _localPlayerProgression.OnResourcesChanged -= RefreshAllSlots;
            _localPlayerProgression.OnUpgradePurchased -= RefreshAllSlots;
        }
    }
}