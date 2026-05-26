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

    [Header("Impact Marker")]
    [SerializeField] private GameObject _impactMarkerPrefab;
    [SerializeField] private float _impactMarkerGroundOffset = 0.04f;

    private GameObject _impactMarkerInstance;

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

        if (_attackerObj != null && CombatTargeting.IsSelf(_attackerObj, other))
            return;

        // Friendly target ignorujeme, flask proletí dál.
        if (_attackerObj != null && CombatTargeting.IsFriendly(_attackerObj, other))
            return;

        Explode();
    }

    private void Explode()
    {
        _hasExploded = true;

        HideImpactMarkerClientRpc();

        if (_explosionVFXPrefab != null)
        {
            Instantiate(_explosionVFXPrefab, transform.position, Quaternion.identity);
        }

        int hits = Physics.OverlapSphereNonAlloc(transform.position, _explosionRadius, _hitColliders, _targetLayer);
        _damagedTargets.Clear();

        for (int i = 0; i < hits; i++)
        {
            Collider col = _hitColliders[i];

            if (!CombatTargeting.TryDamage(col, _attackerObj, _weaponStats.Damage, out GameObject damagedTarget))
                continue;

            if (damagedTarget.TryGetComponent(out StatusEffectReceiver receiver))
            {
                receiver.ApplyStatusEffect(_poisonEffectData);
            }

            if (damagedTarget.TryGetComponent(out EnemyHealth enemy) &&
                _poisonEffectData != null &&
                _poisonEffectData.Type != StatusEffectType.None)
            {
                enemy.ApplyStatusEffect(_poisonEffectData);
            }
        }

        GetComponent<NetworkObject>().Despawn(true);
    }
    
    #region Impact Marker
    public void ShowImpactMarker(Vector3 point, Vector3 normal)
    {
        if (!IsServer)
            return;

        ShowImpactMarkerClientRpc(point, normal);
    }

    [ClientRpc]
    private void ShowImpactMarkerClientRpc(Vector3 point, Vector3 normal)
    {
        if (_impactMarkerPrefab == null)
            return;

        if (_impactMarkerInstance == null)
        {
            _impactMarkerInstance = Instantiate(_impactMarkerPrefab);
        }

        _impactMarkerInstance.SetActive(true);

        Vector3 markerPosition = point + normal * _impactMarkerGroundOffset;
        Quaternion markerRotation = Quaternion.FromToRotation(Vector3.up, normal);

        _impactMarkerInstance.transform.SetPositionAndRotation(markerPosition, markerRotation);
    }

    [ClientRpc]
    private void HideImpactMarkerClientRpc()
    {
        DestroyImpactMarkerLocal();
    }

    private void DestroyImpactMarkerLocal()
    {
        if (_impactMarkerInstance != null)
        {
            Destroy(_impactMarkerInstance);
            _impactMarkerInstance = null;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        DestroyImpactMarkerLocal();
    }
    #endregion
}