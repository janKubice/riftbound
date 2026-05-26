using UnityEngine;
using Unity.Cinemachine;

[RequireComponent(typeof(CinemachineCamera))]
public class PlayerCameraEffects : CinemachineExtension
{
    [Header("Initialization")]
    [SerializeField] private bool _autoInitializeFromParent = true;

    [Header("FOV")]
    [SerializeField] private float _normalFOV = 60f;
    [SerializeField] private float _sprintFOV = 65f;
    [SerializeField] private float _slideFOV = 75f;
    [SerializeField] private float _fovSmoothTime = 0.12f;

    [Header("Dutch Tilt")]
    [SerializeField] private float _slideDutchTilt = 2.0f;
    [SerializeField] private float _dutchSmoothTime = 0.10f;

    [Header("Head Bob")]
    [SerializeField] private bool _enableHeadBob = true;

    [Tooltip("Doporučeno 0.015 až 0.05. Původních 0.5 je extrémně moc.")]
    [SerializeField] private float _walkBobAmplitude = 0.035f;

    [SerializeField] private float _sprintBobAmplitude = 0.055f;
    [SerializeField] private float _walkBobFrequency = 7.5f;
    [SerializeField] private float _sprintBobFrequency = 10.5f;
    [SerializeField] private float _maxExpectedHorizontalSpeed = 8f;
    [SerializeField] private float _bobLerpSpeed = 12f;

    [Header("Idle Breathing")]
    [SerializeField] private bool _enableIdleBreathing = true;
    [SerializeField] private float _idleBreathAmplitude = 0.01f;
    [SerializeField] private float _idleBreathFrequency = 0.65f;

    [Header("Slide")]
    [SerializeField] private float _slideCameraDrop = -0.08f;
    [SerializeField] private float _slideBobSuppression = 0.35f;

    [Header("Landing Kick")]
    [SerializeField] private bool _enableLandingKick = true;
    [SerializeField] private float _minLandingVelocity = 5.0f;
    [SerializeField] private float _landingKickAmount = 0.055f;
    [SerializeField] private float _landingKickRecoverSpeed = 10f;

    [Header("Shake / Trauma")]
    [SerializeField] private float _traumaDecaySpeed = 2.8f;
    [SerializeField] private float _maxShakePosition = 0.055f;
    [SerializeField] private float _maxShakeRotation = 1.8f;
    [SerializeField] private float _shakeFrequency = 28f;

    [Header("Recoil")]
    [SerializeField] private float _recoilRecoverSpeed = 12f;
    [SerializeField] private float _recoilRotationRecoverSpeed = 10f;

    [Header("Debug")]
    [SerializeField] private bool _debugInitialization = false;

    private CinemachineCamera _vCam;
    private Animator _animator;
    private PlayerController _playerController;
    private CharacterController _characterController;

    private bool _initialized;

    private float _currentFOV;
    private float _fovVelocity;

    private float _currentDutch;
    private float _dutchVelocity;

    private float _bobTimer;
    private float _idleTimer;

    private Vector3 _bobOffset;
    private Vector3 _idleOffset;
    private Vector3 _slideOffset;
    private Vector3 _landingOffset;
    private Vector3 _shakeOffset;
    private Vector3 _recoilOffset;

    private Vector3 _rotationOffset;
    private Vector3 _targetRotationOffset;
    private Vector3 _recoilRotationOffset;

    private float _trauma;

    private bool _wasGrounded;
    private float _lastVerticalVelocity;

    private bool _hasIsSprintingParam;
    private bool _hasRightSpeedParam;

    private static readonly int IsSprintingHash = Animator.StringToHash("IsSprinting");
    private static readonly int RightSpeedHash = Animator.StringToHash("RightSpeed");

    protected override void Awake()
    {
        base.Awake();

        _vCam = GetComponent<CinemachineCamera>();

        _currentFOV = _normalFOV;
        SetLens(_normalFOV, 0f);
    }

