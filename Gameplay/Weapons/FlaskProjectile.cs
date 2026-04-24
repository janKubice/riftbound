using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class FlaskProjectile : SmartProjectile
{
    [Header("Lobber Settings")]
    [SerializeField] private float _explosionRadius = 3.5f;
    [SerializeField] private StatusEffectData _poisonEffectData;
    [SerializeField] private LayerMask _targetLayer;
    [SerializeField] private GameObject _explosionVFXPrefab;

    [Header("Visuals")]
    [SerializeField] private Transform _visualTransform;
    [SerializeField] private float _rotationSpeed = 360f;

    private static readonly Collider[] _hitColliders = new Collider[20];
    private static readonly HashSet<PlayerAttributes> _damagedTargets = new HashSet<PlayerAttributes>(); 
    private bool _hasExploded = false;
    private WeaponStats _weaponStats;
    private NetworkObject _attackerObj;

    public override void Initialize(NetworkObject attacker, Vector3 direction, WeaponStats stats, List<HitEffect> payload = null, HashSet<GameObject> passedHitHistory = null)
    {
        base.Initialize(attacker, direction, stats, payload, passedHitHistory);
        _weaponStats = stats;
        _attackerObj = attacker;
        
        // Zapnutí gravitace pro případ, že neletí obloukem (fallback)
        if (_rb != null) _rb.useGravity = true;
    }

    /// <summary>
    /// Nová metoda, kterou zavolá Enemy a předá přesně tu rychlost, kterou použil pro Telegraph.
    /// </summary>
    public void ApplyCalculatedVelocity(Vector3 calculatedVelocity)
    {
        if (_rb != null)
        {
            _rb.useGravity = true;
            _rb.linearVelocity = calculatedVelocity;
        }
    }

    private void Update()
    {
        if (_visualTransform != null && !_hasExploded)
        {
            _visualTransform.Rotate(Vector3.right * (_rotationSpeed * Time.deltaTime));
        }
    }

    protected override void OnTriggerEnter(Collider other)
    {
        if (!IsServer || _hasExploded) return;
        if (other.isTrigger) return;
        if (_attackerObj != null && other.transform.root == _attackerObj.transform.root) return;
        if (other.GetComponentInParent<EnemyHealth>() != null) return;

        Explode();
    }

    private void Explode()
    {
        _hasExploded = true;

        if (_explosionVFXPrefab != null)
        {
            Instantiate(_explosionVFXPrefab, transform.position, Quaternion.identity);
        }

        int hits = Physics.OverlapSphereNonAlloc(transform.position, _explosionRadius, _hitColliders, _targetLayer);
        _damagedTargets.Clear();

        for (int i = 0; i < hits; i++)
        {
            Collider col = _hitColliders[i];
            if (col.GetComponentInParent<EnemyHealth>() != null) continue;

            PlayerAttributes playerHealth = col.GetComponentInParent<PlayerAttributes>();
            if (playerHealth != null && _damagedTargets.Add(playerHealth))
            {
                if (playerHealth.TryGetComponent(out StatusEffectReceiver receiver))
                {
                    receiver.ApplyStatusEffect(_poisonEffectData);
                }
                playerHealth.TakeDamageServerRpc(_weaponStats.Damage, _attackerObj.OwnerClientId);
            }
        }

        GetComponent<NetworkObject>().Despawn(true);
    }
}