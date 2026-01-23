using UnityEngine;
using System.Collections;

public class MeleeEnemy : EnemyBaseAI
{
    [Header("Melee Stats")]
    [SerializeField] private float _attackRange = 2.0f;
    [SerializeField] private int _damage = 10;
    [SerializeField] private float _attackCooldown = 1.5f;
    [SerializeField] private float _attackTelegraphTime = 0.4f; // Čas od začátku animace do úderu

    private float _lastAttackTime;
    private bool _isAttacking = false;

    protected override void BehaviorLogic()
    {
        if (_isAttacking) return;

        float dist = Vector3.Distance(transform.position, _targetPlayer.position);

        if (dist <= _attackRange)
        {
            StopMovement();
            RotateToTarget(); // Otáčet se i při stání

            if (Time.time >= _lastAttackTime + _attackCooldown)
            {
                StartCoroutine(AttackRoutine());
            }
        }
        else
        {
            MoveToTarget();
        }
    }

    private IEnumerator AttackRoutine()
    {
        _isAttacking = true;
        _lastAttackTime = Time.time;

        // 1. Animace
        if (_animator) _animator.SetTrigger("Attack");

        // 2. Telegraph (čekání na dopad)
        yield return new WaitForSeconds(_attackTelegraphTime);

        // 3. Kontrola zásahu (OverlapSphere před nepřítelem)
        // Použijeme jednoduchou detekci
        Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward, 1.5f);
        foreach(var hit in hits)
        {
            if(hit.TryGetComponent(out PlayerAttributes player))
            {
                player.TakeDamageServerRpc(_currentDamage);
            }
        }

        // 4. Dojezd animace
        yield return new WaitForSeconds(0.5f);
        _isAttacking = false;
    }
}