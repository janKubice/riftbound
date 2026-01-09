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

    [Header("Skluz (Slide)")]
    [Tooltip("Úhel záklonu při skluzu (záporná = dozadu).")]
    [SerializeField] private float _slideBackAngle = -45.0f; // Větší úhel pro dramatičtější efekt
    [Tooltip("O kolik se model posune dolů při skluzu (aby 'seděl' na zemi).")]
    [SerializeField] private float _slideYOffset = -0.8f; 
    [Tooltip("Rychlost přechodu do/ze skluzu.")]
    [SerializeField] private float _slideSmoothing = 8.0f;

    private Vector3 _currentRotation;
    private float _currentYOffset;
    private Vector3 _initialLocalPosition;

    private void Start()
    {
        if (_controller == null)
            _controller = GetComponentInParent<PlayerController>();
            
        // Uložíme si výchozí pozici modelu (obvykle 0,0,0 vůči rodiči)
        _initialLocalPosition = transform.localPosition;
    }

    // DŮLEŽITÉ: LateUpdate se volá až PO Animátoru, takže se s ním nepereme
    private void LateUpdate()
    {
        if (_controller == null) return;

        // 1. Logika rotace (stejná jako předtím)
        Vector3 velocity = _controller.Velocity;
        Vector3 localVelocity = transform.parent.InverseTransformDirection(velocity);

        float targetRoll = -localVelocity.x * _leanAmount;
        float targetPitch = localVelocity.z * _runTiltAmount;
        float targetYOffset = 0f;
        float currentSmoothing = _leanSmoothing;

        // --- LOGIKA PRO SLIDE ---
        if (_controller.IsSliding)
        {
            targetPitch = _slideBackAngle;
            targetRoll *= 0.5f;
            targetYOffset = _slideYOffset; // Cílíme na sníženou pozici
            currentSmoothing = _slideSmoothing;
        }
        // ------------------------

        // 2. Vyhlazení Rotace
        _currentRotation.z = Mathf.Lerp(_currentRotation.z, targetRoll, Time.deltaTime * currentSmoothing);
        _currentRotation.x = Mathf.Lerp(_currentRotation.x, targetPitch, Time.deltaTime * currentSmoothing);

        // 3. Vyhlazení Pozice (Y Offset)
        _currentYOffset = Mathf.Lerp(_currentYOffset, targetYOffset, Time.deltaTime * currentSmoothing);

        // 4. APLIKACE (S opravou proti škubání)
        
        // ROTACE:
        // Získáme aktuální Y rotaci, kterou tam dal Animátor/Rodič, a NECHÁME JI TAM.
        // Měníme jen X (náklon) a Z (roll).
        float currentAnimatorY = transform.localEulerAngles.y;
        transform.localRotation = Quaternion.Euler(_currentRotation.x, currentAnimatorY, _currentRotation.z);

        // POZICE:
        // Aplikujeme snížení těžiště, aby zadek "tahal po zemi"
        transform.localPosition = _initialLocalPosition + new Vector3(0, _currentYOffset, 0);
    }
}