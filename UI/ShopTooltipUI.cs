using UnityEngine;
using TMPro;

public class ShopTooltipUI : MonoBehaviour
{
    public static ShopTooltipUI Instance { get; private set; }

    [Header("UI Elementy")]
    [SerializeField] private TMP_Text _headerText;
    [SerializeField] private TMP_Text _typeText;
    [SerializeField] private TMP_Text _descriptionText;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        
        Hide();
    }

    public void Show(ShopItemData data)
    {
        gameObject.SetActive(true);
        _headerText.text = data.ItemName;
        _headerText.color = data.GetRarityColor();
        
        _descriptionText.text = data.EffectPayload.GetDescription();
        
        _typeText.text = data.IsGlobalUpgrade ? "GLOBAL UPGRADE" : "WEAPON MOD";
        _typeText.color = data.IsGlobalUpgrade ? Color.cyan : Color.white;
    }

    public void Show(HitEffect effect)
    {
        gameObject.SetActive(true);
        _headerText.text = effect.EffectName;
        _headerText.color = Color.white; 
        
        _descriptionText.text = effect.GetDescription();
        
        _typeText.text = "EQUIPPED EFFECT";
        _typeText.color = Color.yellow;
    }

    public void Hide() => gameObject.SetActive(false);
}