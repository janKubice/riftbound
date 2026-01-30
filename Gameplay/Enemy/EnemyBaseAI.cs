using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(Rigidbody))]
public abstract class EnemyBaseAI : NetworkBehaviour
{
    [Header("Base Settings")]
    [SerializeField] protected float _aggroRange = 10000f;
    [SerializeField] protected float _rotationSpeed = 720f;
    [SerializeField] protected float _spawnDuration = 0.1f; // Kratší spawn, když není animace
    [SerializeField] protected EnemyTier _tier = EnemyTier.Normal;
    protected int _baseDamage;
    protected int _currentDamage;
    protected float _currentSpeed;
    protected float _currentAttackRate = 1.0f;     // Útoky za sekundu
    protected float _knockbackResistance = 0f;     // 0 = plný odlet, 1 = ani se nehne
    protected int _xpReward = 0;

    [Header("References")]
    // ZMĚNA: Není povinné, může zůstat prázdné
    [SerializeField] protected Animator _animator;

    protected NavMeshAgent _agent;
    protected EnemyHealth _health;
    protected Transform _targetPlayer;
    protected NetworkVariable<bool> _isSpawning = new NetworkVariable<bool>(true);

    private float _lastSearchTime;
    private const float SEARCH_INTERVAL = 0.15f;
    private float _pathUpdateTimer;
    private float _currentPathUpdateInterval = 0.2f;
    private Vector3 _lastPos;

    [Header("Visuals")]
    // Pokud máš model jako dítě objektu, přiřaď ho sem v Inspectoru nebo ho najdeme v Awake
    [SerializeField] protected Renderer _modelRenderer; 
    
    // Optimalizace: Umožňuje měnit barvu bez duplikace materiálu (Draw Call Batching)
    private MaterialPropertyBlock _propBlock;


    protected virtual void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<EnemyHealth>();
        _pathUpdateTimer = Time.time + Random.Range(0f, 0.1f);
        if (_agent != null)
        {
            _agent.acceleration = 60f; // Rychlý rozjezd (default je 8)
            _agent.angularSpeed = 720f; // Rychlé otáčení agenta (pokud ho řídí NavMesh)
            _agent.autoBraking = false; // Nezastavovat před cílem, pokud to neřídíme manuálně
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; // Pro hordy entit šetří CPU
        }

        _propBlock = new MaterialPropertyBlock();

