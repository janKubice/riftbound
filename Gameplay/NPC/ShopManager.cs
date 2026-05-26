using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using System.Collections.Generic;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance; // Singleton jen pro UI přístup

    [Header("UI References")]
    [SerializeField] private GameObject _shopPanel;
    [SerializeField] private Transform _itemsContainer;
    [SerializeField] private GameObject _itemButtonPrefab; // Prefab musí mít WeaponShopItemUI
    [SerializeField] private Button _sellButton;
    [SerializeField] private TextMeshProUGUI _sellButtonText;
    [SerializeField] private TextMeshProUGUI _playerGoldText;

    private NPCInteractable _currentNpc;
    private PlayerShopLogic _localPlayerLogic; // Reference na konkrétního hráče
    private WeaponManager _monitoredWeaponManager;
    private PlayerProgression _localProgression;

    private List<WeaponShopItemUI> _spawnedItems = new List<WeaponShopItemUI>();

    private void Awake()
    {
        Instance = this;
        _shopPanel.SetActive(false);
    }

    // Volá se z NPCInteractable
    public void OpenShop(NPCInteractable npc)
    {
        _currentNpc = npc;

        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            _localPlayerLogic = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerShopLogic>();
            _localProgression = _localPlayerLogic.GetComponent<PlayerProgression>();
        }

        if (_localPlayerLogic == null || _localProgression == null)
        {
            Debug.LogError("[ShopManager] CHYBA: Chybí PlayerShopLogic nebo PlayerProgression na lokálním hráči!");
            return;
        }

        // --- PŘIHLÁŠENÍ K ODBĚRU ZMĚN ---
        _monitoredWeaponManager = _localPlayerLogic.GetComponent<WeaponManager>();
        if (_monitoredWeaponManager != null)
        {
            _monitoredWeaponManager.CurrentWeaponIndex.OnValueChanged += HandleWeaponChanged;
        }

        // Naslouchání na změnu zlaťáků
        _localProgression.Gold.OnValueChanged += HandleGoldChanged;

        RefreshShopUI();
        UpdateGoldText(_localProgression.Gold.Value);

        _shopPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseShop()
    {
        // --- ODHLÁŠENÍ Z ODBĚRU ---
        if (_monitoredWeaponManager != null)
        {
            _monitoredWeaponManager.CurrentWeaponIndex.OnValueChanged -= HandleWeaponChanged;
            _monitoredWeaponManager = null;
        }

        if (_localProgression != null)
        {
            _localProgression.Gold.OnValueChanged -= HandleGoldChanged;
            _localProgression = null;
        }

        _shopPanel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    public bool IsShopOpen()
    {
        return _shopPanel.activeSelf;
    }

    private void HandleWeaponChanged(int oldIndex, int newIndex)
    {
        // Zbraň se změnila, aktualizujeme tlačítko pro prodej
        if (_monitoredWeaponManager != null)
        {
            UpdateSellButton(_monitoredWeaponManager);
        }
    }

    private void HandleGoldChanged(int oldVal, int newVal)
    {
        UpdateGoldText(newVal);

        // Update cenové dostupnosti na všech kartách
        foreach (var item in _spawnedItems)
        {
            item.UpdateAffordability(newVal);
        }
    }

    private void UpdateGoldText(int amount)
    {
        if (_playerGoldText != null)
        {
            _playerGoldText.text = $"Gold: {amount}";
        }
    }

    private void RefreshShopUI()
    {
        // Vyčistit stará tlačítka
        foreach (Transform child in _itemsContainer) Destroy(child.gameObject);
        _spawnedItems.Clear();

        var weaponManager = _localPlayerLogic.GetComponent<WeaponManager>();
        int currentGold = _localProgression.Gold.Value;

        // Vygenerovat tlačítka
        foreach (int index in _currentNpc.WeaponIndexesForSale)
        {
            WeaponData data = weaponManager.GetWeaponDataByIndex(index);

            if (data != null)
            {
                GameObject btnObj = Instantiate(_itemButtonPrefab, _itemsContainer);
                WeaponShopItemUI itemUI = btnObj.GetComponent<WeaponShopItemUI>();

                itemUI.Setup(data, currentGold, () =>
                {
                    // Okamžitá lokální odezva zamezující spamování tlačítka
                    itemUI.SetInteractable(false);
                    _localPlayerLogic.ClientBuyWeapon(index, data.GoldPrice);
                });

                _spawnedItems.Add(itemUI);
            }
        }

        UpdateSellButton(weaponManager);
    }

    private void UpdateSellButton(WeaponManager wm)
    {
        int currentWeaponIndex = wm.CurrentWeaponIndex.Value; // Ujisti se, že přistupuješ k .Value přes public property

        if (currentWeaponIndex != -1)
        {
            WeaponData currentData = wm.GetWeaponDataByIndex(currentWeaponIndex);
            if (currentData == null) return;

            int sellPrice = currentData.GoldPrice / 2;

            _sellButton.interactable = true;
            _sellButtonText.text = $"Sell Weapon ({sellPrice} G)";

            _sellButton.onClick.RemoveAllListeners();
            _sellButton.onClick.AddListener(() =>
            {
                // Okamžitá lokální odezva na click
                _sellButton.interactable = false;
                _sellButtonText.text = "Selling...";

                _localPlayerLogic.ClientSellWeapon(sellPrice);
            });
        }
        else
        {
            _sellButton.interactable = false;
            _sellButtonText.text = "No Weapon";
        }
    }
}