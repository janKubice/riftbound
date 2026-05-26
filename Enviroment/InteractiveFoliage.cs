using UnityEngine;

public class InteractiveFoliage : MonoBehaviour
{
    [Header("Reakce")]
    [SerializeField] private ParticleSystem _leafParticles;
    [SerializeField] private float _shakeAmount = 5.0f;
    [SerializeField] private float _shakeRecovery = 10.0f;
    [SerializeField] private float _stopThreshold = 0.001f;

    private Quaternion _originalRot;
    private Vector3 _shakeOffset;

    private void Awake()
    {
        _originalRot = transform.localRotation;

        // Vypne Update, dokud někdo nezavolá OnHit.
        enabled = false;
    }

    private void Update()
    {
        _shakeOffset = Vector3.Lerp(
            _shakeOffset,
            Vector3.zero,
            Time.deltaTime * _shakeRecovery
        );

        transform.localRotation = _originalRot * Quaternion.Euler(_shakeOffset);

        if (_shakeOffset.sqrMagnitude <= _stopThreshold)
        {
            _shakeOffset = Vector3.zero;
            transform.localRotation = _originalRot;

            // Update už není potřeba.
            enabled = false;
        }
    }

    public void OnHit(Vector3 hitDirection)
    {
        if (_leafParticles != null)
        {
            _leafParticles.Play();
        }

        float intensity = Random.Range(0.8f, 1.2f);

        _shakeOffset = new Vector3(
            hitDirection.z,
            0f,
            -hitDirection.x
        ) * _shakeAmount * intensity;

        enabled = true;
    }
}