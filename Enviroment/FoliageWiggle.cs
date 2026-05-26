using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FoliageWiggle : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _modelTransform;
    [SerializeField] private ParticleSystem _leavesParticles;

    [Header("Reaction Filter")]
    [SerializeField] private LayerMask _reactToLayers = ~0;

    [Header("Shake Settings")]
    [SerializeField] private float _shakeAmount = 10f;
    [SerializeField] private float _shakeSpeed = 15f;
    [SerializeField] private float _recoverySpeed = 5f;
    [SerializeField] private float _stopThreshold = 0.1f;

    private Quaternion _originalRot;
    private float _currentShake;
    private float _time;

    private void Awake()
    {
        if (_modelTransform == null)
            _modelTransform = transform;

        _originalRot = _modelTransform.localRotation;

        // Důležité:
        // Vypne Update callback, dokud se objekt opravdu netřese.
        enabled = false;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;

        _time += deltaTime;

        float wobble = Mathf.Sin(_time * _shakeSpeed) * _currentShake;
        _modelTransform.localRotation = _originalRot * Quaternion.Euler(wobble, 0f, wobble);

        _currentShake = Mathf.Lerp(_currentShake, 0f, deltaTime * _recoverySpeed);

        if (_currentShake <= _stopThreshold)
        {
            _currentShake = 0f;
            _modelTransform.localRotation = _originalRot;

            // Znovu vypnout Update.
            enabled = false;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!ShouldReactTo(other))
            return;

        _currentShake = _shakeAmount;
        _time = 0f;

        if (_leavesParticles != null)
        {
            _leavesParticles.Play();
        }

        // Zapne Update jen po dobu třesení.
        enabled = true;
    }

    private bool ShouldReactTo(Collider other)
    {
        if (((1 << other.gameObject.layer) & _reactToLayers.value) == 0)
            return false;

        // Rychlejší než other.GetComponent<Rigidbody>().
        if (other.attachedRigidbody != null)
            return true;

        // CharacterController nemá attachedRigidbody, proto fallback.
        return other.TryGetComponent<CharacterController>(out _);
    }
}