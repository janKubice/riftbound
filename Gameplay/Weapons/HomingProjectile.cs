using UnityEngine;
using Unity.Netcode;

public class HomingProjectile : SmartProjectile
{
    [Header("Homing Settings")]
    [SerializeField] private float _turnSpeed = 20f;
    [SerializeField] private float _searchRadius = 25f;
    [SerializeField] private float _homingDelay = 0.15f;
    [SerializeField] private LayerMask _targetLayer;

    private Collider _targetCollider; 
    private float _timeAlive;

    // Statický buffer sdílený všemi projektily (drastická úspora paměti pro Survivor-like)
    private static readonly Collider[] _hitBuffer = new Collider[50];

    private void Update()
    {
        if (!IsServer) return;

        _timeAlive += Time.deltaTime;
        
        if (_timeAlive < _homingDelay) return;

        // Pokud cíl umřel nebo zmizel, najdeme nový
        if (_targetCollider == null || !_targetCollider.gameObject.activeInHierarchy)
        {
            FindClosestTarget();
        }
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
        // NonAlloc varianta - nevytváří garbage
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, _searchRadius, _hitBuffer, _targetLayer);
        
        float closestDist = Mathf.Infinity;
        Collider bestTarget = null;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];

            // 1. Ignorovat cíle, které už tento projektil zasáhl (např. ten, od kterého se odrazil)
            if (_hitHistory.Contains(hit.gameObject)) continue;

            // 2. OPRAVA: Ignorovat samotného střelce podle unikátního NetworkObjectId
            var netObj = hit.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) continue;

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
        transform.rotation = Quaternion.LookRotation(newVelocityDir);
    }
}