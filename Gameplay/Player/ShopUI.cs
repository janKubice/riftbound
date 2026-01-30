using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

public class ShopUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private Transform _shopContainer;
    [SerializeField] private Transform _weaponEffectsContainer;
    
    [Header("Prefabs")]
    [SerializeField] private ShopItemUI _shopItemPrefab;      // ZMĚNA: Typ je nyní náš skript
    [SerializeField] private WeaponEffectUI _effectSlotPrefab; // ZMĚNA: Typ je nyní náš skript

    [Header("New UI Elements")]
    [SerializeField] private ShopTooltipUI _tooltip;

    // Pooling seznamy
    private List<ShopItemUI> _spawnedShopSlots = new List<ShopItemUI>();
    private List<WeaponEffectUI> _spawnedEffectSlots = new List<WeaponEffectUI>();

    private ShopInteractable _currentShop;
    private WeaponManager _localWeaponManager;
    private PlayerProgression _localProgression;
    private PlayerShopController _shopController;

    // Cache aktuálních dat pro refresh
    private List<ShopItemData> _currentShopItems;

    private void Start()
    {
        _panel.SetActive(false);
    }

    public void OpenShop(ShopInteractable shop, List<ShopItemData> items)
    {
        _currentShop = shop;
        _currentShopItems = items;

        // Bezpečné získání lokálního hráče
        var localPlayer = NetworkManager.Singleton.SpawnManager.GetLocalPlayerObject();
        if (localPlayer == null) return;

        _localWeaponManager = localPlayer.GetComponent<WeaponManager>();
        _localProgression = localPlayer.GetComponent<PlayerProgression>();
        _shopController = localPlayer.GetComponent<PlayerShopController>();

        // Subscribe eventů pro reaktivní UI (předpokládám, že je máš)
        // Pokud ne, doporučuji je přidat do PlayerProgression/WeaponManager
        // _localProgression.OnGoldChanged += RefreshVisuals;
        // _localWeaponManager.OnEffectsChanged += RefreshVisuals;

        _panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        RefreshVisuals();
    }

    public void CloseShop()
    {
        // Unsubscribe eventů
        // if (_localProgression != null) _localProgression.OnGoldChanged -= RefreshVisuals;
        if (_tooltip != null) _tooltip.Hide();
        _panel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Voláme, kdykoliv se něco změní (kliknutí, nákup, prodej)
    private void RefreshVisuals()
    {
        if (_currentShopItems != null) RefreshShopList(_currentShopItems);
        RefreshWeaponEffects();
    }

    // --- OPTIMALIZOVANÝ LISTING (Pooling Pattern) ---
    
    private void RefreshShopList(List<ShopItemData> items)
    {
        // 1. Pooling (stejný jako minule)
        while (_spawnedShopSlots.Count < items.Count)
        {
            ShopItemUI newSlot = Instantiate(_shopItemPrefab, _shopContainer); // Upravený prefab
            _spawnedShopSlots.Add(newSlot);
        }

        // 2. Setup slotů - PŘIDÁVÁME Tooltip callbacky
        for (int i = 0; i < items.Count; i++)
        {
            _spawnedShopSlots[i].Setup(
                items[i], 
                i, 
                _localProgression.Gold.Value,
                OnBuyClicked,       // Akce nákupu
                OnSlotHoverEnter,   // Akce najetí myši -> Zobraz Tooltip
                OnSlotHoverExit     // Akce odjetí myši -> Skryj Tooltip
            );
        }

        // 3. Skrytí přebytečných
        for (int i = items.Count; i < _spawnedShopSlots.Count; i++)
        {
            _spawnedShopSlots[i].gameObject.SetActive(false);
        }
    }

    // --- Tooltip Logic ---
    private void OnSlotHoverEnter(ShopItemData data)
    {
        if (_tooltip != null) _tooltip.Show(data);
    }

    private void OnSlotHoverExit()
    {
        if (_tooltip != null) _tooltip.Hide();
    }

    public void RefreshWeaponEffects()
    {
        var effects = _localWeaponManager.CurrentRuntimeStats.OnHitEffects;
        if (effects == null) effects = new List<HitEffect>(); // Pojistka proti null

        // 1. Zajistíme sloty
        while (_spawnedEffectSlots.Count < effects.Count)
        {
            WeaponEffectUI newSlot = Instantiate(_effectSlotPrefab, _weaponEffectsContainer);
            _spawnedEffectSlots.Add(newSlot);
        }

        // 2. Nastavíme
        for (int i = 0; i < effects.Count; i++)
        {
            _spawnedEffectSlots[i].Setup(effects[i], i, effects.Count, OnSwapClicked, OnSellClicked);
        }

        // 3. Skryjeme zbytek
        for (int i = effects.Count; i < _spawnedEffectSlots.Count; i++)
        {
            _spawnedEffectSlots[i].gameObject.SetActive(false);
        }
    }

    // --- INTERACTION CALLBACKS ---

    private void OnBuyClicked(int index, ShopItemData item)
    {
        if (_localProgression.Gold.Value < item.GoldCost) return;
        
        if (_shopController != null)
        {
            // Optimistic Update: Můžeme přehrát zvuk hned, i když server ještě nepotvrdil
            _shopController.BuyItemTransactionServerRpc(index, _currentShop);
            
            // POZNÁMKA: Správně by UI nemělo reagovat hned, ale počkat, až se vrátí změna ze serveru
            // (např. přes NetworkVariable OnValueChanged), která zavolá RefreshVisuals.
            // Pro jednoduchost volám refresh po malém zpoždění nebo spoléhám na eventy.
        }
    }

    private void OnSwapClicked(int indexA, int indexB)
    {
        _shopController?.SwapEffectsServerRpc(indexA, indexB);
        // RefreshVisuals se ideálně zavolá přes callback z WeaponManageru
    }

    private void OnSellClicked(int index)
    {
        _shopController?.RemoveEffectServerRpc(index);
    }
}