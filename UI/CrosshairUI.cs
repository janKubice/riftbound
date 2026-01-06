using UnityEngine;
using UnityEngine.UI;

public class CrosshairUI : MonoBehaviour
{
    [SerializeField] private Image _crosshairImage;
    
    // Můžeme měnit barvu při míření na nepřítele
    public void SetColor(Color color)
    {
        if(_crosshairImage != null) _crosshairImage.color = color;
    }
}