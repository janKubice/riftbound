using UnityEngine;
using Unity.Netcode;

public class HomingProjectile : SmartProjectile
{
    [Header("Homing Settings")]
    [SerializeField] private float _turnSpeed = 20f;
    [SerializeField] private float _searchRadius = 25f;
    [SerializeField] private float _homingDelay = 0.15f;
    [SerializeField] private LayerMask _targetLayer;
    [SerializeField] private float _retargetInterval = 0.15f;
    [SerializeField] private float _retargetJitter = 0.05f;
    
    private float _nextSearchTime;
    private Collider _targetCollider;
    private float _timeAlive;

    // Statický buffer sdílený všemi projektily (drastická úspora paměti pro Survivor-like)
    private static readonly Collider[] _hitBuffer = new Collider[50];

    // Reset stavu při navrácení do fronty (Object Pool) nebo při zničení
    private void OnDisable()
    {
        _targetCollider = null;
        _timeAlive = 0f;
        _nextSearchTime = 0f;
    }

    private void Update()
    {
        if (!IsServer) return;

        _timeAlive += Time.deltaTime;

        if (_timeAlive < _homingDelay)
            return;

        bool targetInvalid =
            _targetCollider == null ||
            !_targetCollider.gameObject.activeInHierarchy ||
            !CanUseAsHomingTarget(_targetCollider);

        if (!targetInvalid)
            return;

        if (Time.time < _nextSearchTime)
            return;

        FindClosestTarget();

        _nextSearchTime = Time.time + _retargetInterval + Random.Range(0f, _retargetJitter);
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        if (_targetCollider != null && _rb != null)
        {
            RotateVelocityTowardsTarget();
        }
    }

    private void FindClosestTarget()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            _searchRadius,
            _hitBuffer,
            _targetLayer
        );

        float closestDist = Mathf.Infinity;
        Collider bestTarget = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];
            if (hit == null)
                continue;

            // UPOZORNĚNÍ: Filtrování ostatních projektilů nyní musí řešit _targetLayer!
            // Z důvodu výkonu zde byl odstraněn GetComponentInParent<SmartProjectile>().

            if (!CombatTargeting.CanDamage(_attackerObj, hit))
                continue;

            GameObject hitKey = ResolveHitHistoryKey(hit);
            if (hitKey != null && _hitHistory.Contains(hitKey))
                continue;

            NetworkObject netObj = hit.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.NetworkObjectId == _attackerObjectId)
                continue;

            if (!CanUseAsHomingTarget(hit))
                continue;

            float dist = Vector3.SqrMagnitude(transform.position - hit.transform.position);

            if (dist < closestDist)
            {
                closestDist = dist;
                bestTarget = hit;
            }
        }

        _targetCollider = bestTarget;

        System.Array.Clear(_hitBuffer, 0, hitCount);
    }

    private void RotateVelocityTowardsTarget()
    {
        Vector3 targetCenter = _targetCollider.bounds.center;
        Vector3 directionToTarget = (targetCenter - transform.position).normalized;
        Vector3 currentVelocity = _rb.linearVelocity;

        if (currentVelocity == Vector3.zero) return;

        Vector3 newVelocityDir = Vector3.RotateTowards(
            currentVelocity.normalized,
            directionToTarget,
            _turnSpeed * Time.fixedDeltaTime,
            0.0f
        );

        _rb.linearVelocity = newVelocityDir * currentVelocity.magnitude;
        
        // Změna na MoveRotation pro korektní interakci s fyzikálním enginem a network interpolací
        _rb.MoveRotation(Quaternion.LookRotation(newVelocityDir));
    }
}