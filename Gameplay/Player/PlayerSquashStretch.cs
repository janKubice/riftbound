using UnityEngine;

[DisallowMultipleComponent]
public class PlayerSquashStretch : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerController _controller;
    [SerializeField] private CharacterController _characterController;

    [Tooltip("Objekt s grafikou/model rootem. Nikdy ne root s CharacterControllerem.")]
    [SerializeField] private Transform _visualModel;

    [Header("Automatic Detection")]
    [SerializeField] private bool _autoDetectJumpAndLand = false;
    [SerializeField] private float _jumpVelocityThreshold = 3.0f;
    [SerializeField] private float _landVelocityThreshold = 5.0f;

    [Header("Squash / Stretch")]
    [Tooltip("Y scale při skoku. 1.15 = jemné natažení.")]
    [SerializeField] private float _jumpStretchY = 1.16f;

    [Tooltip("Y scale při dopadu. 0.82 = jemné zmáčknutí.")]
    [SerializeField] private float _landSquashY = 0.82f;

    [Tooltip("Maximální extra intenzita podle síly dopadu.")]
    [SerializeField] private float _maxLandingStrength = 1.5f;

    [Header("Smoothing")]
    [SerializeField] private float _scaleFollowSpeed = 18.0f;
    [SerializeField] private float _targetReturnSpeed = 8.0f;

    [Header("Options")]
    [SerializeField] private bool _preserveVolume = true;
    [SerializeField] private bool _warnIfUsingRoot = true;

    private Vector3 _originalScale;
    private Vector3 _currentTargetScale;

    private bool _wasGrounded;
    private float _lastVerticalVelocity;
    private bool _initialized;

    private void Awake()
    {
        if (_controller == null)
            _controller = GetComponentInParent<PlayerController>();

        if (_characterController == null)
            _characterController = GetComponentInParent<CharacterController>();

        if (_visualModel == null)
        {
            if (transform.childCount > 0)
            {
                _visualModel = transform.GetChild(0);
                Debug.LogWarning(
                    $"[PlayerSquashStretch] VisualModel nebyl přiřazen. Automaticky používám: {_visualModel.name}. " +
                    "Doporučuji to přiřadit ručně v Inspectoru.",
                    this
                );
            }
            else
            {
                _visualModel = transform;

                if (_warnIfUsingRoot)
                {
                    Debug.LogError(
                        "[PlayerSquashStretch] VisualModel není přiřazen a objekt nemá child. " +
                        "Script deformuje vlastní transform. Pokud je to player root s fyzikou, může to rozbít pohyb.",
                        this
                    );
                }
            }
        }

        _originalScale = _visualModel.localScale;
        _currentTargetScale = _originalScale;

        _wasGrounded = IsGrounded();
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized || _visualModel == null)
            return;

        float dt = Time.deltaTime;

        if (_autoDetectJumpAndLand && _controller != null)
        {
            DetectJumpAndLanding();
        }

        _visualModel.localScale = Vector3.Lerp(
            _visualModel.localScale,
            _currentTargetScale,
            dt * _scaleFollowSpeed
        );

        _currentTargetScale = Vector3.Lerp(
            _currentTargetScale,
            _originalScale,
            dt * _targetReturnSpeed
        );
    }

    private void DetectJumpAndLanding()
    {
        bool grounded = IsGrounded();
        float verticalVelocity = _controller.Velocity.y;

        if (_wasGrounded && !grounded && verticalVelocity > _jumpVelocityThreshold)
        {
            TriggerJumpSquash();
        }

        if (!_wasGrounded && grounded)
        {
            float fallSpeed = Mathf.Abs(_lastVerticalVelocity);

            if (fallSpeed >= _landVelocityThreshold)
            {
                float strength = Mathf.InverseLerp(
                    _landVelocityThreshold,
                    _landVelocityThreshold * 2.5f,
                    fallSpeed
                );

                TriggerLandSquash(Mathf.Lerp(1f, _maxLandingStrength, strength));
            }
        }

        _wasGrounded = grounded;
        _lastVerticalVelocity = verticalVelocity;
    }

    public void TriggerJumpSquash()
    {
        TriggerJumpSquash(1f);
    }

    public void TriggerJumpSquash(float strength)
    {
        strength = Mathf.Max(0f, strength);

        float y = Mathf.Lerp(1f, _jumpStretchY, strength);
        _currentTargetScale = BuildScaleFromY(y);
    }

    public void TriggerLandSquash()
    {
        TriggerLandSquash(1f);
    }

    public void TriggerLandSquash(float strength)
    {
        strength = Mathf.Max(0f, strength);

        float y = Mathf.Lerp(1f, _landSquashY, strength);
        _currentTargetScale = BuildScaleFromY(y);
    }

    public void TriggerCustomSquash(float targetYScaleMultiplier, float strength = 1f)
    {
        strength = Mathf.Max(0f, strength);

        float y = Mathf.Lerp(1f, targetYScaleMultiplier, strength);
        _currentTargetScale = BuildScaleFromY(y);
    }

    public void ResetScaleImmediate()
    {
        if (_visualModel == null)
            return;

        _currentTargetScale = _originalScale;
        _visualModel.localScale = _originalScale;
    }

    private Vector3 BuildScaleFromY(float yMultiplier)
    {
        yMultiplier = Mathf.Max(0.05f, yMultiplier);

        float xzMultiplier;

        if (_preserveVolume)
        {
            xzMultiplier = 1f / Mathf.Sqrt(yMultiplier);
        }
        else
        {
            xzMultiplier = 1f / yMultiplier;
        }

        return new Vector3(
            _originalScale.x * xzMultiplier,
            _originalScale.y * yMultiplier,
            _originalScale.z * xzMultiplier
        );
    }

    private bool IsGrounded()
    {
        return _characterController != null && _characterController.isGrounded;
    }
}