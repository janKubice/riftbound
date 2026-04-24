using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // Nutné pro detekci UI událostí

public class UISoundElement : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler
{
    [Header("Audio Overrides")]
    [Tooltip("Nech prázdné pro výchozí zvuk z UIAudioManageru")]
    [SerializeField] private AudioClip _clickSound;
    
    [Tooltip("Nech prázdné pro výchozí zvuk z UIAudioManageru")]
    [SerializeField] private AudioClip _hoverSound;

    [Header("Settings")]
    [SerializeField] private bool _enableHover = true;
    [SerializeField] private bool _enableClick = true;

    private Button _button;

    private void Awake()
    {
        _button = GetComponent<Button>();
        if (_button == null)
        {
            Debug.LogWarning("UISoundElement je připojen k objektu bez Button komponenty. Zvuky nebudou fungovat.");
        }
    }

    private bool IsInteractable()
    {
        return _button == null || _button.interactable;
    }

    // Voláno automaticky při kliknutí na objekt
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_enableClick || !IsInteractable()) return;
        
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayClick(_clickSound);
        }
    }

    // Voláno automaticky při najetí myší
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_enableHover || !IsInteractable()) return;

        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayHover(_hoverSound);
        }
    }
}