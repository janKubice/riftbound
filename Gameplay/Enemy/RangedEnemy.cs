using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class RangedEnemy : EnemyBaseAI
{
    [Header("Ranged Stats")]
    [SerializeField] private float _stopDistance = 10f; // Zastaví se dál od hráče
    [SerializeField] private float _attackCooldown = 3.0f;
    [SerializeField] private float _telegraphTime = 1.0f; // Jak dlouho svítí (nabíjí)

    [Header("Projectile")]
    [SerializeField] private GameObject _projectilePrefab; // Tvůj SmartProjectile prefab
    [SerializeField] private Transform _firePoint;
    [SerializeField] private WeaponStats _projectileStats; // Reuse tvého structu!
    
    [Header("Telegraph VFX")]
    [SerializeField] private GameObject _chargeUpVFX; // Světýlko při nabíjení

    private float _lastAttackTime;
    private bool _isAttacking = false;

    protected override void BehaviorLogic()
    {
        if (_isAttacking) 
        {
            RotateToTarget(); // Otáčí se za hráčem i při nabíjení
            return;
        }

        float dist = Vector3.Distance(transform.position, _targetPlayer.position);

        // Logika pohybu: Jdi k hráči, ale zastav se na dostřel
        if (dist > _stopDistance)
        {
            MoveToTarget();
        }
        else
        {
            StopMovement();
            RotateToTarget();

            if (Time.time >= _lastAttackTime + _attackCooldown)
            {
                StartCoroutine(ShootRoutine());
            }
        }
    }

    private IEnumerator ShootRoutine()
    {
        _isAttacking = true;
        _lastAttackTime = Time.time;

        // 1. Zapnout nabíjecí efekt (Telegraph)
        if (_chargeUpVFX) _chargeUpVFX.SetActive(true);
        if (_animator) _animator.SetTrigger("Cast"); // Nebo jiná animace

        yield return new WaitForSeconds(_telegraphTime);

        // 2. Vypnout efekt a vystřelit
        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);

        if (_projectilePrefab != null && _firePoint != null)
        {
            GameObject proj = Instantiate(_projectilePrefab, _firePoint.position, _firePoint.rotation);
            var netObj = proj.GetComponent<NetworkObject>();
            netObj.Spawn(true);

            if (proj.TryGetComponent(out SmartProjectile smartProj))
            {
                // DŮLEŽITÉ: Jako ownerId posíláme Server (0) nebo ID nepřítele, 
                // ale v SmartProjectile jsme upravili logiku na NetworkObjectId, takže to bude fungovat.
                _projectileStats.Damage = _currentDamage;
                smartProj.Initialize(NetworkObject, _firePoint.forward, _projectileStats);
            }
        }

        _isAttacking = false;
    }
}