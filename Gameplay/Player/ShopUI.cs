using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using TMPro;

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
    [Tooltip("Text pro zobrazení aktuálních XP")]
    [SerializeField] private TextMeshProUGUI _xpText;

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

    private void OnDestroy()
    {
        // Bezpečnostní pojistka pro případ, že by se scéna změnila nebo byl objekt zničen s otevřeným UI
        if (_panel != null && _panel.activeSelf)
        {
            Time.timeScale = 1f;
        }
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

        if (_localProgression != null)
        {
            _localProgression.Gold.OnValueChanged += OnResourcesChanged;
            _localProgression.CurrentXP.OnValueChanged += OnResourcesChanged;
        }

        _panel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Kontrola singleplayeru (hráč je v relaci sám) -> Pozastavit hru
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count == 1)
        {
            Time.timeScale = 0f;
        }

        RefreshVisuals();
        RefreshResourceTexts();
    }

    public void CloseShop()
    {
        // Unsubscribe eventů
        if (_localProgression != null)
        {
            _localProgression.Gold.OnValueChanged -= OnResourcesChanged;
            _localProgression.CurrentXP.OnValueChanged -= OnResourcesChanged;
        }
        if (_tooltip != null) _tooltip.Hide();
        _panel.SetActive(false);
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Obnovit herní čas, pokud jsme v singleplayeru
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.ConnectedClientsIds.Count == 1)
        {
            Time.timeScale = 1f;
        }
    }

    // Voláme, kdykoliv se něco změní (kliknutí, nákup, prodej)
    private void RefreshVisuals()
    {
        if (_currentShopItems != null) RefreshShopList(_currentShopItems);
        RefreshWeaponEffects();
    }

    private void OnResourcesChanged(int previousValue, int newValue)
    {
        // Aktualizujeme texty nahoře
        RefreshResourceTexts();

        // Aktualizujeme i samotné itemy (např. aby tlačítko nákupu zešedlo, pokud už nemám dost peněz)
        if (_currentShopItems != null)
        {
            RefreshShopList(_currentShopItems);
        }
    }

    private void RefreshResourceTexts()
    {
        if (_localProgression == null) return;

        if (_xpText != null)
        {
            _xpText.text = $"{_localProgression.Gold.Value:N0} <color=#AAAAAA>XP</color>";
        }
    }

    // --- OPTIMALIZOVANÝ LISTING (Pooling Pattern) ---

    private void RefreshShopList(List<ShopItemData> items)
    {
        while (_spawnedShopSlots.Count < items.Count)
        {
            ShopItemUI newSlot = Instantiate(_shopItemPrefab, _shopContainer);
            _spawnedShopSlots.Add(newSlot);
        }

        int totalEffectsCount = 0;
        var currentEffects = _localWeaponManager?.CurrentRuntimeStats.OnHitEffects;

        if (currentEffects != null)
        {
            totalEffectsCount = currentEffects.Count;
        }

        for (int i = 0; i < items.Count; i++)
        {
            int duplicateCount = 0;

            // Zjištění počtu duplikátů pro konkrétní iterovaný item
            if (currentEffects != null && items[i].EffectPayload != null)
            {
                // Předpokládá se, že porovnáváš instance ScriptableObjectů.
                // Pokud se instancují kopie, použij porovnání přes unikátní ID.
                foreach (var effect in currentEffects)
                {
                    if (effect == items[i].EffectPayload)
                    {
                        duplicateCount++;
                    }
                }
            }

            int dynamicPrice = items[i].GetDynamicPrice(totalEffectsCount, duplicateCount);

            _spawnedShopSlots[i].SetupWithDynamicPrice(
                items[i],
                i,
                _localProgression.Gold.Value,
                dynamicPrice,
                OnBuyClicked,
                OnSlotHoverEnter,
                OnSlotHoverExit
            );
        }

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
        if (item == null) return;
        if (!item.InDemo) return;

        int totalEffectsCount = 0;
        int duplicateCount = 0;

        var currentEffects = _localWeaponManager?.CurrentRuntimeStats.OnHitEffects;
        if (currentEffects != null)
        {
            totalEffectsCount = currentEffects.Count;

            if (item.EffectPayload != null)
            {
                foreach (var effect in currentEffects)
                {
                    if (effect == item.EffectPayload)
                    {
                        duplicateCount++;
                    }
                }
            }
        }

        // Výpočet ceny kombinující celkový počet slotů i specifické duplikáty
        int realPrice = item.GetDynamicPrice(totalEffectsCount, duplicateCount);

        if (_localProgression.Gold.Value < realPrice) return;

        if (_shopController != null)
        {
            _shopController.BuyItemTransactionServerRpc(index, _currentShop);

            // Okamžitý lokální "odhadovaný" update (Optimistic UI), aby se předešlo lagu
            Invoke(nameof(RefreshVisuals), 0.05f);
        }

        RefreshVisuals();
        RefreshResourceTexts();
    }

    private void OnSwapClicked(int indexA, int indexB)
    {
        _shopController?.SwapEffectsServerRpc(indexA, indexB);
        // RefreshVisuals se ideálně zavolá přes callback z WeaponManageru
    }

    private void OnSellClicked(int index)
    {
        _shopController?.RemoveEffectServerRpc(index);
        RefreshVisuals();
        RefreshResourceTexts();
    }
}