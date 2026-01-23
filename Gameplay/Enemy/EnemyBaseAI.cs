using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
// ZMĚNA: Smazali jsme [RequireComponent(typeof(NetworkAnimator))]
public abstract class EnemyBaseAI : NetworkBehaviour
{
    [Header("Base Settings")]
    [SerializeField] protected float _aggroRange = 20f;
    [SerializeField] protected float _rotationSpeed = 30f;
    [SerializeField] protected float _spawnDuration = 1.0f; // Kratší spawn, když není animace

    protected int _currentDamage;
    protected float _currentSpeed;

    [Header("References")]
    // ZMĚNA: Není povinné, může zůstat prázdné
    [SerializeField] protected Animator _animator;

    protected NavMeshAgent _agent;
    protected EnemyHealth _health;
    protected Transform _targetPlayer;
    protected NetworkVariable<bool> _isSpawning = new NetworkVariable<bool>(true);

    private float _lastSearchTime;
    private const float SEARCH_INTERVAL = 0.15f;

    protected virtual void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<EnemyHealth>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _health.OnDeath += HandleDeath;
            _health.OnDamageTaken += HandleDamage;
            _health.IsInvulnerable = true;
            _isSpawning.Value = true;
            StartCoroutine(SpawnRoutine());
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            _health.OnDeath -= HandleDeath;
            _health.OnDamageTaken -= HandleDamage;
        }
    }

    protected virtual IEnumerator SpawnRoutine()
    {
        // ZMĚNA: Kontrola null před použitím
        if (_animator != null) _animator.SetTrigger("Spawn");

        // Pokud nemáme animaci, počkáme jen chvilku, aby se stihly inicializovat věci
        yield return new WaitForSeconds(_spawnDuration);

        _health.IsInvulnerable = false;
        _isSpawning.Value = false;
    }


    protected virtual void Update()
    {
        if (!IsServer || _health.CurrentHealth.Value <= 0 || _isSpawning.Value) return;

        if (Time.time > _lastSearchTime + SEARCH_INTERVAL)
        {
            FindClosestPlayer();
            _lastSearchTime = Time.time;
        }

        if (_targetPlayer != null)
        {
            BehaviorLogic();

            // ZMĚNA: Kontrola null
            if (_animator != null)
            {
                _animator.SetBool("Moving", _agent.velocity.magnitude > 0.1f);
            }
        }
    }

    public virtual void InitializeEnemy(EnemyTier tier, int hp, int damage, float speed, float scaleMultiplier)
    {
        // 1. Nastavíme staty
        _currentDamage = damage;
        _currentSpeed = speed;
        if (_agent) _agent.speed = speed;

        // 2. Nastavíme HP přes komponentu Health
        if (_health) _health.InitializeHealth(hp);

        // 3. Vizuální změna podle Tieru (Zvětšení modelu)
        transform.localScale = Vector3.one * scaleMultiplier;

        // 4. (Volitelné) Změna barvy nebo materiálu podle Tieru
        // Zde bys mohl měnit barvu očí nebo texturu, aby hráč poznal Elite moba.
    }

    protected abstract void BehaviorLogic();

    // --- POHYB ---

    protected void MoveToTarget()
    {
        if (_targetPlayer == null || !_agent.isOnNavMesh) return;
        _agent.SetDestination(_targetPlayer.position);
    }

    protected void StopMovement()
    {
        if (_agent.isOnNavMesh) _agent.ResetPath();
    }

    protected void RotateToTarget()
    {
        if (_targetPlayer == null) return;
        Vector3 dir = (_targetPlayer.position - transform.position).normalized;
        dir.y = 0;
        if (dir != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * _rotationSpeed);
        }
    }

    protected void FindClosestPlayer()
    {
        float closestDist = float.MaxValue;
        Transform bestTarget = null;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                float d = Vector3.Distance(transform.position, client.PlayerObject.transform.position);
                if (d < closestDist && d <= _aggroRange)
                {
                    closestDist = d;
                    bestTarget = client.PlayerObject.transform;
                }
            }
        }
        _targetPlayer = bestTarget;
    }

    // --- SMRT A POŠKOZENÍ ---

    protected virtual void HandleDamage(int damage)
    {
        StartCoroutine(KnockbackRoutine());
    }

    private IEnumerator KnockbackRoutine()
    {
        if (_agent.enabled) _agent.enabled = false;
        yield return new WaitForSeconds(0.2f);
        if (_health.CurrentHealth.Value > 0 && _agent != null)
        {
            _agent.enabled = true;
            if (_agent.isOnNavMesh) _agent.Warp(transform.position);
        }
    }

    protected virtual void HandleDeath()
    {
        if (_agent.enabled) _agent.enabled = false;

        // ZMĚNA: Kontrola null
        if (_animator != null) _animator.SetTrigger("Die");

        StartCoroutine(DespawnRoutine());
    }

    private IEnumerator DespawnRoutine()
    {
        // Bez animace stačí kratší čas na despawn (např. 0.5s)
        float delay = _animator != null ? 3.0f : 0.5f;
        yield return new WaitForSeconds(delay);

        // Loot logic...
        if (TryGetComponent(out DestructibleProp propLoot))
        {
            propLoot.TakeHit(); // Hack: Použijeme TakeHit pro drop lootu
        }

        _health.DestroySelf();
    }
}