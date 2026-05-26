using UnityEngine;

[DisallowMultipleComponent]
public class PlayerProceduralLean : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController _controller;
    [SerializeField] private CharacterController _characterController;

    [Tooltip("Objekt, který se bude naklánět. Pokud je prázdný, použije se transform tohoto objektu.")]
    [SerializeField] private Transform _visualRoot;

    [Header("Movement Lean")]
    [Tooltip("Náklon do stran při pohybu do boku.")]
    [SerializeField] private float _sideLeanAmount = 7.0f;

    [Tooltip("Náklon dopředu při běhu.")]
    [SerializeField] private float _forwardLeanAmount = 3.0f;

    [Tooltip("Maximální očekávaná horizontální rychlost hráče. Podle toho se normalizuje lean.")]
    [SerializeField] private float _maxExpectedSpeed = 8.0f;

    [Tooltip("Jak rychle lean následuje cílovou hodnotu.")]
    [SerializeField] private float _leanSmoothSpeed = 12.0f;

    [Header("Slide")]
    [SerializeField] private float _slideBackAngle = -18.0f;
    [SerializeField] private float _slideRollAmount = 4.0f;
    [SerializeField] private float _slideYOffset = -0.18f;
    [SerializeField] private float _slideSmoothSpeed = 14.0f;

    [Header("Air / Landing")]
    [SerializeField] private bool _useAirTilt = true;
    [SerializeField] private float _airForwardTilt = -4.0f;
    [SerializeField] private float _landingDipAmount = -0.08f;
    [SerializeField] private float _landingRecoverSpeed = 10.0f;
    [SerializeField] private float _minLandingVelocity = 5.0f;

    [Header("Limits")]
    [SerializeField] private float _maxPitch = 25.0f;
    [SerializeField] private float _maxRoll = 18.0f;
    [SerializeField] private float _maxYOffset = 0.35f;

    private Quaternion _initialLocalRotation;
    private Vector3 _initialLocalPosition;

    private Vector3 _currentEulerOffset;
    private Vector3 _eulerVelocity;

    private float _currentYOffset;
    private float _yVelocity;
    private float _landingOffset;

    private bool _wasGrounded;
    private float _lastVerticalVelocity;

    private void Awake()
    {
        if (_visualRoot == null)
            _visualRoot = transform;

        if (_controller == null)
            _controller = GetComponentInParent<PlayerController>();

        if (_characterController == null)
            _characterController = GetComponentInParent<CharacterController>();

        _initialLocalRotation = _visualRoot.localRotation;
        _initialLocalPosition = _visualRoot.localPosition;

        _wasGrounded = IsGrounded();
    }

    private void LateUpdate()
    {
        if (_controller == null || _visualRoot == null)
            return;

        float dt = Time.deltaTime;

        Vector3 velocity = _controller.Velocity;
        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);

        Transform reference = _controller.transform;
        Vector3 localVelocity = reference.InverseTransformDirection(horizontalVelocity);

        float speed01 = Mathf.Clamp01(horizontalVelocity.magnitude / Mathf.Max(0.01f, _maxExpectedSpeed));

        bool grounded = IsGrounded();
        bool sliding = _controller.IsSliding;

        Vector3 targetEuler = Vector3.zero;
        float targetYOffset = 0f;
        float smoothSpeed = _leanSmoothSpeed;

        if (sliding)
        {
            float side = Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, _maxExpectedSpeed), -1f, 1f);

            targetEuler.x = _slideBackAngle;
            targetEuler.z = -side * _slideRollAmount;

            targetYOffset = _slideYOffset;
            smoothSpeed = _slideSmoothSpeed;
        }
        else
        {
            float side01 = Mathf.Clamp(localVelocity.x / Mathf.Max(0.01f, _maxExpectedSpeed), -1f, 1f);
            float forward01 = Mathf.Clamp(localVelocity.z / Mathf.Max(0.01f, _maxExpectedSpeed), -1f, 1f);

            targetEuler.z = -side01 * _sideLeanAmount * speed01;
            targetEuler.x = forward01 * _forwardLeanAmount * speed01;

            if (_useAirTilt && !grounded)
            {
                targetEuler.x += _airForwardTilt;
            }
        }

        targetEuler.x = Mathf.Clamp(targetEuler.x, -_maxPitch, _maxPitch);
        targetEuler.z = Mathf.Clamp(targetEuler.z, -_maxRoll, _maxRoll);
        targetYOffset = Mathf.Clamp(targetYOffset, -_maxYOffset, _maxYOffset);

        HandleLanding(grounded, velocity.y, dt);

        _currentEulerOffset = Vector3.SmoothDamp(
            _currentEulerOffset,
            targetEuler,
            ref _eulerVelocity,
            1f / Mathf.Max(0.01f, smoothSpeed)
        );

        _currentYOffset = Mathf.SmoothDamp(
            _currentYOffset,
            targetYOffset,
            ref _yVelocity,
            1f / Mathf.Max(0.01f, smoothSpeed)
        );

        _landingOffset = Mathf.Lerp(
            _landingOffset,
            0f,
            dt * _landingRecoverSpeed
        );

        Vector3 finalPosition =
            _initialLocalPosition +
            new Vector3(0f, _currentYOffset + _landingOffset, 0f);

        Quaternion finalRotation =
            _initialLocalRotation *
            Quaternion.Euler(_currentEulerOffset);

        _visualRoot.localPosition = finalPosition;
        _visualRoot.localRotation = finalRotation;
    }

    private void HandleLanding(bool grounded, float verticalVelocity, float dt)
    {
        if (!_wasGrounded && grounded)
        {
            float fallSpeed = Mathf.Abs(_lastVerticalVelocity);

            if (fallSpeed >= _minLandingVelocity)
            {
                float t = Mathf.InverseLerp(
                    _minLandingVelocity,
                    _minLandingVelocity * 2.25f,
                    fallSpeed
                );

                _landingOffset += _landingDipAmount * t;
            }
        }

        _wasGrounded = grounded;
        _lastVerticalVelocity = verticalVelocity;
    }

    public void AddImpulseLean(Vector3 worldDirection, float force)
    {
        if (_controller == null)
            return;

        worldDirection.y = 0f;

        if (worldDirection.sqrMagnitude < 0.0001f)
            return;

        Vector3 localDirection = _controller.transform.InverseTransformDirection(worldDirection.normalized);

        _eulerVelocity += new Vector3(
            localDirection.z * force,
            0f,
            -localDirection.x * force
        );
    }

    private bool IsGrounded()
    {
        return _characterController != null && _characterController.isGrounded;
    }
}