    private void Start()
    {
        if (_autoInitializeFromParent && !_initialized)
        {
            Animator animator = GetComponentInParent<Animator>();

            if (animator != null)
                Initialize(animator);
            else if (_debugInitialization)
                Debug.LogWarning($"{nameof(PlayerCameraEffects)}: Animator nebyl nalezen v parent hierarchy.", this);
        }
    }

    public void Initialize(Animator playerAnimator)
    {
        _animator = playerAnimator;

        if (_animator == null)
        {
            _initialized = false;
            return;
        }

        _playerController = _animator.GetComponentInParent<PlayerController>();
        _characterController = _animator.GetComponentInParent<CharacterController>();

        _hasIsSprintingParam = HasAnimatorParameter(_animator, IsSprintingHash, AnimatorControllerParameterType.Bool);
        _hasRightSpeedParam = HasAnimatorParameter(_animator, RightSpeedHash, AnimatorControllerParameterType.Float);

        _wasGrounded = IsGrounded();
        _initialized = _playerController != null;

        if (_debugInitialization)
        {
            Debug.Log(
                $"{nameof(PlayerCameraEffects)} initialized. " +
                $"Animator: {_animator != null}, " +
                $"PlayerController: {_playerController != null}, " +
                $"CharacterController: {_characterController != null}, " +
                $"Has IsSprinting: {_hasIsSprintingParam}, " +
                $"Has RightSpeed: {_hasRightSpeedParam}",
                this
            );
        }
    }

    private void Update()
    {
        if (!_initialized || _animator == null || _playerController == null)
            return;

        float dt = Time.deltaTime;

        CameraStateData state = ReadCameraState();

        UpdateFOVAndDutch(state);
        UpdateHeadBob(state, dt);
        UpdateIdleBreathing(state, dt);
        UpdateSlideOffset(state, dt);
        UpdateLandingKick(state, dt);
        UpdateShake(dt);
        UpdateRecoil(dt);
    }

    private CameraStateData ReadCameraState()
    {
        Vector3 velocity = _playerController.Velocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);

        bool grounded = IsGrounded();
        bool moving = horizontalVelocity.magnitude > 0.1f && grounded;

        bool sprinting = _hasIsSprintingParam && _animator.GetBool(IsSprintingHash);
        bool sliding = _playerController.IsSliding;

        float rightSpeed = _hasRightSpeedParam ? _animator.GetFloat(RightSpeedHash) : 0f;

        float speed01 = Mathf.Clamp01(
            horizontalVelocity.magnitude / Mathf.Max(0.01f, _maxExpectedHorizontalSpeed)
        );

