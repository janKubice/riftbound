using UnityEngine;
using TMPro;

public class ShopTooltipUI : MonoBehaviour
{
    [Header("UI Elementy")]
    [SerializeField] private TMP_Text _headerText;
    [SerializeField] private TMP_Text _typeText;
    [SerializeField] private TMP_Text _descriptionText;
    [SerializeField] private RectTransform _rectTransform; // Pro posun u myši

    [Header("Nastavení")]
    [SerializeField] private Vector2 _offset = new Vector2(15f, -15f);

    public void Show(ShopItemData data)
    {
        gameObject.SetActive(true);
        _headerText.text = data.ItemName;
        _descriptionText.text = data.Description;

        // Rozlišení typu upgradu barvou a textem
        if (data.IsGlobalUpgrade)
        {
            _typeText.text = "GLOBAL UPGRADE";
            _typeText.color = Color.cyan; // Nebo zlatá
        }
        else
        {
            _typeText.text = "WEAPON MOD";
            _typeText.color = Color.white;
        }

        UpdatePosition();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void Update()
    {
        if (gameObject.activeSelf)
        {
            UpdatePosition();
        }
    }

    private void UpdatePosition()
    {
        // Tooltip sleduje myš
        Vector2 mousePos = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        _rectTransform.position = mousePos + _offset;
        
        // Zde by se dala přidat logika "Clamp to Screen", aby tooltip nevyjížděl z obrazovky
    }
}