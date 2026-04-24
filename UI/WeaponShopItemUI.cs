using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class WeaponShopItemUI : MonoBehaviour
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _infoText; // Např. Název + Cena
    [SerializeField] private Button _buyButton;

    [Header("Demo Lockdown")]
    [SerializeField] private GameObject _lockedOverlay;

    // Metoda pro nastavení tlačítka
    public void Setup(WeaponData data, Action onBuyClick)
    {
        // 1. Nastavení základních vizuálů
        if (data.Icon != null) _iconImage.sprite = data.Icon;
        _infoText.text = data.GetRichTextStats();

        // 2. Demo Lockdown Logika
        if (!data.InDemo)
        {
            // STAV: ZAMČENO (Mimo demo)
            if (_lockedOverlay != null) _lockedOverlay.SetActive(true);
            _buyButton.interactable = false;
            
            // Volitelně: Modifikace textu pro zamčenou zbraň
            _infoText.text = $"<color=#888888>{data.WeaponName}</color>\n<color=red>NOT IN DEMO</color>";
        }
        else
        {
            // STAV: DOSTUPNÉ
            if (_lockedOverlay != null) _lockedOverlay.SetActive(false);
            _buyButton.interactable = true;

            _buyButton.onClick.RemoveAllListeners();
            _buyButton.onClick.AddListener(() => onBuyClick?.Invoke());
        }
    }
}