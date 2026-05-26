using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems; // Důležité pro Hover eventy
using System;

public class ShopItemUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [Header("UI Reference")]
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _priceText;
    [SerializeField] private Button _buyButton;
    [SerializeField] private Image _iconImage;
    [SerializeField] private GameObject _globalBadge; // Malá ikonka/rámeček značící "Global"
    [SerializeField] private GameObject _lockedOverlay; // Překryv pro zobrazení zamčeného itemu

    private ShopItemData _data;
    private Action<int, ShopItemData> _onBuyClick;
    private Action<ShopItemData> _onHoverEnter; // Callback pro zobrazení tooltipu
    private Action _onHoverExit;                // Callback pro skrytí
    private int _index;

    public void Setup(ShopItemData item, int index, int playerGold,
                      Action<int, ShopItemData> onBuy,
                      Action<ShopItemData> onHover,
                      Action onHoverExit)
    {
        _data = item;
        _index = index;
        _onBuyClick = onBuy;
        _onHoverEnter = onHover;
        _onHoverExit = onHoverExit;

        // 1. Nastavení Textů
        _nameText.text = item.ItemName;
        _priceText.text = $"{item.GoldCost} G";

        // 2. Nastavení Ikony (pokud existuje)
        if (item.Icon != null)
        {
            _iconImage.sprite = item.Icon;
            _iconImage.enabled = true;
        }
        else
        {
            _iconImage.enabled = false; // Skryjeme prázdný Image
        }

        // 3. Vizuální indikace typu (Global vs Weapon)
        if (_globalBadge != null)
        {
            _globalBadge.SetActive(item.IsGlobalUpgrade);
        }

        // 4. Logika nákupu (Cena)
        bool isInDemo = item.InDemo;
        bool canAfford = playerGold >= item.GoldCost;
        bool canBuy = isInDemo && canAfford;

        _buyButton.interactable = canAfford;
        _priceText.color = canAfford ? Color.white : Color.red;

        _buyButton.onClick.RemoveAllListeners();
        _buyButton.onClick.AddListener(() => _onBuyClick(_index, _data));

        if (_lockedOverlay != null)
            _lockedOverlay.SetActive(!isInDemo);


        _buyButton.onClick.RemoveAllListeners();
        if (canBuy)
            _buyButton.onClick.AddListener(() => _onBuyClick(_index, _data));

        gameObject.SetActive(true);
    }

    // Uvnitř třídy ShopItemUI.cs přidejte novou metodu Setup:

    public void SetupWithDynamicPrice(ShopItemData item, int index, int playerGold, int calculatedPrice,
                      Action<int, ShopItemData> onBuy,
                      Action<ShopItemData> onHover,
                      Action onHoverExit)
    {
        _data = item;
        _index = index;
        _onBuyClick = onBuy;
        _onHoverEnter = onHover;
        _onHoverExit = onHoverExit;

        _nameText.text = item.ItemName;
        _priceText.text = $"{calculatedPrice} G"; // Použití dynamické ceny

        if (item.Icon != null)
        {
            _iconImage.sprite = item.Icon;
            _iconImage.enabled = true;
        }
        else
        {
            _iconImage.enabled = false;
        }

        if (_globalBadge != null)
        {
            _globalBadge.SetActive(item.IsGlobalUpgrade);
        }

        bool isInDemo = item.InDemo;
        bool canAfford = playerGold >= calculatedPrice; // Ověření vůči dynamické ceně
        bool canBuy = isInDemo && canAfford;

        _buyButton.interactable = canAfford;
        _priceText.color = canAfford ? Color.white : Color.red;

        if (_lockedOverlay != null)
            _lockedOverlay.SetActive(!isInDemo);

        _buyButton.onClick.RemoveAllListeners();
        if (canBuy)
            _buyButton.onClick.AddListener(() => _onBuyClick(_index, _data));

        gameObject.SetActive(true);
    }

    // Unity Event System metody
    public void OnPointerEnter(PointerEventData eventData)
    {
        ShopTooltipUI.Instance.Show(_data);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ShopTooltipUI.Instance.Hide();
    }
}