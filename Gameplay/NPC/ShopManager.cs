using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;

public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance; // Singleton jen pro UI přístup

    [Header("UI References")]
    [SerializeField] private GameObject _shopPanel;
    [SerializeField] private Transform _itemsContainer;
    [SerializeField] private GameObject _itemButtonPrefab; // Prefab musí mít WeaponShopItemUI
    [SerializeField] private Button _sellButton;
    [SerializeField] private TextMeshProUGUI _sellButtonText;

    private NPCInteractable _currentNpc;
    private PlayerShopLogic _localPlayerLogic; // Reference na konkrétního hráče
    private WeaponManager _monitoredWeaponManager;

    private void Awake()
    {
        Instance = this;
        _shopPanel.SetActive(false);
    }

    // Volá se z NPCInteractable
    public void OpenShop(NPCInteractable npc)
    {
        _currentNpc = npc;

        // 1. NAJDEME LOKÁLNÍHO HRÁČE (Bezpečnější než statická instance)
        if (NetworkManager.Singleton.LocalClient != null && NetworkManager.Singleton.LocalClient.PlayerObject != null)
        {
            _localPlayerLogic = NetworkManager.Singleton.LocalClient.PlayerObject.GetComponent<PlayerShopLogic>();
        }

        if (_localPlayerLogic == null)
        {
            Debug.LogError("[ShopManager] CHYBA: Nenalezen PlayerShopLogic na lokálním hráči!");
            return;
        }

        // --- PŘIHLÁŠENÍ K ODBĚRU ZMĚN ---
        _monitoredWeaponManager = _localPlayerLogic.GetComponent<WeaponManager>();
        if (_monitoredWeaponManager != null)
        {
            // Nasloucháme změně zbraně (předchozí hodnota, nová hodnota)
            _monitoredWeaponManager.CurrentWeaponIndex.OnValueChanged += HandleWeaponChanged;
        }

        RefreshShopUI();
        _shopPanel.SetActive(true);

        // Odemknout myš
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseShop()
    {
        // --- ODHLÁŠENÍ Z ODBĚRU (Důležité proti memory leakům) ---
        if (_monitoredWeaponManager != null)
        {
            _monitoredWeaponManager.CurrentWeaponIndex.OnValueChanged -= HandleWeaponChanged;
            _monitoredWeaponManager = null;
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


    private void RefreshShopUI()
    {
        // Vyčistit stará tlačítka
        foreach (Transform child in _itemsContainer) Destroy(child.gameObject);

        // Získat WeaponManager pro data zbraní
        var weaponManager = _localPlayerLogic.GetComponent<WeaponManager>();

        // Vygenerovat tlačítka pro zbraně, které NPC prodává
        foreach (int index in _currentNpc.WeaponIndexesForSale)
        {
            WeaponData data = weaponManager.GetWeaponDataByIndex(index);

            if (data != null)
            {
                GameObject btnObj = Instantiate(_itemButtonPrefab, _itemsContainer);
                WeaponShopItemUI itemUI = btnObj.GetComponent<WeaponShopItemUI>();

                // Nastavení tlačítka + Co se stane při kliknutí
                itemUI.Setup(data, () =>
                {
                    Debug.Log($"[UI] Kliknuto na {data.WeaponName}");
                    _localPlayerLogic.ClientBuyWeapon(index, data.GoldPrice);
                });
            }
        }

        UpdateSellButton(weaponManager);
    }

    private void UpdateSellButton(WeaponManager wm)
    {
        int currentWeaponIndex = wm._currentWeaponIndex.Value;

        if (currentWeaponIndex != -1)
        {
            WeaponData currentData = wm.GetWeaponDataByIndex(currentWeaponIndex);
            int sellPrice = currentData.GoldPrice / 2;

            _sellButton.interactable = true;
            _sellButtonText.text = $"Prodat zbraň ({sellPrice} G)";

            _sellButton.onClick.RemoveAllListeners();
            _sellButton.onClick.AddListener(() =>
            {
                _localPlayerLogic.ClientSellWeapon(sellPrice);
                CloseShop(); // Volitelné zavření po prodeji
            });
        }
        else
        {
            _sellButton.interactable = false;
            _sellButtonText.text = "Žádná zbraň";
        }
    }
}