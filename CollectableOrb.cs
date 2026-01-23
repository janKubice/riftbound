using UnityEngine;
using System;

public class CollectableOrb : MonoBehaviour
{
    [Header("Data")]
    private int _amount;
    private LootType _type;
    
    [Header("Pohyb")]
    [SerializeField] private float _magnetSpeed = 15.0f;
    [SerializeField] private float _magnetAcceleration = 20.0f;
    [SerializeField] private float _initialPopForce = 6.0f;
    
    [Header("Vizuál")]
    [SerializeField] private TrailRenderer _trail;

    private Transform _targetTransform;
    private PlayerCollector _targetCollector; // Reference na skript hráče
    private bool _isMagnetized = false;
    public bool IsMagnetized => _isMagnetized; // Aby PlayerCollector věděl, že už ho má

    private float _currentSpeed;
    private Rigidbody _rb;
    private Collider _col;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _currentSpeed = _magnetSpeed;
        if (_trail) _trail.enabled = false;
    }

    public void Initialize(int amount, LootType type)
    {
        _amount = amount;
        _type = type;

        // Efekt "vyskočení"
        Vector3 randomDir = UnityEngine.Random.insideUnitSphere;
        randomDir.y = Mathf.Abs(randomDir.y); 
        randomDir.x *= 0.5f; // Trochu plošší rozptyl
        randomDir.z *= 0.5f;

        if (_rb != null)
        {
            _rb.AddForce(randomDir * _initialPopForce, ForceMode.Impulse);
        }
    }

    public void StartMagnet(PlayerCollector collector)
    {
        if (_isMagnetized) return;

        _isMagnetized = true;
        _targetCollector = collector;
        _targetTransform = collector.transform;

        // Vypneme fyziku
        if (_rb != null) _rb.isKinematic = true;
        if (_col != null) _col.enabled = false; 

        if (_trail != null) _trail.enabled = true;
    }

    private void Update()
    {
        if (!_isMagnetized || _targetTransform == null) return;

        // Akcelerace
        _currentSpeed += _magnetAcceleration * Time.deltaTime;
        
        // Pohyb k hráči
        transform.position = Vector3.MoveTowards(transform.position, _targetTransform.position, _currentSpeed * Time.deltaTime);

        // Jsme v cíli?
        if (Vector3.Distance(transform.position, _targetTransform.position) < 0.5f)
        {
            Collect();
        }
    }

    private void Collect()
    {
        // Řekneme hráči: "Tady mě máš"
        if (_targetCollector != null)
        {
            _targetCollector.OnOrbCollectedLocal(_type, _amount);
        }
        Destroy(gameObject);
    }
}