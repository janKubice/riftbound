using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachineCamera))]
public class PlayerCameraEffects : MonoBehaviour
{
    [Header("Nastavení FOV")]
    [SerializeField] private float _normalFOV = 60f;
    [SerializeField] private float _sprintFOV = 65f;
    [SerializeField] private float _fovChangeSpeed = 5.0f;

    private CinemachineCamera _vCam;
    private Animator _animator;

    private void Awake()
    {
        _vCam = GetComponent<CinemachineCamera>();
        _vCam.Lens.FieldOfView = _normalFOV;
        // Skript necháme vypnutý, dokud nedostane referenci na Animator
        enabled = false;
    }

    public void Initialize(Animator playerAnimator)
    {
        _animator = playerAnimator;
        enabled = (playerAnimator != null);
    }

    private void Update()
    {
        if (_animator == null) return;

        bool isSprinting = _animator.GetBool("IsSprinting");
        float targetFOV = isSprinting ? _sprintFOV : _normalFOV;

        _vCam.Lens.FieldOfView = Mathf.Lerp(
            _vCam.Lens.FieldOfView, 
            targetFOV, 
            Time.deltaTime * _fovChangeSpeed
        );
    }
}