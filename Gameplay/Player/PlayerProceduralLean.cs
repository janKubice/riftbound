using UnityEngine;

public class PlayerProceduralLean : MonoBehaviour
{
    [Header("Nastavení")]
    [Tooltip("Odkaz na kořenový PlayerController (v rodiči).")]
    [SerializeField] private PlayerController _controller;

    [Header("Náklon do stran (Banking)")]
    [SerializeField] private float _leanAmount = 2.5f; 
    [SerializeField] private float _leanSmoothing = 10.0f;

    [Header("Náklon dopředu (Running)")]
    [SerializeField] private float _runTiltAmount = 1.5f;

    [Header("Skluz (Volitelné)")]
    [SerializeField] private float _slideBackAngle = -45.0f;
    [SerializeField] private float _slideYOffset = -0.8f; 
    [SerializeField] private float _slideSmoothing = 8.0f;

    private Vector3 _currentRotation; // X=Pitch, Y=Yaw, Z=Roll
    private float _currentYOffset;
    private Vector3 _initialLocalPosition;

    private void Start()
    {
        // Najdeme controller v rodiči (Player)
        if (_controller == null)
            _controller = GetComponentInParent<PlayerController>();
            
        // Uložíme si, kde má ModelPivot být (pravděpodobně 0,0,0)
        _initialLocalPosition = transform.localPosition;
    }

    private void LateUpdate()
    {
        if (_controller == null) return;

        // 1. Získáme rychlost hráče a převedeme ji do lokálního směru
        // Díky tomu víme, co je "dopředu" a co "do boku", ať už koukáme kamkoliv
        Vector3 velocity = _controller.Velocity;
        Vector3 localVelocity = transform.parent.InverseTransformDirection(velocity);

        // 2. Vypočítáme cílové hodnoty
        float targetRoll = -localVelocity.x * _leanAmount; // Náklon do zatáčky
        float targetPitch = localVelocity.z * _runTiltAmount; // Náklon při běhu
        float targetYOffset = 0f;
        float currentSmoothing = _leanSmoothing;

        
        if (_controller.IsSliding) // Pokud máš tuto proměnnou v Controlleru
        {
            targetPitch = _slideBackAngle; // Záklon
            targetYOffset = _slideYOffset; // Snížení
            currentSmoothing = _slideSmoothing;
        }
        

        // 3. Vyhlazení (Lerp)
        _currentRotation.z = Mathf.Lerp(_currentRotation.z, targetRoll, Time.deltaTime * currentSmoothing);
        _currentRotation.x = Mathf.Lerp(_currentRotation.x, targetPitch, Time.deltaTime * currentSmoothing);
        
        // Y rotaci držíme striktně na 0 - TOTO JE KLÍČ K ODSTRANĚNÍ DRIFTU
        _currentRotation.y = 0f; 

        _currentYOffset = Mathf.Lerp(_currentYOffset, targetYOffset, Time.deltaTime * currentSmoothing);

        // 4. Aplikace
        transform.localRotation = Quaternion.Euler(_currentRotation.x, 0f, _currentRotation.z);
        transform.localPosition = _initialLocalPosition + new Vector3(0, _currentYOffset, 0);
    }
}