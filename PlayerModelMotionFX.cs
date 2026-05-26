using UnityEngine;

[DisallowMultipleComponent]
public class PlayerModelMotionFX : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController _playerController;
    [SerializeField] private CharacterController _characterController;

    [Tooltip("Objekt, kterým se má hýbat. Pokud je prázdný, použije se tento transform.")]
    [SerializeField] private Transform _modelPivot;

    [Header("Velocity")]
    [Tooltip("Zapnuto = PlayerController.Velocity je world-space. Vypnuto = Velocity už je local-space.")]
    [SerializeField] private bool _velocityIsWorldSpace = true;

    [Tooltip("Pokud je zapnuto, lean funguje jen na zemi. Pro debug doporučuji nejdřív false.")]
    [SerializeField] private bool _requireGroundedForLean = false;

    [SerializeField] private float _maxExpectedSpeed = 8f;

    [Header("Lean")]
    [SerializeField] private float _sideLeanAmount = 8f;
    [SerializeField] private float _forwardLeanAmount = 4f;
    [SerializeField] private float _leanSmoothTime = 0.08f;

    [Header("Slide")]
    [SerializeField] private float _slidePitch = -18f;
    [SerializeField] private float _slideRollAmount = 5f;
    [SerializeField] private float _slideYOffset = -0.16f;
    [SerializeField] private float _slideSmoothTime = 0.06f;

    [Header("Landing")]
    [SerializeField] private bool _enableLandingDip = true;
    [SerializeField] private float _minLandingVelocity = 5f;
    [SerializeField] private float _landingDipAmount = -0.08f;
    [SerializeField] private float _landingRecoverSpeed = 10f;

    [Header("Squash / Stretch")]
    [SerializeField] private bool _enableSquashStretch = true;
    [SerializeField] private float _jumpStretchY = 1.14f;
    [SerializeField] private float _landSquashY = 0.84f;
    [SerializeField] private float _scaleFollowSpeed = 18f;
    [SerializeField] private float _scaleReturnSpeed = 8f;
    [SerializeField] private bool _preserveVolume = true;

    [Header("Limits")]
    [SerializeField] private float _maxPitch = 25f;
    [SerializeField] private float _maxRoll = 20f;
    [SerializeField] private float _maxYOffset = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs = false;

    [Tooltip("Zapněte pro test. ModelPivot se bude naklánět i bez pohybu hráče.")]
    [SerializeField] private bool _debugForceMovement = false;

    [SerializeField] private Vector3 _debugLocalVelocity = new Vector3(3f, 0f, 5f);

    private Vector3 _baseLocalPosition;
    private Quaternion _baseLocalRotation;
    private Vector3 _baseLocalScale;

    private Vector3 _currentEuler;
    private Vector3 _eulerVelocity;

    private float _currentYOffset;
    private float _yOffsetVelocity;
    private float _landingOffset;

    private Vector3 _targetScale;
    private bool _wasGrounded;
    private float _lastVerticalVelocity;

    private bool _initialized;

    private void Reset()
    {
        _modelPivot = transform;
        _playerController = GetComponentInParent<PlayerController>();
        _characterController = GetComponentInParent<CharacterController>();
    }

    private void Awake()
    {
        if (_modelPivot == null)
            _modelPivot = transform;

        if (_playerController == null)
            _playerController = GetComponentInParent<PlayerController>();

        if (_characterController == null)
            _characterController = GetComponentInParent<CharacterController>();

        _baseLocalPosition = _modelPivot.localPosition;
        _baseLocalRotation = _modelPivot.localRotation;
        _baseLocalScale = _modelPivot.localScale;

        _targetScale = _baseLocalScale;
        _wasGrounded = IsGrounded();

        _initialized = true;

        if (_debugLogs)
        {
            Debug.Log(
                $"[{nameof(PlayerModelMotionFX)}] Initialized. " +
                $"Controller: {_playerController != null}, " +
                $"CharacterController: {_characterController != null}, " +
                $"ModelPivot: {_modelPivot.name}",
                this
            );
        }
    }

    private void LateUpdate()
    {
        if (!_initialized || _modelPivot == null)
            return;

        float dt = Time.deltaTime;

        Vector3 localVelocity = GetLocalVelocity();
        Vector3 horizontalLocalVelocity = new Vector3(localVelocity.x, 0f, localVelocity.z);

        bool grounded = IsGrounded();
        bool sliding = _playerController != null && _playerController.IsSliding;

        bool canLean = !_requireGroundedForLean || grounded;
        float horizontalSpeed = horizontalLocalVelocity.magnitude;

        float speed01 = Mathf.Clamp01(horizontalSpeed / Mathf.Max(0.01f, _maxExpectedSpeed));
        float side01 = Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, _maxExpectedSpeed), -1f, 1f);
        float forward01 = Mathf.Clamp(localVelocity.z / Mathf.Max(0.01f, _maxExpectedSpeed), -1f, 1f);

        Vector3 targetEuler = Vector3.zero;
        float targetYOffset = 0f;
        float smoothTime = _leanSmoothTime;

        if (canLean)
        {
            if (sliding)
            {
                targetEuler.x = _slidePitch;
                targetEuler.z = -side01 * _slideRollAmount;
                targetYOffset = _slideYOffset;
                smoothTime = _slideSmoothTime;
            }
            else
            {
                targetEuler.x = forward01 * _forwardLeanAmount * speed01;
                targetEuler.z = -side01 * _sideLeanAmount * speed01;
            }
        }

        targetEuler.x = Mathf.Clamp(targetEuler.x, -_maxPitch, _maxPitch);
        targetEuler.z = Mathf.Clamp(targetEuler.z, -_maxRoll, _maxRoll);
        targetYOffset = Mathf.Clamp(targetYOffset, -_maxYOffset, _maxYOffset);

        _currentEuler = Vector3.SmoothDamp(
            _currentEuler,
            targetEuler,
            ref _eulerVelocity,
            Mathf.Max(0.001f, smoothTime)
        );

        _currentYOffset = Mathf.SmoothDamp(
            _currentYOffset,
            targetYOffset,
            ref _yOffsetVelocity,
            Mathf.Max(0.001f, smoothTime)
        );

        HandleLandingAndSquash(grounded, localVelocity.y, dt);
        ApplyTransform(dt);
    }

    private Vector3 GetLocalVelocity()
    {
        if (_debugForceMovement)
            return _debugLocalVelocity;

        if (_playerController == null)
            return Vector3.zero;

        Vector3 velocity = _playerController.Velocity;

        if (!_velocityIsWorldSpace)
            return velocity;

        Transform reference = _playerController.transform;
        return reference.InverseTransformDirection(velocity);
    }

    private void HandleLandingAndSquash(bool grounded, float verticalVelocity, float dt)
    {
        if (_enableLandingDip)
        {
            if (!_wasGrounded && grounded)
            {
                float fallSpeed = Mathf.Abs(_lastVerticalVelocity);

                if (fallSpeed >= _minLandingVelocity)
                {
                    float strength = Mathf.InverseLerp(
                        _minLandingVelocity,
                        _minLandingVelocity * 2.4f,
                        fallSpeed
                    );

                    _landingOffset += _landingDipAmount * strength;

                    if (_enableSquashStretch)
                        TriggerLandSquash(Mathf.Lerp(1f, 1.4f, strength));
                }
            }

            _landingOffset = Mathf.Lerp(
                _landingOffset,
                0f,
                dt * _landingRecoverSpeed
            );
        }

        if (_enableSquashStretch)
        {
            _modelPivot.localScale = Vector3.Lerp(
                _modelPivot.localScale,
                _targetScale,
                dt * _scaleFollowSpeed
            );

            _targetScale = Vector3.Lerp(
                _targetScale,
                _baseLocalScale,
                dt * _scaleReturnSpeed
            );
        }

        _wasGrounded = grounded;
        _lastVerticalVelocity = verticalVelocity;
    }

    private void ApplyTransform(float dt)
    {
        Vector3 finalPosition =
            _baseLocalPosition +
            new Vector3(0f, _currentYOffset + _landingOffset, 0f);

        Quaternion finalRotation =
            _baseLocalRotation *
            Quaternion.Euler(_currentEuler);

        _modelPivot.localPosition = finalPosition;
        _modelPivot.localRotation = finalRotation;

        if (!_enableSquashStretch)
            _modelPivot.localScale = _baseLocalScale;
    }

    public void TriggerJumpSquash()
    {
        TriggerJumpSquash(1f);
    }

    public void TriggerJumpSquash(float strength)
    {
        if (!_enableSquashStretch)
            return;

        float y = Mathf.Lerp(1f, _jumpStretchY, Mathf.Max(0f, strength));
        _targetScale = BuildScaleFromY(y);
    }

    public void TriggerLandSquash()
    {
        TriggerLandSquash(1f);
    }

    public void TriggerLandSquash(float strength)
    {
        if (!_enableSquashStretch)
            return;

        float y = Mathf.Lerp(1f, _landSquashY, Mathf.Max(0f, strength));
        _targetScale = BuildScaleFromY(y);
    }

    public void AddImpulseLean(Vector3 worldDirection, float force)
    {
        if (_playerController == null)
            return;

        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude < 0.001f)
            return;

        Vector3 localDirection = _playerController.transform.InverseTransformDirection(worldDirection.normalized);

        _eulerVelocity += new Vector3(
            localDirection.z * force,
            0f,
            -localDirection.x * force
        );
    }

    public void ResetMotionImmediate()
    {
        _currentEuler = Vector3.zero;
        _eulerVelocity = Vector3.zero;
        _currentYOffset = 0f;
        _yOffsetVelocity = 0f;
        _landingOffset = 0f;

        _targetScale = _baseLocalScale;

        _modelPivot.localPosition = _baseLocalPosition;
        _modelPivot.localRotation = _baseLocalRotation;
        _modelPivot.localScale = _baseLocalScale;
    }

    private Vector3 BuildScaleFromY(float yMultiplier)
    {
        yMultiplier = Mathf.Max(0.05f, yMultiplier);

        float xzMultiplier = _preserveVolume
            ? 1f / Mathf.Sqrt(yMultiplier)
            : 1f / yMultiplier;

        return new Vector3(
            _baseLocalScale.x * xzMultiplier,
            _baseLocalScale.y * yMultiplier,
            _baseLocalScale.z * xzMultiplier
        );
    }

    private bool IsGrounded()
    {
        if (_characterController == null)
            return true;

        return _characterController.isGrounded;
    }
}