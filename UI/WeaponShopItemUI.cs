using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class WeaponShopItemUI : MonoBehaviour
{
    [SerializeField] private Image _iconImage;
    [SerializeField] private TextMeshProUGUI _infoText; // Např. Název + Cena
    [SerializeField] private Button _buyButton;

    // Metoda pro nastavení tlačítka
    public void Setup(WeaponData data, Action onBuyClick)
    {
        // Nastavení vizuálu
        if (data.Icon != null) _iconImage.sprite = data.Icon;
        
        _infoText.text = data.GetRichTextStats();

        // Nastavení kliknutí
        _buyButton.onClick.RemoveAllListeners();
        _buyButton.onClick.AddListener(() => onBuyClick?.Invoke());
    }
}