using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachineCamera))]
public class PlayerCameraEffects : MonoBehaviour
{
    [Header("Nastavení FOV")]
    [SerializeField] private float _normalFOV = 60f;
    [SerializeField] private float _sprintFOV = 65f;
    [Header("Head Bob")]
    [SerializeField] private float _bobFrequency = 10f; // Rychlost kmitání
    [SerializeField] private float _bobAmplitude = 0.5f; // Síla kmitání
    [SerializeField] private Transform _cameraHolder; // Objekt, se kterým budeme hýbat (ne samotná kamera, ale její rodič/držák)

    [Header("Slide")]
    [SerializeField] private float _slideFOV = 75f; // Ještě širší než sprint
    [SerializeField] private float _slideDutchTilt = 2.0f; // Jemný náklon kamery

    [SerializeField] private float _fovChangeSpeed = 5.0f;
    private float _timer = 0;
    private CinemachineCamera _vCam;
    private Animator _animator;
    private PlayerController _playerController; // Potřebujeme pro IsSliding

    private void Awake()
    {
        _vCam = GetComponent<CinemachineCamera>();
        _vCam.Lens.FieldOfView = _normalFOV;
        enabled = false;
    }

    public void Initialize(Animator playerAnimator)
    {
        _animator = playerAnimator;
        // Pokusíme se najít controller na stejném objektu jako animator (nebo v rodiči)
        _playerController = playerAnimator.GetComponentInParent<PlayerController>();
        enabled = (playerAnimator != null);
    }

    private void Update()
    {
        if (_animator == null) return;

        bool isSprinting = _animator.GetBool("IsSprinting");
        // Získáme stav skluzu přímo z controlleru (pokud ho máme), jinak false
        bool isSliding = (_playerController != null) && _playerController.IsSliding;

        // --- LOGIKA PRIORIT ---
        float targetFOV = _normalFOV;
        float targetDutch = 0f;

        if (isSliding)
        {
            targetFOV = _slideFOV;
            // Náklon kamery podle směru pohybu do strany (pokud existuje input), 
            // nebo fixní hodnota pro efekt "nestability"
            float strafe = _animator.GetFloat("RightSpeed"); // Použijeme existující parametr z animatoru
            targetDutch = -strafe * _slideDutchTilt;
        }
        else if (isSprinting)
        {
            targetFOV = _sprintFOV;
        }

        // --- APLIKACE ---

        // 1. FOV
        _vCam.Lens.FieldOfView = Mathf.Lerp(
            _vCam.Lens.FieldOfView,
            targetFOV,
            Time.deltaTime * _fovChangeSpeed
        );

        // 2. Dutch Tilt (Náklon kamery)
        _vCam.Lens.Dutch = Mathf.Lerp(
            _vCam.Lens.Dutch,
            targetDutch,
            Time.deltaTime * _fovChangeSpeed
        );

        HandleHeadBob();
    }

    private void HandleHeadBob()
    {
        if (_playerController == null) return;

        // Zjistíme, jestli se hýbeme (podle rychlosti controlleru)
        Vector3 velocity = _playerController.Velocity;
        bool isMoving = velocity.magnitude > 0.1f && _playerController.GetComponent<CharacterController>().isGrounded;

        if (isMoving)
        {
            _timer += Time.deltaTime * _bobFrequency;

            // Vypočítáme novou pozici (sinusovka pro Y, cosinusovka pro X = osmička)
            float xOffset = Mathf.Cos(_timer / 2) * _bobAmplitude * 0.5f; // X je poloviční
            float yOffset = Mathf.Sin(_timer) * _bobAmplitude;

            // Aplikujeme na localPosition držáku kamery
            Vector3 targetPos = new Vector3(xOffset, yOffset, 0);

            // Pokud máš _cameraHolder nastavený:
            if (_cameraHolder != null)
                _cameraHolder.localPosition = Vector3.Lerp(_cameraHolder.localPosition, targetPos, Time.deltaTime * 5f);
        }
        else
        {
            // Reset do nuly, když stojíme
            _timer = 0;
            if (_cameraHolder != null)
                _cameraHolder.localPosition = Vector3.Lerp(_cameraHolder.localPosition, Vector3.zero, Time.deltaTime * 5f);
        }
    }
}