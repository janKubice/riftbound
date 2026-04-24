using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(CharacterController))]
public abstract class EnemyBaseAI : NetworkBehaviour
{
    [Header("Base Settings")]
    [SerializeField] protected float _aggroRange = 10000f;
    [SerializeField] protected float _rotationSpeed = 720f;
    [SerializeField] protected float _spawnDuration = 0.1f; // Kratší spawn, když není animace
    [SerializeField] protected EnemyTier _tier = EnemyTier.Normal;
    [HideInInspector] public Transform TargetPlayer;
    [HideInInspector] public Transform MyTransform;
    private float _verticalVelocity = 0f;
    protected int _baseDamage;
    protected int _currentDamage;
    protected float _currentSpeed;
    protected float _currentAttackRate = 1.0f;     // Útoky za sekundu
    protected float _knockbackResistance = 0f;     // 0 = plný odlet, 1 = ani se nehne
    protected int _xpReward = 0;
    public Vector3 _targetOffset;
    [Header("References")]

    protected EnemyHealth _health;
    protected Transform _targetPlayer;
    protected NetworkVariable<bool> _isSpawning = new NetworkVariable<bool>(true);
    protected float _targetScale = 1.0f;
    public float CurrentSpeed => _currentSpeed;
    protected CharacterController _controller;

    private Vector3 _positionSnapshot;
    private float _stuckCheckTimer = 0f;
    private const float STUCK_CHECK_INTERVAL = 1.0f;
    private const float MIN_MOVEMENT_DISTANCE_SQR = 0.05f;

    [Header("Visuals")]
    // Pokud máš model jako dítě objektu, přiřaď ho sem v Inspectoru nebo ho najdeme v Awake
    [SerializeField] protected Renderer _modelRenderer;

    [Header("Telegraph Framework")]
    protected Coroutine _telegraphRoutine;

    // Optimalizace: Umožňuje měnit barvu bez duplikace materiálu (Draw Call Batching)
    private MaterialPropertyBlock _propBlock;
    // Flag pro Manager: Pokud je TRUE, Manager tento frame ignoruje pohyb
    public bool IsMovementPaused { get; set; } = false;
    [HideInInspector] public Vector3 CachedSeparation = Vector3.zero;
    protected virtual void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _health = GetComponent<EnemyHealth>();
        MyTransform = transform;
        _propBlock = new MaterialPropertyBlock();

        if (_modelRenderer == null)
        {
            _modelRenderer = GetComponentInChildren<Renderer>();
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
        if (_controller != null)
        {
            _controller.enabled = false;

            // Rekalibrace středu a výšky válce k zachování kontaktu s podlahou
            float baseHeight = 2.0f; // Výchozí výška modelu
            float baseRadius = 0.5f; // Výchozí poloměr

            _controller.height = baseHeight * _targetScale;
            _controller.radius = baseRadius * _targetScale;
            _controller.center = new Vector3(0, _controller.height / 2f, 0);

            // Korekce stepOffset pro zabránění zasekávání
            _controller.stepOffset = Mathf.Clamp(0.5f * _targetScale, 0.1f, _controller.height / 2f);
        }

        transform.localScale = Vector3.one;
        transform.rotation = Quaternion.identity;
        IsMovementPaused = false;
    }

    private IEnumerator SpawnRoutine()
    {
        float timer = 0f;
        Vector3 startScale = Vector3.zero;

        while (timer < _spawnDuration)
        {
            timer += Time.deltaTime;
            float progress = timer / _spawnDuration;

            // Easing (SmoothStep)
            progress = progress * progress * (3f - 2f * progress);

            // OPRAVA: Dynamicky čteme _targetScale, kdyby se změnila během animace
            Vector3 currentEndScale = Vector3.one * _targetScale;
            MyTransform.localScale = Vector3.Lerp(startScale, currentEndScale, progress);
            yield return null;
        }

        MyTransform.localScale = Vector3.one * _targetScale;

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

        if (TargetPlayer != null)
        {
            BehaviorLogic();
            EvaluateMobilityState();
        }
    }

    public virtual void InitializeEnemy(
    EnemyTier tier, int hp, int damage, float speed, float scaleMultiplier, float attackRate, float knockbackResistance, int xp, Vector3 pos)
    {
        // 1. Zablokování fyziky před modifikacemi
        if (_controller != null) _controller.enabled = false;

        // 2. Aplikace prostorových změn
        transform.localScale = Vector3.one * scaleMultiplier;

        // 3. Výpočet absolutního maxima a definice Step Offsetu PŘED aktivací
        if (_controller != null)
        {
            float maxStepOffset = (_controller.height + _controller.radius * 2f) * scaleMultiplier;
            _controller.stepOffset = Mathf.Min(0.3f, maxStepOffset - 0.01f);
        }

        // 4. Zápis metadat
        _tier = tier;
        _currentDamage = damage;
        _currentSpeed = speed;
        _currentAttackRate = attackRate;
        _knockbackResistance = knockbackResistance;
        _xpReward = xp;
        _health.IsInvulnerable = false;
        _targetScale = scaleMultiplier / 2;

        // 5. Inicializace subsystémů (zde dochází k Collider.enabled = true)
        if (_health != null)
        {
            _health.InitializeHealth(hp);
        }

        SetEnemyVisualsClientRpc(scaleMultiplier, tier);

        // 6. Finální přesun a opětovná aktivace kontroleru
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
        IsMovementPaused = true;

        float duration = 0.2f * (1.0f - _knockbackResistance);
        if (duration < 0.05f) duration = 0.05f;

        yield return new WaitForSeconds(duration);

        if (_health.CurrentHealth.Value > 0)
        {
            IsMovementPaused = false;
        }
    }

