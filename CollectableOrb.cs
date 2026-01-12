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
    [SerializeField] private float _initialPopForce = 5.0f;
    
    [Header("Vizuál")]
    [SerializeField] private TrailRenderer _trail;

    private Transform _target;
    private bool _isMagnetized = false;
    private float _currentSpeed;
    private Rigidbody _rb;
    private Collider _col;

    // Callback, když orbu "doneseme" hráči
    public event Action<LootType, int> OnCollected;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _currentSpeed = _magnetSpeed;
    }

    public void Initialize(int amount, LootType type)
    {
        _amount = amount;
        _type = type;

        // Efekt "vyskočení" z nepřítele
        Vector3 randomDir = UnityEngine.Random.insideUnitSphere;
        randomDir.y = Mathf.Abs(randomDir.y); // Vždy nahoru
        if (_rb != null)
        {
            _rb.AddForce(randomDir * _initialPopForce, ForceMode.Impulse);
        }
    }

    public void StartMagnet(Transform targetPlayer)
    {
        if (_isMagnetized) return;

        _isMagnetized = true;
        _target = targetPlayer;

        // Vypneme fyziku, aby orba prošla zdí a letěla přímo k hráči
        if (_rb != null) _rb.isKinematic = true;
        if (_col != null) _col.enabled = false; 

        if (_trail != null) _trail.enabled = true;
    }

    private void Update()
    {
        if (!_isMagnetized || _target == null) return;

        // Zrychlování směrem k hráči
        _currentSpeed += _magnetAcceleration * Time.deltaTime;
        
        // Pohyb
        transform.position = Vector3.MoveTowards(transform.position, _target.position, _currentSpeed * Time.deltaTime);

        // Detekce "doteku" (když je dost blízko, sebereme ji)
        if (Vector3.Distance(transform.position, _target.position) < 0.5f)
        {
            Collect();
        }
    }

    private void Collect()
    {
        OnCollected?.Invoke(_type, _amount);
        Destroy(gameObject); // Nebo ReturnToPool() v budoucnu
    }
}