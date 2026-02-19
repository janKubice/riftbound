using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public abstract class EnemyBaseAI : NetworkBehaviour
{
    [Header("Base Settings")]
    [SerializeField] protected float _aggroRange = 10000f;
    [SerializeField] protected float _rotationSpeed = 720f;
    [SerializeField] protected float _spawnDuration = 0.1f; // Kratší spawn, když není animace
    [SerializeField] protected EnemyTier _tier = EnemyTier.Normal;
    [HideInInspector] public Transform TargetPlayer;
    [HideInInspector] public Transform MyTransform;
    protected int _baseDamage;
    protected int _currentDamage;
    protected float _currentSpeed;
    protected float _currentAttackRate = 1.0f;     // Útoky za sekundu
    protected float _knockbackResistance = 0f;     // 0 = plný odlet, 1 = ani se nehne
    protected int _xpReward = 0;
    public Vector3 _targetOffset;
    [Header("References")]

    protected NavMeshAgent _agent;
    protected EnemyHealth _health;
    protected Transform _targetPlayer;
    protected NetworkVariable<bool> _isSpawning = new NetworkVariable<bool>(true);


    [Header("Visuals")]
    // Pokud máš model jako dítě objektu, přiřaď ho sem v Inspectoru nebo ho najdeme v Awake
    [SerializeField] protected Renderer _modelRenderer; 
    
    // Optimalizace: Umožňuje měnit barvu bez duplikace materiálu (Draw Call Batching)
    private MaterialPropertyBlock _propBlock;
    // Flag pro Manager: Pokud je TRUE, Manager tento frame ignoruje pohyb
    public bool IsMovementPaused { get; set; } = false;
    [HideInInspector] public Vector3 CachedSeparation = Vector3.zero;
    protected virtual void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _health = GetComponent<EnemyHealth>();
        MyTransform = transform;
        if (_agent != null)
        {
        }

        _propBlock = new MaterialPropertyBlock();

        if (_modelRenderer == null)
        {
            _modelRenderer = GetComponentInChildren<Renderer>();
        }

        if (_agent != null)
        {
            _agent.updatePosition = false; // Manuální synchronizace pozice
            _agent.updateRotation = false; // Manuální rotace
            _agent.updateUpAxis = false;   // Necháme true jen pokud je terén extrémně kopcovitý, jinak false
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; // Vypne drahé RVO
            _agent.acceleration = 60f; // Rychlý rozjezd (default je 8)
            _agent.angularSpeed = 720f; // Rychlé otáčení agenta (pokud ho řídí NavMesh)
            _agent.autoBraking = false; // Nezastavovat před cílem, pokud to neřídíme manuálně
            _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance; // Pro hordy entit šetří CPU

        }
        _targetOffset = new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f));
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {

            // Reset stavu
            _isSpawning.Value = true;
            
            // Registrace do Managera (pokud existuje)
            if (EnemyMovementManager.Instance != null)
            {
                EnemyMovementManager.Instance.RegisterEnemy(this);
            }
            else
            {
                Debug.LogError("EnemyMovementManager chybí ve scéně! AI se nebude hýbat.");
            }
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

            if (EnemyMovementManager.Instance != null)
                EnemyMovementManager.Instance.UnregisterEnemy(this);
        }
    }

    private void ResetEnemyState()
    {
        // 1. NEJDŘÍV vypnout agenta. Tím se zruší všechny probíhající výpočty cesty (Jobs).
        if (_agent != null)
        {
            _agent.isStopped = true;
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
    }

    private IEnumerator SpawnRoutine()
    {
        // Efekt vynoření ze země
        float timer = 0f;
        Vector3 endScale = Vector3.one;
        Vector3 startScale = Vector3.zero;

        while (timer < _spawnDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / _spawnDuration;
            
            // Easing (SmoothStep)
            progress = progress * progress * (3f - 2f * progress);
            
            MyTransform.localScale = Vector3.Lerp(startScale, endScale, progress);
            yield return null;
        }

        MyTransform.localScale = endScale;
        
        if (IsServer)
        {
            _isSpawning.Value = false;
        }
    }


    /// <summary>
    /// Hlavní smyčka pro logiku útoku. Pohyb je řešen externě,
    /// ale útočení a cooldowny si řeší každá instance sama.
    /// </summary>
    protected virtual void Update()
    {
        if (!IsServer || _isSpawning.Value) return;

        // Pokud máme cíl a jsme naživu, vykonáváme logiku chování (útoky)
        if (TargetPlayer != null)
        {
            BehaviorLogic();
        }
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
        _health.IsInvulnerable = false;

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

    public virtual void BehaviorLogic() { }

    protected void RotateToTarget()
    {
        if (TargetPlayer == null) return;
        
        Vector3 dir = (TargetPlayer.position - MyTransform.position).normalized;
        dir.y = 0; // Nechceme se naklánět nahoru/dolů
        
        if (dir != Vector3.zero)
        {
            Quaternion rot = Quaternion.LookRotation(dir);
            MyTransform.rotation = Quaternion.RotateTowards(MyTransform.rotation, rot, _rotationSpeed * Time.deltaTime);
        }
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

    /// <summary>
    /// Aplikuje pohyb vypočítaný Managerem.
    /// </summary>
    /// <param name="velocity">Vektor pohybu (směr * rychlost)</param>
    public void ManualMove(Vector3 velocity)
    {
        if (_isSpawning.Value) return;

        float speed = velocity.magnitude;

        // 1. Posun NavMeshAgenta (virtuální pozice na mapě)
        // Agent zajistí, že nevyběhneme z NavMeshe (validace pozice)
        _agent.nextPosition = MyTransform.position + velocity * Time.deltaTime;

        // 2. Synchronizace vizuálního Transformu s Agentem
        MyTransform.position = _agent.nextPosition;

        // 3. Rotace směrem k pohybu
        if (speed > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(velocity.normalized);
            MyTransform.rotation = Quaternion.RotateTowards(MyTransform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
        }
    }


}