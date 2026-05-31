using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class RewardChoiceCardUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image _background;
    [SerializeField] private Image _icon;
    [SerializeField] private TMP_Text _rarityText;
    [SerializeField] private TMP_Text _titleText;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField] private Button _button;

    private int _choiceIndex;
    private Action<int> _onSelected;

    public void Setup(
        int choiceIndex,
        RewardChoiceDefinition definition,
        ItemRarity rarity,
        int amount,
        float statValue,
        Action<int> onSelected)
    {
        _choiceIndex = choiceIndex;
        _onSelected = onSelected;

        Color rarityColor = RewardRarityUtility.GetColor(rarity);

        if (_background != null)
            _background.color = new Color(rarityColor.r, rarityColor.g, rarityColor.b, 1.0f);

        if (_icon != null)
        {
            _icon.sprite = definition.Icon;
            _icon.enabled = definition.Icon != null;
        }

        if (_rarityText != null)
        {
            _rarityText.text = RewardRarityUtility.GetLabel(rarity);
            _rarityText.color = rarityColor;
        }

        if (_titleText != null)
            _titleText.text = definition.BuildTitle(rarity);

        if (_descriptionText != null)
        {
            _descriptionText.text = definition.BuildDescription(rarity);
            _descriptionText.color = Color.white;

        }
            
        if (_button != null)
        {
            _button.onClick.RemoveAllListeners();
            _button.onClick.AddListener(() => _onSelected?.Invoke(_choiceIndex));
        }
    }
}