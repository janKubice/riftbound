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
        bool canAfford = playerGold >= item.GoldCost;
        _buyButton.interactable = canAfford;
        _priceText.color = canAfford ? Color.white : Color.red;

        _buyButton.onClick.RemoveAllListeners();
        _buyButton.onClick.AddListener(() => _onBuyClick(_index, _data));
        
        gameObject.SetActive(true);
    }

    // Unity Event System metody
    public void OnPointerEnter(PointerEventData eventData)
    {
        _onHoverEnter?.Invoke(_data);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        _onHoverExit?.Invoke();
    }
}