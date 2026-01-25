using UnityEngine;
using System.Collections;

public class MeleeEnemy : EnemyBaseAI
{
    [Header("Melee Stats")]
    [SerializeField] private float _attackRange = 2.0f;
    [SerializeField] private int _damage = 10;
    [SerializeField] private float _attackCooldown = 1.5f;
    [SerializeField] private float _attackTelegraphTime = 0.4f;

    // OPTIMALIZACE 1: Statický buffer sdílený všemi nepřáteli
    // Už žádné 'new Collider[]' při každém útoku!
    private static readonly Collider[] _hitBuffer = new Collider[10];
    
    // OPTIMALIZACE 2: Hash animace místo stringu
    private static readonly int AnimID_Attack = Animator.StringToHash("Attack");

    private float _lastAttackTime;
    private bool _isAttacking = false;
    private float _attackRangeSqr; // Předpočítaná vzdálenost

    protected override void Awake()
    {
        base.Awake();
        // Předpočítáme si druhou mocninu, ať nemusíme odmocňovat v Update
        _attackRangeSqr = _attackRange * _attackRange;
    }

    protected override void BehaviorLogic()
    {
        if (_isAttacking) return;

        // OPTIMALIZACE 3: Použití sqrMagnitude místo Distance (ušetří procesor)
        float distSqr = (transform.position - _targetPlayer.position).sqrMagnitude;

        if (distSqr <= _attackRangeSqr)
        {
            StopMovement();
            RotateToTarget(); 

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

        // Použití Hash ID je rychlejší než string "Attack"
        if (_animator) _animator.SetTrigger(AnimID_Attack);

        yield return new WaitForSeconds(_attackTelegraphTime);

        // OPTIMALIZACE 4: NonAlloc verze fyziky
        // Výsledek 'hitCount' nám řekne, kolik věcí jsme trefili.
        // Data se zapíšou do existujícího pole '_hitBuffer', nevzniká žádný odpad v paměti.
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position + transform.forward, 
            1.5f, 
            _hitBuffer,
            LayerMask.GetMask("Player") // Pokud máš vrstvu Player, použij ji pro zrychlení! Pokud ne, smaž tento řádek.
        );

        for(int i = 0; i < hitCount; i++)
        {
            var hit = _hitBuffer[i];
            
            // Kontrola, zda jsme netrefili sami sebe nebo jiného nepřítele (pokud nemáš LayerMask)
            if (hit.gameObject == gameObject) continue;

            if(hit.TryGetComponent(out PlayerAttributes player))
            {
                player.TakeDamageServerRpc(_currentDamage);
            }
        }

        // Vyčistíme reference v poli, aby nezůstaly viset v paměti (dobrá praxe)
        for(int i = 0; i < hitCount; i++) _hitBuffer[i] = null;

        yield return new WaitForSeconds(0.5f);
        _isAttacking = false;
    }
}