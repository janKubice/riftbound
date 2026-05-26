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

    [Header("Tooltip Reference")]
    [SerializeField] private GameObject _tooltipPanel; // Obalový objekt tooltipu
    [SerializeField] private TMP_Text _tooltipNameText;
    [SerializeField] private TMP_Text _tooltipDescriptionText;
    [SerializeField] private TMP_Text _tooltipStatTypeText;

    private PlayerProgression _localPlayerProgression;
    private List<UpgradeSlotUI> _spawnedSlots = new List<UpgradeSlotUI>();

    // Reference na komponenty hráče
    private PlayerController _localPlayerController;
    private PlayerVFX _localPlayerVFX;
    private Animator _localAnimator;
    private bool _isOpen = false;

    private void Start()
    {
        _shopWindow.SetActive(false);
        HideTooltip();
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

        // Kontrola singleplayeru (hráč je v relaci sám)
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count == 1)
        {
            Time.timeScale = _isOpen ? 0f : 1f;
        }

        if (_localPlayerController != null)
        {
            if (_isOpen)
            {
                // OTEVŘENO
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;

                // Zapneme Shop Mode (zajistí levitaci + zámek pohybu)
                _localPlayerController.SetShopMode(true);

                // Zapneme VFX efekty - v singleplayeru se provede lokálně na serveru/hostovi
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
            _localPlayerVFX = localClient.GetComponent<PlayerVFX>();
            _localAnimator = localClient.GetComponent<Animator>();

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
            slotUI.Initialize(i, _localPlayerProgression, data, this);
            _spawnedSlots.Add(slotUI);
        }
        RefreshAllSlots();
    }

    public void ShowTooltip(StatUpgradeData data, int currentLevel)
    {
        _tooltipPanel.SetActive(true);

        // Voláme rozšířené metody přímo na enumu Type!
        string displayName = data.Type.GetDisplayName();
        string hexColor = data.Type.GetColorHex();

        _tooltipNameText.text = $"<color={hexColor}>{data.UpgradeName}</color>";
        _tooltipDescriptionText.text = data.Description;

        _tooltipStatTypeText.text = $"Modifies: <color={hexColor}>{displayName}</color>\n" +
                                    $"Effect: {data.GetValuePreview(currentLevel)}";
    }

    public void HideTooltip()
    {
        _tooltipPanel.SetActive(false);
    }


    private void RefreshAllSlots()
    {
        if (_localPlayerProgression == null) return;
        _totalXPText.SetText("XP: {0}", _localPlayerProgression.CurrentXP.Value);
        foreach (var slot in _spawnedSlots) slot.Refresh();
    }

    private void OnDestroy()
    {
        // Pojistka pro obnovení času při zničení UI okna během pauzy
        if (_isOpen)
        {
            Time.timeScale = 1f;
        }

        if (_localPlayerProgression != null)
        {
            _localPlayerProgression.OnResourcesChanged -= RefreshAllSlots;
            _localPlayerProgression.OnUpgradePurchased -= RefreshAllSlots;
        }
    }
}