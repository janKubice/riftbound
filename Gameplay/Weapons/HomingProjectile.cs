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

    // Unity volá Update automaticky
    private void Update()
    {
        if (!IsServer) return;

        _timeAlive += Time.deltaTime;
        
        // Fáze 1: Čekání
        if (_timeAlive < _homingDelay) return;

        // Fáze 2: Hledání cíle (pokud ho nemáme nebo byl zničen)
        if (_targetCollider == null)
        {
            FindClosestTarget();
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Pokud máme cíl, zatáčíme
        if (_targetCollider != null && _rb != null)
        {
            RotateVelocityTowardsTarget();
        }
    }

    private void FindClosestTarget()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, _searchRadius, _targetLayer);
        float closestDist = Mathf.Infinity;
        Collider bestTarget = null;

        foreach (var hit in hits)
        {
            // Ignorujeme sami sebe (Owner)
            var netObj = hit.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) continue;

            float dist = Vector3.Distance(transform.position, hit.transform.position);
            if (dist < closestDist)
            {
                closestDist = dist;
                bestTarget = hit;
            }
        }

        _targetCollider = bestTarget;
    }

    private void RotateVelocityTowardsTarget()
    {
        // --- OPRAVA ZDE ---
        // Místo .transform.position (nohy) bereme .bounds.center (střed těla/hrudník)
        Vector3 targetCenter = _targetCollider.bounds.center;
        
        Vector3 directionToTarget = (targetCenter - transform.position).normalized;
        // ------------------

        Vector3 currentVelocity = _rb.linearVelocity; // Unity 6 (nebo .velocity)
        
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