    protected virtual void HandleDeath()
    {
        IsMovementPaused = true;
        if (_controller != null) _controller.enabled = false;
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
        if (_controller != null)
        {
            _controller.enabled = false;
            transform.position = pos;
            _controller.enabled = true;
        }
    }

    /// <summary>
    /// Aplikuje pohyb vypočítaný Managerem.
    /// </summary>
    /// <param name="velocity">Vektor pohybu (směr * rychlost)</param>
    public void ManualMove(Vector3 velocity)
    {
        if (_isSpawning.Value) return;

        // Extrakce horizontálního směru pro správnou rotaci nezávislou na pádu
        Vector3 flatVelocity = new Vector3(velocity.x, 0, velocity.z);
        float flatSpeed = flatVelocity.magnitude;

        if (_controller != null && _controller.enabled)
        {
            // Detekce dotyku se zemí
            if (_controller.isGrounded)
            {
                // Trvalý tlak k povrchu zabraňuje odskakování na klesajících svazích
                _verticalVelocity = -2.0f;
            }
            else
            {
                // Aplikace standardní Unity gravitace (lze násobit pro strmější pád)
                _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            }

            // Sloučení horizontálního pohybu s vypočítanou gravitací
            velocity.y = _verticalVelocity;

            _controller.Move(velocity * Time.deltaTime);
        }

        if (flatSpeed > 0.1f)
        {
            Quaternion targetRot = Quaternion.LookRotation(flatVelocity.normalized);
            MyTransform.rotation = Quaternion.RotateTowards(MyTransform.rotation, targetRot, _rotationSpeed * Time.deltaTime);
        }
    }

    private void EvaluateMobilityState()
    {
        if (IsMovementPaused || _controller == null || !_controller.enabled) return;

        _stuckCheckTimer += Time.deltaTime;
        if (_stuckCheckTimer >= STUCK_CHECK_INTERVAL)
        {
            float sqrDistanceMoved = (MyTransform.position - _positionSnapshot).sqrMagnitude;

            if (sqrDistanceMoved < MIN_MOVEMENT_DISTANCE_SQR)
            {
                ForceUnstuck();
            }

            _positionSnapshot = MyTransform.position;
            _stuckCheckTimer = 0f;
        }
    }

    private void ForceUnstuck()
    {
        Vector3 escapeVector = (TargetPlayer.position - MyTransform.position).normalized;

        // Nucená translace: 2.5m vpřed směrem k cíli, 1.5m vzhůru pro překonání překážky
        Vector3 warpPosition = MyTransform.position + (escapeVector * 0.5f) + (Vector3.up * 1.5f);

        WarpAgentToPosition(warpPosition);
    }

    #region Telegraph Framework
    /// <summary>
    /// Spustí server před útokem. Rozešle příkaz všem klientům k přehrání vizuálu.
    /// </summary>
    protected void TriggerTelegraph(float duration)
    {
        if (IsServer)
        {
            PlayTelegraphClientRpc(duration);
        }
    }

    [ClientRpc]
    private void PlayTelegraphClientRpc(float duration)
    {
        if (_telegraphRoutine != null) StopCoroutine(_telegraphRoutine);
        _telegraphRoutine = StartCoroutine(TelegraphVisualRoutine(duration));
    }

    /// <summary>
    /// Lokální smyčka na každém klientovi. Šetří síť, počítá si progress lokálně.
    /// </summary>
    private IEnumerator TelegraphVisualRoutine(float duration)
    {
        StartTelegraphVisual();
        float timer = 0f;

        while (timer < duration)
        {
            timer += Time.deltaTime;
            UpdateTelegraphVisual(Mathf.Clamp01(timer / duration));
            yield return null;
        }

        StopTelegraphVisual();
        _telegraphRoutine = null;
    }

    // --- VIRTUÁLNÍ METODY PRO POTOMKY ---

    public virtual void StartTelegraphVisual() { }

    /// <summary>
    /// Voláno každý frame během telegraph fáze.
    /// </summary>
    /// <param name="progress">0.0 (začátek) až 1.0 (konec)</param>
    public virtual void UpdateTelegraphVisual(float progress) { }

    public virtual void StopTelegraphVisual() { }

    #endregion

}