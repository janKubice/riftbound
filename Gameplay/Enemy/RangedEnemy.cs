using UnityEngine;
using System.Collections;
using Unity.Netcode;

public class RangedEnemy : EnemyBaseAI
{
    [Header("Ranged Stats")]
    [SerializeField] private float _stopDistance = 10f; 
    [SerializeField] private float _attackCooldown = 3.0f;
    [SerializeField] private float _telegraphTime = 1.0f; 

    [Header("Projectile")]
    [SerializeField] private GameObject _projectilePrefab; 
    [SerializeField] private Transform _firePoint;
    [SerializeField] private WeaponStats _projectileStats; 

    [Header("Telegraph VFX")]
    [SerializeField] private GameObject _chargeUpVFX; 

    private float _lastAttackTime;
    private bool _isAttacking = false;
    private float _attackRangeSqr; 

    protected override void Awake()
    {
        base.Awake();
        _attackRangeSqr = _stopDistance * _stopDistance;
    }

    // BEZPEČNOSTNÍ POJISTKA: Pokud enemy umře nebo se vypne během nabíjení, zhasneme VFX
    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);
        _isAttacking = false;
        IsMovementPaused = false;
    }

    public override void BehaviorLogic()
    {
        // Pokud útočím, už nic neřeším - Coroutina si řídí rotaci sama
        if (_isAttacking) return;

        float distSqr = (MyTransform.position - TargetPlayer.position).sqrMagnitude;

        if (distSqr <= _attackRangeSqr)
        {
            // Jsme v dosahu
            if (Time.time >= _lastAttackTime + _attackCooldown)
            {
                StartCoroutine(ShootRoutine());
            }
            else
            {
                // Jsme v dosahu, čekáme na cooldown -> Stát a koukat na hráče
                IsMovementPaused = true;
                RotateToTarget();
            }
        }
        else
        {
            // Jsme daleko -> Jdi k hráči (Manager)
            IsMovementPaused = false;
        }
    }

    private IEnumerator ShootRoutine()
    {
        _isAttacking = true;
        IsMovementPaused = true; // Zastavíme pohyb
        _lastAttackTime = Time.time;

        // 1. Zapnout nabíjení
        if (_chargeUpVFX) _chargeUpVFX.SetActive(true);
        // if (_animator) _animator.SetTrigger("Cast");

        // 2. Čekání s otáčením (Telegraph phase)
        // Místo obyčejného WaitForSeconds budeme v cyklu čekat a otáčet se
        float timer = 0f;
        while (timer < _telegraphTime)
        {
            timer += Time.deltaTime;
            
            // DŮLEŽITÉ: Otáčíme se za hráčem i během nabíjení, aby nemohl jen tak uhnout
            RotateToTarget(); 
            
            yield return null; // Počkáme na další frame
        }

        // 3. Výstřel
        if (_chargeUpVFX) _chargeUpVFX.SetActive(false);

        // Kontrola, zda jsme stále naživu a máme cíl (mohli jsme umřít během while cyklu)
        if (IsSpawned && _projectilePrefab != null && _firePoint != null)
        {
            // TODO: V budoucnu nahradit za NetworkObjectPool.GetNetworkObject()
            GameObject proj = Instantiate(_projectilePrefab, _firePoint.position, _firePoint.rotation);
            var netObj = proj.GetComponent<NetworkObject>();
            netObj.Spawn(true);

            if (proj.TryGetComponent(out SmartProjectile smartProj))
            {
                _projectileStats.Damage = _currentDamage;
                // Posíláme 'this.NetworkObject', aby kill log věděl, kdo střílel
                smartProj.Initialize(this.NetworkObject, _firePoint.forward, _projectileStats);
            }
        }

        _isAttacking = false;
        IsMovementPaused = false; // Můžeme se zase hýbat
    }
}