using UnityEngine;

public class PlayerProceduralLean : MonoBehaviour
{
    [Header("Nastavení")]
    [Tooltip("Odkaz na kořenový PlayerController (v rodiči).")]
    [SerializeField] private PlayerController _controller;

    [Header("Náklon do stran (Banking)")]
    [SerializeField] private float _leanAmount = 2.5f; // Síla náklonu
    [SerializeField] private float _leanSmoothing = 10.0f; // Rychlost interpolace

    [Header("Náklon dopředu (Running)")]
    [SerializeField] private float _runTiltAmount = 1.5f; // Předklon při běhu

    private Vector3 _currentRotation;
    private Vector3 _targetRotation;

    private void Start()
    {
        // Automaticky najde controller v rodiči, pokud není přiřazen
        if (_controller == null)
        {
            _controller = GetComponentInParent<PlayerController>();
        }
    }

    private void Update()
    {
        if (_controller == null) return;

        // 1. Získáme rychlost hráče
        Vector3 velocity = _controller.Velocity;

        // 2. Převedeme globální rychlost na lokální (relativně k otočení hráče)
        // X = úkroky do stran, Z = pohyb dopředu/dozadu
        Vector3 localVelocity = transform.parent.InverseTransformDirection(velocity);

        // 3. Vypočítáme cílové úhly
        // Roll (Z): Pokud jdu doleva (negativní X), chci kladnou rotaci Z (doleva).
        // Pitch (X): Pokud jdu dopředu (pozitivní Z), chci kladnou rotaci X (předklon).
        
        // Poznámka: Osa rotace závisí na pivotu modelu. 
        // Většinou: +Z rotace je náklon doleva, -Z doprava.
        float targetRoll = -localVelocity.x * _leanAmount; 
        float targetPitch = localVelocity.z * _runTiltAmount;

        // 4. Vyhlazení (Smoothing)
        // Lerpujeme směrem k cílovým hodnotám
        _currentRotation.z = Mathf.Lerp(_currentRotation.z, targetRoll, Time.deltaTime * _leanSmoothing);
        _currentRotation.x = Mathf.Lerp(_currentRotation.x, targetPitch, Time.deltaTime * _leanSmoothing);

        // 5. Aplikace rotace
        // Používáme localRotation, abychom neovlivnili globální orientaci
        transform.localRotation = Quaternion.Euler(_currentRotation.x, 0, _currentRotation.z);
    }
}