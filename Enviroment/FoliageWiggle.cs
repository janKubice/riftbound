using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FoliageWiggle : MonoBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private ParticleSystem _leavesParticles; // Volitelné
    [SerializeField] private Transform _modelTransform;
    [SerializeField] private float _shakeAmount = 10f;
    [SerializeField] private float _shakeSpeed = 15f;
    [SerializeField] private float _recoverySpeed = 5f;

    private Quaternion _originalRot;
    private float _currentShake = 0f;

    private void Start()
    {
        if (_modelTransform == null) _modelTransform = transform;
        _originalRot = _modelTransform.localRotation;
    }

    private void Update()
    {
        if (_currentShake > 0.1f)
        {
            // Jednoduchý "wobble" efekt pomocí Sinusu
            float wobble = Mathf.Sin(Time.time * _shakeSpeed) * _currentShake;
            
            // Aplikace rotace
            _modelTransform.localRotation = _originalRot * Quaternion.Euler(wobble, 0, wobble);

            // Útlum
            _currentShake = Mathf.Lerp(_currentShake, 0, Time.deltaTime * _recoverySpeed);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        // Reaguje na hráče, nepřátele, projektily... cokoliv s Rigidbody nebo CharacterControllerem
        if (other.GetComponent<Rigidbody>() != null || other.GetComponent<CharacterController>() != null)
        {
            _currentShake = _shakeAmount;
            
            if (_leavesParticles != null)
            {
                _leavesParticles.Play();
            }
        }
    }
}