        return new CameraStateData
        {
            Velocity = velocity,
            HorizontalSpeed = horizontalVelocity.magnitude,
            Speed01 = speed01,
            IsGrounded = grounded,
            IsMoving = moving,
            IsSprinting = sprinting,
            IsSliding = sliding,
            RightSpeed = rightSpeed
        };
    }

    private void UpdateFOVAndDutch(CameraStateData state)
    {
        float targetFOV = _normalFOV;
        float targetDutch = 0f;

        if (state.IsSliding)
        {
            targetFOV = _slideFOV;
            targetDutch = -state.RightSpeed * _slideDutchTilt;

            if (Mathf.Abs(state.RightSpeed) < 0.05f)
                targetDutch = _slideDutchTilt * 0.35f;
        }
        else if (state.IsSprinting)
        {
            targetFOV = _sprintFOV;
        }

        _currentFOV = Mathf.SmoothDamp(
            _currentFOV,
            targetFOV,
            ref _fovVelocity,
            Mathf.Max(0.001f, _fovSmoothTime)
        );

        _currentDutch = Mathf.SmoothDamp(
            _currentDutch,
            targetDutch,
            ref _dutchVelocity,
            Mathf.Max(0.001f, _dutchSmoothTime)
        );

        SetLens(_currentFOV, _currentDutch);
    }

    private void UpdateHeadBob(CameraStateData state, float dt)
    {
        if (!_enableHeadBob)
        {
            _bobOffset = Vector3.zero;
            return;
        }

        Vector3 targetBob = Vector3.zero;

        if (state.IsMoving)
        {
            float frequency = state.IsSprinting ? _sprintBobFrequency : _walkBobFrequency;
            float amplitude = state.IsSprinting ? _sprintBobAmplitude : _walkBobAmplitude;

            if (state.IsSliding)
                amplitude *= _slideBobSuppression;

            amplitude *= Mathf.Lerp(0.35f, 1f, state.Speed01);

            _bobTimer += dt * frequency;

            float x = Mathf.Cos(_bobTimer * 0.5f) * amplitude * 0.45f;
            float y = Mathf.Sin(_bobTimer) * amplitude;

            targetBob = new Vector3(x, y, 0f);
        }
        else
        {
            _bobTimer = Mathf.Lerp(_bobTimer, 0f, dt * 8f);
        }

        _bobOffset = Vector3.Lerp(
            _bobOffset,
            targetBob,
            dt * _bobLerpSpeed
        );
    }

    private void UpdateIdleBreathing(CameraStateData state, float dt)
    {
        if (!_enableIdleBreathing)
        {
            _idleOffset = Vector3.zero;
            return;
        }

        Vector3 targetIdle = Vector3.zero;

        if (!state.IsMoving && !state.IsSliding && state.IsGrounded)
        {
            _idleTimer += dt * _idleBreathFrequency;

            float y = Mathf.Sin(_idleTimer * Mathf.PI * 2f) * _idleBreathAmplitude;
            targetIdle = new Vector3(0f, y, 0f);
        }

        _idleOffset = Vector3.Lerp(
            _idleOffset,
            targetIdle,
            dt * 5f
        );
    }

    private void UpdateSlideOffset(CameraStateData state, float dt)
    {
        Vector3 target = state.IsSliding
            ? new Vector3(0f, _slideCameraDrop, 0f)
            : Vector3.zero;

        _slideOffset = Vector3.Lerp(
            _slideOffset,
            target,
            dt * 10f
        );
    }

    private void UpdateLandingKick(CameraStateData state, float dt)
    {
        if (!_enableLandingKick)
            return;

        bool grounded = state.IsGrounded;

        if (!_wasGrounded && grounded)
        {
            float fallVelocity = Mathf.Abs(_lastVerticalVelocity);

            if (fallVelocity >= _minLandingVelocity)
            {
                float normalized = Mathf.InverseLerp(
                    _minLandingVelocity,
                    _minLandingVelocity * 2.2f,
                    fallVelocity
                );

                float kick = _landingKickAmount * normalized;

                _landingOffset += new Vector3(0f, -kick, 0f);
                AddShake(0.08f * normalized);
            }
        }

        _landingOffset = Vector3.Lerp(
            _landingOffset,
            Vector3.zero,
            dt * _landingKickRecoverSpeed
        );

        _wasGrounded = grounded;
        _lastVerticalVelocity = state.Velocity.y;
    }

    private void UpdateShake(float dt)
    {
        if (_trauma <= 0.0001f)
        {
            _trauma = 0f;
            _shakeOffset = Vector3.Lerp(_shakeOffset, Vector3.zero, dt * 12f);
            _targetRotationOffset = Vector3.Lerp(_targetRotationOffset, Vector3.zero, dt * 12f);
            return;
        }

        _trauma = Mathf.Max(0f, _trauma - dt * _traumaDecaySpeed);

        float shake = _trauma * _trauma;
        float time = Time.time * _shakeFrequency;

        float x = (Mathf.PerlinNoise(time, 0.13f) - 0.5f) * 2f;
        float y = (Mathf.PerlinNoise(0.37f, time) - 0.5f) * 2f;
        float z = (Mathf.PerlinNoise(time, time * 0.31f) - 0.5f) * 2f;

        _shakeOffset = new Vector3(
            x * _maxShakePosition * shake,
            y * _maxShakePosition * shake,
            0f
        );

        _targetRotationOffset = new Vector3(
            y * _maxShakeRotation * shake,
            x * _maxShakeRotation * shake,
            z * _maxShakeRotation * shake
        );
    }

    private void UpdateRecoil(float dt)
    {
        _recoilOffset = Vector3.Lerp(
            _recoilOffset,
            Vector3.zero,
            dt * _recoilRecoverSpeed
        );

        _recoilRotationOffset = Vector3.Lerp(
            _recoilRotationOffset,
            Vector3.zero,
            dt * _recoilRotationRecoverSpeed
        );

        _rotationOffset = Vector3.Lerp(
            _rotationOffset,
            _targetRotationOffset + _recoilRotationOffset,
            dt * 18f
        );
    }

    protected override void PostPipelineStageCallback(
        CinemachineVirtualCameraBase vcam,
        CinemachineCore.Stage stage,
        ref CameraState state,
        float deltaTime)
    {
        if (!_initialized)
            return;

        if (stage != CinemachineCore.Stage.Finalize)
            return;

        Vector3 localPositionOffset =
            _bobOffset +
            _idleOffset +
            _slideOffset +
            _landingOffset +
            _shakeOffset +
            _recoilOffset;

        Vector3 localRotationOffset = _rotationOffset;

        // PositionCorrection je world-space, proto lokální offset převedeme přes orientaci kamery.
        Vector3 worldOffset = state.RawOrientation * localPositionOffset;

        state.PositionCorrection += worldOffset;

        Quaternion rotationCorrection = Quaternion.Euler(localRotationOffset);

        state.OrientationCorrection = state.OrientationCorrection * rotationCorrection;
    }

    public void AddShake(float amount)
    {
        _trauma = Mathf.Clamp01(_trauma + Mathf.Max(0f, amount));
    }

    public void AddRecoil(float backwardKick, float pitchKick, float yawKick = 0f)
    {
        _recoilOffset += new Vector3(0f, 0f, -Mathf.Abs(backwardKick));
        _recoilRotationOffset += new Vector3(-Mathf.Abs(pitchKick), yawKick, 0f);
    }

    public void AddDirectionalRecoil(Vector3 localPositionKick, Vector3 localRotationKick)
    {
        _recoilOffset += localPositionKick;
        _recoilRotationOffset += localRotationKick;
    }

    public void ResetEffects()
    {
        _trauma = 0f;

        _bobOffset = Vector3.zero;
        _idleOffset = Vector3.zero;
        _slideOffset = Vector3.zero;
        _landingOffset = Vector3.zero;
        _shakeOffset = Vector3.zero;
        _recoilOffset = Vector3.zero;

        _rotationOffset = Vector3.zero;
        _targetRotationOffset = Vector3.zero;
        _recoilRotationOffset = Vector3.zero;

        SetLens(_normalFOV, 0f);
    }

    private void SetLens(float fov, float dutch)
    {
        if (_vCam == null)
            return;

        LensSettings lens = _vCam.Lens;
        lens.FieldOfView = fov;
        lens.Dutch = dutch;
        _vCam.Lens = lens;
    }

    private bool IsGrounded()
    {
        return _characterController != null && _characterController.isGrounded;
    }

    private static bool HasAnimatorParameter(
        Animator animator,
        int parameterHash,
        AnimatorControllerParameterType type)
    {
        if (animator == null)
            return false;

        AnimatorControllerParameter[] parameters = animator.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];

            if (parameter.nameHash == parameterHash && parameter.type == type)
                return true;
        }

        return false;
    }

    private struct CameraStateData
    {
        public Vector3 Velocity;
        public float HorizontalSpeed;
        public float Speed01;
        public bool IsGrounded;
        public bool IsMoving;
        public bool IsSprinting;
        public bool IsSliding;
        public float RightSpeed;
    }
}