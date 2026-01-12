using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class UpgradeSlotUI : MonoBehaviour
{
    [Header("UI Elementy")]
    [SerializeField] private Image _iconImage;
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private TMP_Text _levelText;
    [SerializeField] private TMP_Text _valueText; // Např. "+10% Damage"
    [SerializeField] private TMP_Text _costText;
    [SerializeField] private Button _buyButton;
    [SerializeField] private Image _buttonBackground; // Pro změnu barvy (šedá/aktivní)

    [Header("Barvy")]
    [SerializeField] private Color _affordableColor = Color.green;
    [SerializeField] private Color _tooExpensiveColor = Color.red;

    private int _upgradeIndex;
    private PlayerProgression _playerProgression;
    private StatUpgradeData _data;

    // Inicializace (volá Shop Manager při startu)
    public void Initialize(int index, PlayerProgression progression, StatUpgradeData data)
    {
        _upgradeIndex = index;
        _playerProgression = progression;
        _data = data;

        // Statické věci (se nemění)
        _nameText.text = data.UpgradeName;
        if (data.Icon != null) _iconImage.sprite = data.Icon;

        // Kliknutí na tlačítko
        _buyButton.onClick.RemoveAllListeners();
        _buyButton.onClick.AddListener(() => {
            Debug.Log($"[UpgradeSlot] KLIKNUTO NA: {data.UpgradeName}");
            OnBuyClicked();
        });

        Refresh();
    }

    public void Refresh()
    {
        if (_playerProgression == null) return;

        // Získání aktuálních dat
        int currentLevel = _playerProgression.GetUpgradeLevel(_upgradeIndex);
        int currentCost = _data.GetCost(currentLevel);
        int playerXP = _playerProgression.CurrentXP.Value;
        float currentBonus = _data.GetTotalBonus(currentLevel);

        // Aktualizace Textů
        _levelText.text = $"Lvl {currentLevel}";
        _costText.text = $"{currentCost} XP";
        
        // Zobrazíme, co ten stat aktuálně dává (např. Speed: +2.5)
        _valueText.text = $"Bonus: +{currentBonus:F1}";

        // Logika Tlačítka (Máme na to?)
        bool canAfford = playerXP >= currentCost;
        _buyButton.interactable = canAfford;
        _costText.color = canAfford ? _affordableColor : _tooExpensiveColor;
    }

    private void OnBuyClicked()
    {
        Debug.Log("Buying: " + _nameText);
        _playerProgression.TryBuyUpgrade(_upgradeIndex);
        // UI se refreshne automaticky přes eventy v Shop Manageru
    }
}