using UnityEngine;
using UnityEngine.EventSystems; // Nutné pro detekci UI událostí

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

    // Voláno automaticky při kliknutí na objekt
    public void OnPointerClick(PointerEventData eventData)
    {
        if (!_enableClick) return;
        
        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayClick(_clickSound);
        }
    }

    // Voláno automaticky při najetí myší
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (!_enableHover) return;

        if (UIAudioManager.Instance != null)
        {
            UIAudioManager.Instance.PlayHover(_hoverSound);
        }
    }
}