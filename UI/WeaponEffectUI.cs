using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using System;

public class WeaponEffectUI : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private Button _upBtn;
    [SerializeField] private Button _downBtn;
    [SerializeField] private Button _sellBtn;
    [SerializeField] private TMP_Text _sellBtnText; 

    private HitEffect _effect;

    public void Setup(HitEffect effect, int index, int totalCount, Action<int, int> onSwap, Action<int> onRemove, int refundValue = 0)
    {
        _effect = effect;
        _nameText.text = effect.EffectName;

        _upBtn.interactable = index > 0;
        _upBtn.onClick.RemoveAllListeners();
        _upBtn.onClick.AddListener(() => onSwap(index, index - 1));

        _downBtn.interactable = index < totalCount - 1;
        _downBtn.onClick.RemoveAllListeners();
        _downBtn.onClick.AddListener(() => onSwap(index, index + 1));

        _sellBtn.onClick.RemoveAllListeners();
        _sellBtn.onClick.AddListener(() => onRemove(index));

        // Dynamická změna textu na tlačítku
        _sellBtnText.text = refundValue > 0 ? $"+{refundValue} G" : "Remove";
        
        gameObject.SetActive(true);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        ShopTooltipUI.Instance.Show(_effect);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ShopTooltipUI.Instance.Hide();
    }
}