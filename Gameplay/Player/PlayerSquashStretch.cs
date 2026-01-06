using UnityEngine;

public class PlayerSquashStretch : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private PlayerController _controller; // Odkaz na controller (pro eventy)
    [SerializeField] private float _jumpStretch = 1.2f;    // Natažení při skoku (Y > 1)
    [SerializeField] private float _landSquash = 0.7f;     // Zmáčknutí při dopadu (Y < 1)
    [SerializeField] private float _returnSpeed = 10f;     // Rychlost návratu do normálu

    private Vector3 _originalScale;
    private Vector3 _targetScale;

    private void Start()
    {
        _originalScale = transform.localScale;
        _targetScale = _originalScale;

        // Automaticky najít controller v rodiči, pokud chybí
        if (_controller == null) _controller = GetComponentInParent<PlayerController>();
    }

    private void Update()
    {
        // Kontrola skoku a dopadu
        CheckInputs();

        // Plynulý návrat k původnímu měřítku
        transform.localScale = Vector3.Lerp(transform.localScale, _targetScale, Time.deltaTime * _returnSpeed);
        
        // Plynulý návrat targetu do normálu (aby se efekt utlumil)
        _targetScale = Vector3.Lerp(_targetScale, _originalScale, Time.deltaTime * 5f);
    }

    private void CheckInputs()
    {
        if (_controller == null) return;

        // Tady potřebujeme vědět o skoku/dopadu. 
        // V ideálním světě bys měl C# Eventy (OnJump, OnLand).
        // PRO TEĎ: Detekce změny stavu přes Animator nebo Velocity (hack, ale rychlý).
        
        // Lepší řešení: Jdi do PlayerController.cs a přidej eventy.
        // public event System.Action OnJumpEvent;
        // public event System.Action OnLandEvent;
        // A zavolej je v HandleJump() a OnLand().
    }

    // Tyto metody zavolej z PlayerControlleru (přes Unity Event nebo přímo)
    public void TriggerJumpSquash()
    {
        // Natažení do výšky (Y), zúžení do šířky (X, Z)
        _targetScale = new Vector3(_originalScale.x * (1/_jumpStretch), _originalScale.y * _jumpStretch, _originalScale.z * (1/_jumpStretch));
    }

    public void TriggerLandSquash()
    {
        // Zmáčknutí do výšky (Y), rozšíření do šířky (X, Z)
        _targetScale = new Vector3(_originalScale.x * (1/_landSquash), _originalScale.y * _landSquash, _originalScale.z * (1/_landSquash));
    }
}