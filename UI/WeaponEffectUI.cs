using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System;

public class WeaponEffectUI : MonoBehaviour
{
    [SerializeField] private TMP_Text _nameText;
    [SerializeField] private Button _upBtn;
    [SerializeField] private Button _downBtn;
    [SerializeField] private Button _sellBtn;

    public void Setup(HitEffect effect, int index, int totalCount, Action<int, int> onSwap, Action<int> onRemove)
    {
        _nameText.text = effect.name; // Nebo effect.DisplayName

        // Logika tlačítek
        _upBtn.interactable = index > 0;
        _downBtn.interactable = index < totalCount - 1;

        _upBtn.onClick.RemoveAllListeners();
        _upBtn.onClick.AddListener(() => onSwap(index, index - 1));

        _downBtn.onClick.RemoveAllListeners();
        _downBtn.onClick.AddListener(() => onSwap(index, index + 1));

        _sellBtn.onClick.RemoveAllListeners();
        _sellBtn.onClick.AddListener(() => onRemove(index));
        
        gameObject.SetActive(true);
    }
}