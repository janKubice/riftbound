using UnityEngine;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class UpgradeSlotUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
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
    [SerializeField] private Color _maxedColor = Color.gray;

    [Header("Demo Lockdown")]
    [SerializeField] private GameObject _lockedOverlay; // Reference na Overlay objekt

    private int _upgradeIndex;
    private PlayerProgression _playerProgression;
    private StatUpgradeData _data;
    private UpgradeShopUI _parentShop;

    // Inicializace (volá Shop Manager při startu)
    public void Initialize(int index, PlayerProgression progression, StatUpgradeData data, UpgradeShopUI parentShop)
    {
        _upgradeIndex = index;
        _playerProgression = progression;
        _data = data;
        _parentShop = parentShop;

        // Pokud není v demu, aktivujeme overlay a vypneme interaktivitu
        if (!data.inDemo)
        {
            if (_lockedOverlay != null) _lockedOverlay.SetActive(true);
            _buyButton.interactable = false;

            // Nastavení základních textů i pro zamčený prvek
            _nameText.text = data.UpgradeName;
            if (data.Icon != null) _iconImage.sprite = data.Icon;
            return;
        }

        // Standardní inicializace pro dostupné upgrady
        if (_lockedOverlay != null) _lockedOverlay.SetActive(false);

        _nameText.text = data.UpgradeName;
        if (data.Icon != null) _iconImage.sprite = data.Icon;

        _buyButton.onClick.RemoveAllListeners();
        _buyButton.onClick.AddListener(() =>
        {
            OnBuyClicked();
        });

        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_data == null || _parentShop == null) return;

        int currentLevel = _playerProgression != null ? _playerProgression.GetUpgradeLevel(_upgradeIndex) : 0;
        _parentShop.ShowTooltip(_data, currentLevel);
    }

    // Voláno enginem při odjetí myší
    public void OnPointerExit(PointerEventData eventData)
    {
        if (_parentShop == null) return;
        _parentShop.HideTooltip();
    }

    public void Refresh()
    {
        if (_playerProgression == null || _data == null || !_data.inDemo) return;

        // 1. Získání dat
        int currentLevel = _playerProgression.GetUpgradeLevel(_upgradeIndex);
        int playerXP = _playerProgression.CurrentXP.Value;
        bool isMaxed = _data.IsMaxLevel(currentLevel);

        // 2. Aktualizace Levelu
        _levelText.text = isMaxed ? "MAX" : $"Lvl {currentLevel}";

        // 3. Aktualizace Bonusu (použijeme novou metodu z Data)
        // Zobrazí např.: "Bonus: 10 -> 15"
        _valueText.text = _data.GetValuePreview(currentLevel);

        // 4. Logika Tlačítka a Ceny
        if (isMaxed)
        {
            // STAV: MAXIMÁLNÍ LEVEL
            _costText.text = "---";
            _costText.color = _maxedColor;
            _buyButton.interactable = false;

            // Volitelně: změnit pozadí tlačítka na šedou
            if (_buttonBackground != null) _buttonBackground.color = Color.gray;
        }
        else
        {
            // STAV: LZE UPGRADOVAT
            int currentCost = _data.GetCost(currentLevel);
            _costText.text = $"{currentCost} XP";

            bool canAfford = playerXP >= currentCost;

            // Tlačítko je aktivní jen pokud máme dost XP
            _buyButton.interactable = canAfford;

            // Barva textu podle ceny
            _costText.color = canAfford ? _affordableColor : _tooExpensiveColor;

            // Reset barvy pozadí (pokud jsme ji měnili u MAX)
            if (_buttonBackground != null) _buttonBackground.color = Color.white;
        }
    }

    private void OnBuyClicked()
    {
        int currentLevel = _playerProgression.GetUpgradeLevel(_upgradeIndex);
        if (_data.IsMaxLevel(currentLevel)) return;

        Debug.Log($"[UpgradeSlot] Buying: {_data.UpgradeName}");
        _playerProgression.TryBuyUpgrade(_upgradeIndex);
    }
}