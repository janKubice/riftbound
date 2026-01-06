using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;
using Unity.Netcode.Components;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(NetworkAnimator))] 
public class EnemyController : NetworkBehaviour
{
    [Header("Settings")]
    [SerializeField] private WeaponStats _stats; // Zde nastavíte Damage, Range, Speed...
    [SerializeField] private float _spawnDuration = 2.0f; // Jak dlouho trvá spawn animace
    [SerializeField] private float _rotationSpeed = 10f;

    [Header("References")]
    [SerializeField] private Animator _animator;

    private NavMeshAgent _agent;
    private EnemyHealth _health;
    private Transform _target;
    private float _lastAttackTime;
    
    private enum State { Spawning, Chasing, Attacking, Dead }
    private NetworkVariable<State> _currentState = new NetworkVariable<State>(State.Spawning);

    private void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<EnemyHealth>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _currentState.Value = State.Spawning;
            _health.IsInvulnerable = true;
            
            // Start Spawn Logic
            StartCoroutine(SpawnRoutine());

            // Listen for death
            _health.OnDeath += HandleDeath;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) _health.OnDeath -= HandleDeath;
    }

    private void Update()
    {
        if (!IsServer) return;

        switch (_currentState.Value)
        {
            case State.Chasing:
                LogicChasing();
                break;
            case State.Attacking:
                LogicAttacking();
                break;
        }
        
        // Update Animation params (Moving)
        if (_agent.enabled)
        {
            _animator.SetBool("Moving", _agent.velocity.magnitude > 0.1f);
        }
    }

    // --- State Logic ---

    private IEnumerator SpawnRoutine()
    {
        // Spustí animaci spawnu
        _animator.SetTrigger("Spawn");
        
        yield return new WaitForSeconds(_spawnDuration);

        // Konec spawnu
        _health.IsInvulnerable = false;
        _currentState.Value = State.Chasing;
    }

    private void LogicChasing()
    {
        FindClosestPlayer();

        if (_target == null) return;

        float distance = Vector3.Distance(transform.position, _target.position);

        // Pokud jsme v dosahu útoku -> Útok
        if (distance <= _stats.Range)
        {
            _currentState.Value = State.Attacking;
            _agent.ResetPath(); // Zastaví pohyb
        }
        else
        {
            // Jdeme k hráči
            _agent.SetDestination(_target.position);
        }
    }

    private void LogicAttacking()
    {
        if (_target == null)
        {
            _currentState.Value = State.Chasing;
            return;
        }

        // Rotace na hráče
        Vector3 direction = (_target.position - transform.position).normalized;
        direction.y = 0;
        if (direction != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * _rotationSpeed);
        }

        // Cooldown check
        if (Time.time >= _lastAttackTime + _stats.Cooldown)
        {
            StartCoroutine(PerformAttack());
        }
        
        // Pokud hráč utekl z dosahu (s menší tolerancí, aby enemy nekmital)
        float distance = Vector3.Distance(transform.position, _target.position);
        if (distance > _stats.Range * 1.2f) 
        {
            _currentState.Value = State.Chasing;
        }
    }

    // --- Actions ---

    protected virtual IEnumerator PerformAttack()
    {
        _lastAttackTime = Time.time;
        
        // 1. Spustíme animaci
        _animator.SetTrigger("Attack");

        // 2. Počkáme na "moment úderu" (jednoduchá prodleva, v budoucnu přes Animation Events)
        // Řekněme, že úder dopadne v polovině animace (např. 0.5s)
        yield return new WaitForSeconds(0.4f); 

        if (_currentState.Value == State.Dead) yield break;

        // 3. Detekce zásahu (Server Side OverlapSphere)
        // Zde používáme logiku podobnou MeleeAttackLogic, ale zjednodušenou
        Collider[] hits = Physics.OverlapSphere(transform.position + transform.forward * (_stats.Range / 2), _stats.Range);
        
        foreach (var hit in hits)
        {
            if (hit.TryGetComponent(out PlayerAttributes player))
            {
                // Check Angle (aby nezasáhl hráče za zády)
                Vector3 dirToPlayer = (player.transform.position - transform.position).normalized;
                if (Vector3.Angle(transform.forward, dirToPlayer) < _stats.AttackAngle / 2)
                {
                    player.TakeDamageServerRpc(_stats.Damage);
                }
            }
        }
        
        // Počkáme zbytek času (aby nemohl hned běžet)
        yield return new WaitForSeconds(0.5f);
    }

    private void FindClosestPlayer()
    {
        // Jednoduchá implementace - najde všechny hráče a vybere nejbližšího
        // Optimalizace: Nedělat každý frame, ale např. každých 0.5s
        
        float closestDist = float.MaxValue;
        Transform bestTarget = null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
                if (d < closestDist)
                {
                    closestDist = d;
                    bestTarget = client.PlayerObject.transform;
                }
            }
        }
        _target = bestTarget;
    }

    private void HandleDeath()
    {
        _currentState.Value = State.Dead;
        _agent.enabled = false;
        _animator.SetTrigger("Die");
        
        // Spustíme coroutinu pro despawn
        StartCoroutine(DespawnRoutine());
    }

    private IEnumerator DespawnRoutine()
    {
        yield return new WaitForSeconds(3.0f); // Čas na animaci smrti
        _health.DestroySelf(); // Zavolá NetDestroy
    }
}