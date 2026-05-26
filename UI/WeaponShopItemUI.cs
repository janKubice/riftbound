using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class WeaponShopItemUI : MonoBehaviour
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _infoText; 
    [SerializeField] private Button _buyButton;

    [Header("Demo Lockdown")]
    [SerializeField] private GameObject _lockedOverlay;

    private WeaponData _assignedData;

    // Metoda pro nastavení tlačítka
    public void Setup(WeaponData data, int currentGold, Action onBuyClick)
    {
        _assignedData = data;

        // Nastavení základních vizuálů
        if (data.Icon != null) _iconImage.sprite = data.Icon;
        _infoText.text = data.GetRichTextStats();

        _buyButton.onClick.RemoveAllListeners();
        _buyButton.onClick.AddListener(() => onBuyClick?.Invoke());

        UpdateAffordability(currentGold);
    }

    // Volá se ze ShopManageru, když se změní stav hráčova konta
    public void UpdateAffordability(int currentGold)
    {
        if (_assignedData == null) return;

        if (!_assignedData.InDemo)
        {
            // Mimo demo
            if (_lockedOverlay != null) _lockedOverlay.SetActive(true);
            _buyButton.interactable = false;
            _infoText.text = $"<color=#888888>{_assignedData.WeaponName}</color>\n<color=red>NOT IN DEMO</color>";
        }
        else
        {
            // Ve hře - kontrola peněz
            if (_lockedOverlay != null) _lockedOverlay.SetActive(false);
            _buyButton.interactable = (currentGold >= _assignedData.GoldPrice);
        }
    }

    // Pomocná metoda pro okamžitou lokální blokaci po kliknutí
    public void SetInteractable(bool state)
    {
        _buyButton.interactable = state;
    }
}