        if (_modelRenderer == null)
        {
            _modelRenderer = GetComponentInChildren<Renderer>();
        }
    }

    public override void OnNetworkSpawn()
    {
        _lastPos = transform.position;
        if (IsServer)
        {
            ResetEnemyState();

            _health.OnDeath -= HandleDeath;
            _health.OnDamageTaken -= HandleDamage;

            _health.OnDeath += HandleDeath;
            _health.OnDamageTaken += HandleDamage;

            _health.IsInvulnerable = true;
            _isSpawning.Value = true;
            StartCoroutine(SpawnRoutine());

            if (_health != null)
            {
                _health.SetEnemyTier(_tier);
            }
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

    private void ResetEnemyState()
    {
        // 1. NEJDŘÍV vypnout agenta. Tím se zruší všechny probíhající výpočty cesty (Jobs).
        if (_agent != null)
        {
            _agent.isStopped = true;
            _agent.ResetPath(); // Zahoď starou cestu
            _agent.enabled = false; // Vypni komponentu
        }

        // 2. Reset transformace
        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.identity;

        // Reset fyziky
        var rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = false;
            rb.linearVelocity = Vector3.zero; // (Unity 6) nebo .velocity ve starších
            rb.angularVelocity = Vector3.zero;
        }

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        // Reset Animatoru
        if (_animator != null)
        {
            _animator.Rebind();
            _animator.Update(0f);
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
        // 1. Společné kontroly (pokud je mrtvý nebo se spawnuje, nic neděláme)
        if (_health.CurrentHealth.Value <= 0 || _isSpawning.Value) return;

        // 2. SERVER LOGIKA (AI, Pathfinding)
        if (IsServer)
        {
            if (Time.time > _lastSearchTime + SEARCH_INTERVAL)
            {
                FindClosestPlayer();
                _lastSearchTime = Time.time;
            }

            if (_targetPlayer != null)
            {
                BehaviorLogic();

                if (_animator != null)
                {
                    _animator.SetBool("Moving", _agent.velocity.magnitude > 0.1f);
                }
            }
        }
        // 3. KLIENT LOGIKA (Tvoje interpolace rotace)
        else
        {
            // Vypočítáme směr pohybu z rozdílu pozic
            Vector3 movementDir = (transform.position - _lastPos).normalized;

            // Pokud se pohnul
            if ((transform.position - _lastPos).sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(movementDir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 10f);
            }

            _lastPos = transform.position;
        }
        _lastPos = transform.position;
    }

    public virtual void InitializeEnemy(
        EnemyTier tier, int hp, int damage, float speed, float scaleMultiplier, float attackRate, float knockbackResistance, int xp, Vector3 pos)
    {
        // 1. Nastavení základních statů
        _tier = tier;
        _currentDamage = damage;
        _currentSpeed = speed;
        _currentAttackRate = attackRate;
        _knockbackResistance = knockbackResistance;
        _xpReward = xp;

        // Aplikace rychlosti na Agenta
        if (_agent != null)
        {
            _agent.speed = speed;
            // Volitelně: Těžší nepřátelé se otáčejí pomaleji
            // _agent.angularSpeed = 720f * (1f - (knockbackResistance * 0.5f)); 
        }

        // 2. Nastavení HP (přes Health komponentu)
        if (_health != null)
        {
            _health.InitializeHealth(hp);
            // Pokud má Health komponenta metodu pro nastavení tieru/XP, zavolejte ji zde
            // _health.SetReward(xp); 
        }

        // 3. Vizuální změna (Server side)
        transform.localScale = Vector3.one * scaleMultiplier;

        // 4. Synchronizace vizuálu na klienty
        SetEnemyVisualsClientRpc(scaleMultiplier, tier);

        // 5. Warp na startovní pozici
        WarpAgentToPosition(pos);
    }

    [ClientRpc]
    private void SetEnemyVisualsClientRpc(float scale, EnemyTier tier)
    {
        // 1. Změna velikosti (Scale)
        transform.localScale = Vector3.one * scale;

        // 2. Změna barvy (pomocí MaterialPropertyBlock pro výkon)
        if (_modelRenderer != null)
        {
            // Načteme aktuální vlastnosti (abychom nepřepsali jiné věci)
            _modelRenderer.GetPropertyBlock(_propBlock);

            Color targetColor = Color.white; // Default (Normal)

            switch (tier)
            {
                case EnemyTier.Elite:
                    targetColor = new Color(1f, 0.8f, 0.2f); // Zlatá/Oranžová
                    break;
                case EnemyTier.Boss:
                    targetColor = new Color(1f, 0.3f, 0.3f); // Červená
                    break;
                // Normal zůstává bílý (nezměněná textura)
            }

            // Nastavíme barvu. 
            // "_BaseColor" je standard pro URP. 
            // "_Color" je standard pro Built-in pipeline.
            // Pro jistotu zkusíme nastavit oboje, nebo si vyber podle tvého render pipeline.
            _propBlock.SetColor("_BaseColor", targetColor); 
            _propBlock.SetColor("_Color", targetColor);

            // Aplikujeme zpět na renderer
            _modelRenderer.SetPropertyBlock(_propBlock);
        }
    }

    public void SetTier(EnemyTier tier)
    {
        _tier = tier;
        // Pokud se změní staty (HP/DMG), aplikujte je zde

        // DŮLEŽITÉ: Předat Tier do Health komponenty
        if (_health != null)
        {
            _health.SetEnemyTier(tier);
        }
    }

    protected abstract void BehaviorLogic();

    // --- POHYB ---

    protected virtual void MoveToTarget()
    {
        if (!IsServer || _targetPlayer == null || !_agent.enabled || !_agent.isOnNavMesh) return;
        // 1. Zrušení "přemýšlení" - agent nebude brzdit před cílem, prostě jím "projede" (nebo narazí do collideru)
        if (_agent.autoBraking) _agent.autoBraking = false;

        // 2. Plynulejší rotace - když se hýbe, nechť rotuje NavMeshAgent (je to plynulejší než Lerp v Update)
        _agent.updateRotation = true;

        // 3. Odstranění "zasekávání" - nevolat SetDestination každý frame!
        if (Time.time >= _pathUpdateTimer)
        {
            UpdatePathingRate();
            _pathUpdateTimer = Time.time + _currentPathUpdateInterval;
            _agent.SetDestination(_targetPlayer.position);
        }

        if (_agent.isStopped) _agent.isStopped = false;
    }

    protected virtual void StopMovement()
    {
        if (!IsServer || !_agent.enabled) return;

        _agent.isStopped = true;

        // Když stojí, chceme ho otáčet manuálně v Update (RotateToTarget), aby mířil na hráče při útoku
        _agent.updateRotation = false;

        // Reset velocity, aby "doklouzal" jen minimálně
        _agent.velocity = Vector3.zero;
    }

    protected void RotateToTarget()
    {
        // Pokud se hýbe pomocí Agenta, nezasahujeme do rotace manuálně (cukalo by to)
        if (_agent.enabled && !_agent.isStopped && _agent.updateRotation) return;

        if (_targetPlayer == null) return;

        Vector3 direction = (_targetPlayer.position - transform.position).normalized;
        direction.y = 0;

        if (direction != Vector3.zero)
        {
            Quaternion lookRotation = Quaternion.LookRotation(direction);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, lookRotation, _rotationSpeed * Time.deltaTime);
        }
    }

    protected void FindClosestPlayer()
    {
        float closestDistSq = float.MaxValue; // Používáme čtverec vzdálenosti
        Transform bestTarget = null;

        // Předpočítáme si AggroRange na druhou (např. 100*100 = 10000)
        // Ideálně to mějte v proměnné _aggroRangeSqr vypočítané v Awake
        float aggroRangeSqr = _aggroRange * _aggroRange;

        Vector3 myPos = transform.position; // Cachování transformu (drobná pomoc)

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                // Rychlý výpočet bez odmocniny
                float dSq = (myPos - client.PlayerObject.transform.position).sqrMagnitude;

                if (dSq < closestDistSq && dSq <= aggroRangeSqr)
                {
                    closestDistSq = dSq;
                    bestTarget = client.PlayerObject.transform;
                }
            }
        }
        _targetPlayer = bestTarget;
    }

    // --- SMRT A POŠKOZENÍ ---

    protected virtual void HandleDamage(int damage)
    {
        // Vypočítáme šanci na "odolání" knockbacku
        // Pokud je Resistance 1.0, podmínka (1f >= 1f) je pravdivá -> return (žádný knockback)
        // Pokud je Resistance 0.0, podmínka (0f > Random) -> malá šance, většinou projde

        // Varianta A: Knockback se vůbec nestane (Hard resist)
        if (_knockbackResistance >= 1.0f) return;

        // Varianta B: Náhodná šance na odolání (Soft resist)
        // Např. s rezistencí 0.7 má 70% šanci, že se nepohne
        if (Random.value < _knockbackResistance) return;

        // Pokud prošlo, spustíme rutinu
        // (Volitelně můžete délku knockbacku zkrátit podle rezistence)
        StartCoroutine(KnockbackRoutine());
    }

    private IEnumerator KnockbackRoutine()
    {
        if (_agent.enabled) _agent.enabled = false;

        // Délka stun efektu zmenšená o rezistenci (min 0.05s)
        float duration = 0.2f * (1.0f - _knockbackResistance);
        if (duration < 0.05f) duration = 0.05f;

        yield return new WaitForSeconds(duration);

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
        // 1. Malý "výskok" při smrti (vizuální bounce)
        Vector3 startScale = transform.localScale;
        Vector3 bounceScale = startScale * 1.2f;
        float timer = 0;
        float duration = 0.5f;

        // Fáze 1: Krátké zvětšení (overshoot)
        while (timer < 0.15f)
        {
            timer += Time.deltaTime;
            transform.localScale = Vector3.Lerp(startScale, bounceScale, timer / 0.15f);
            yield return null;
        }

        // Fáze 2: Smrštění do nuly
        timer = 0;
        while (timer < duration)
        {
            timer += Time.deltaTime;
            // Použití EaseInBack nebo plynulý Lerp k nule
            transform.localScale = Vector3.Lerp(bounceScale, Vector3.zero, timer / duration);

            // Volitelně: Rotace během mizení
            transform.Rotate(Vector3.up, 180f * Time.deltaTime);
            yield return null;
        }

        _health.DestroySelf();
    }

    protected void UpdatePathingRate()
    {
        if (_targetPlayer == null) return;

        // Vypočítáme čtverec vzdálenosti (rychlejší než Vector3.Distance)
        float distSq = (_targetPlayer.position - transform.position).sqrMagnitude;

        // 400 = 20 metrů (20 * 20). 
        // Pokud je hráč dál než 20 metrů, hledáme cestu jen 1x za sekundu.
        if (distSq > 400f)
        {
            _currentPathUpdateInterval = 1.0f;
        }
        else
        {
            // Pokud je blízko, reagujeme rychle (0.2s)
            _currentPathUpdateInterval = 0.2f;
        }
    }

    public void WarpAgentToPosition(Vector3 pos)
    {
        if (_agent != null)
        {
            // Agent musí být vypnutý, když měníme pozici transformu o velký kus
            _agent.enabled = false;
            transform.position = pos;

            // Teď ho zapneme
            _agent.enabled = true;

            // A pro jistotu ho warpnem na NavMesh, aby nelevitoval
            if (_agent.isOnNavMesh)
            {
                _agent.Warp(pos);
            }
        }
    }


}