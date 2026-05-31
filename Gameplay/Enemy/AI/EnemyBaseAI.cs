using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;
using System.Collections;

[RequireComponent(typeof(EnemyHealth))]
[RequireComponent(typeof(CharacterController))]
public abstract class EnemyBaseAI : NetworkBehaviour
{
    [Header("Definition")]
    [SerializeField] protected EnemyDefinition _definition;
    public EnemyDefinition Definition => _definition;

    [Header("Base Settings")]
    [SerializeField] protected EnemyTier _tier = EnemyTier.Normal;
    [SerializeField] public bool isDummy = false; // Pokud je TRUE, Manager tento objekt ignoruje (nepočítá se do aggro, pohybu, atd.)
    [HideInInspector] public Transform TargetPlayer;
    [HideInInspector] public Transform MyTransform;
    private float _verticalVelocity = 0f;

    [Header("Runtime Stats")]
    protected int _currentDamage;
    protected float _currentSpeed;
    public float CurrentSpeed
    {
        get
        {
            if (_statusReceiver != null)
                return _currentSpeed * _statusReceiver.CurrentSpeedMultiplier;
            return _currentSpeed;
        }
    }
    protected float _currentAttackRate = 1.0f;     // Útoky za sekundu
    protected float _knockbackResistance = 0f;     // 0 = plný odlet, 1 = ani se nehne
    protected int _xpReward = 0;
    protected float _aggroRange;
    protected float _rotationSpeed;
    protected float _spawnDuration;
    protected float _targetScale = 1.0f;

    [Header("Movement Feel")]
    [SerializeField] protected float _desiredStopDistance = 1.1f;
    [SerializeField] private float _movementAcceleration = 22f;
    [SerializeField] private float _movementDeceleration = 30f;
    [SerializeField] private float _minimumRotationSpeed = 0.08f;

    private Vector3 _smoothedHorizontalVelocity = Vector3.zero;
    private Coroutine _spawnRoutineHandle;

    public Vector3 _targetOffset;
    [Header("References")]
    protected EnemyHealth _health;
    protected Transform _targetPlayer;
    protected NetworkVariable<bool> _isSpawning = new NetworkVariable<bool>(true);
    protected CharacterController _controller;
    protected StatusEffectReceiver _statusReceiver;
    private bool _isMovementPaused = false;

    [Header("Movement Mode")]
    [SerializeField] protected bool _useFlowFieldMovement = true;

    public bool UsesFlowFieldMovement => _useFlowFieldMovement;
    public float DesiredStopDistance => Mathf.Max(0.25f, _desiredStopDistance * Mathf.Max(1f, _targetScale));
    public bool IsSpawning => _isSpawning.Value;
    public bool IsAlive => _health != null && _health.CurrentHealth.Value > 0;

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
    public bool IsMovementPaused
    {
        get { return _isMovementPaused || (_statusReceiver != null && _statusReceiver.IsStunned); }
        set { _isMovementPaused = value; }
    }
    [HideInInspector] public Vector3 CachedSeparation = Vector3.zero;
    public float StablePreferredSide { get; private set; }
    protected virtual void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _health = GetComponent<EnemyHealth>();
        MyTransform = transform;
        _propBlock = new MaterialPropertyBlock();
        _statusReceiver = GetComponent<StatusEffectReceiver>();

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
            // SpawnRoutine se spouští až z InitializeEnemy(), aby neběžely dvě spawn animace současně.

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
        _smoothedHorizontalVelocity = Vector3.zero;
        _verticalVelocity = 0f;
        IsMovementPaused = false;
    }

    private void BeginSpawn()
    {
        if (_spawnRoutineHandle != null)
            StopCoroutine(_spawnRoutineHandle);

        _isSpawning.Value = true;
        if (_health != null)
            _health.IsInvulnerable = true;

        _spawnRoutineHandle = StartCoroutine(SpawnRoutine());
    }

    private IEnumerator SpawnRoutine()
    {
        // 1. Zablokujeme fyziku, aby se nepřítel nezasekl o zem, když vyjíždí
        if (_controller != null) _controller.enabled = false;

        // 2. Rozešleme klientům příkaz k instanciaci částic (VFX)
        PlaySpawnEffectClientRpc(MyTransform.position);

        float timer = 0f;

        // 3. Výpočet pozic
        Vector3 endPosition = MyTransform.position;
        // Odhadneme hloubku podle výšky kontroleru (nebo fixní hodnoty, např. 2f)
        float depth = _controller != null ? Mathf.Max(1f, _controller.height) : 2.0f;
        Vector3 startPosition = endPosition - new Vector3(0, depth, 0);

        // Nastavíme počáteční polohu pod zem a scale na 0
        MyTransform.position = startPosition;
        MyTransform.localScale = Vector3.zero;

        // 4. Samotná animace
        while (timer < _spawnDuration)
        {
            timer += Time.deltaTime;
            float progress = _spawnDuration <= 0f ? 1f : timer / _spawnDuration;

            // Easing (SmoothStep) pro plynulý dojezd a zpomalení na konci
            progress = progress * progress * (3f - 2f * progress);

            // Interpolace pozice (vyjetí) i velikosti (zvětšení)
            MyTransform.position = Vector3.Lerp(startPosition, endPosition, progress);
            MyTransform.localScale = Vector3.Lerp(Vector3.zero, Vector3.one * _targetScale, progress);

            yield return null;
        }

        // 5. Finální dorovnání pro jistotu
        MyTransform.position = endPosition;
        MyTransform.localScale = Vector3.one * _targetScale;

        // 6. Opětovné zapnutí fyziky
        if (_controller != null) _controller.enabled = true;

        if (IsServer)
        {
            _isSpawning.Value = false;
            if (_health != null)
                _health.IsInvulnerable = false;
        }

        _spawnRoutineHandle = null;
    }

    [ClientRpc]
    private void PlaySpawnEffectClientRpc(Vector3 pos)
    {
        // Zde využíváme náš Single Source of Truth - Definici
        if (_definition != null && _definition.SpawnVFX != null)
        {
            // Mírný posun nahoru, aby částice nebyly úplně utopené v zemi
            Vector3 vfxPos = pos + new Vector3(0, 0.2f, 0);
            GameObject vfx = Instantiate(_definition.SpawnVFX, vfxPos, Quaternion.identity);

            // Úklid paměti po 5 sekundách (pokud se částice nezničí sama)
            Destroy(vfx, 5f);
        }
    }

    /// <summary>
    /// Hlavní smyčka pro logiku útoku. Pohyb je řešen externě,
    /// ale útočení a cooldowny si řeší každá instance sama.
    /// </summary>
    protected virtual void Update()
    {
        if (!IsServer || _isSpawning.Value)
            return;

        // Nepřítel je zastaven, neprovádí AI logiku
        if (_statusReceiver != null && _statusReceiver.IsStunned)
            return;

        if (TargetPlayer != null && _useFlowFieldMovement)
        {
            EvaluateMobilityState();
        }
    }

    public virtual void InitializeEnemy(EnemyTier tier, EnemyDefinition def, float finalScale, Vector3 pos)
    {
        _definition = def;
        // 1. Zablokování fyziky před modifikacemi
        if (_controller != null) _controller.enabled = false;

        // 2. Aplikace prostorových změn
        transform.localScale = Vector3.one * finalScale;

        // 3. Výpočet absolutního maxima a definice Step Offsetu PŘED aktivací
        if (_controller != null)
        {
            float maxStepOffset = (_controller.height + _controller.radius * 2f) * finalScale;
            _controller.stepOffset = Mathf.Min(0.3f, maxStepOffset - 0.01f);
        }

        // 4. Zápis metadat
        _tier = tier;
        _currentDamage = Mathf.RoundToInt(_definition.BaseDamage * finalScale);
        _currentSpeed = _definition.BaseSpeed * (1f + (finalScale * 0.03f) + (finalScale * 0.05f));
        _currentAttackRate = _definition.BaseAttackRate * (1f + Mathf.Clamp((finalScale - 1f) * 0.2f, 0f, 0.5f));
        _knockbackResistance = _definition.BaseKnockbackResistance + (1f - _definition.BaseKnockbackResistance) * (1f - (1f / finalScale));
        _xpReward = Mathf.RoundToInt(_definition.BaseXPDrop * finalScale);
        _health.IsInvulnerable = true;
        _targetScale = (finalScale / 2) * def.defaultScale;
        _aggroRange = _definition._aggroRange;
        _rotationSpeed = _definition._rotationSpeed;
        _spawnDuration = _definition._spawnDuration;

        int calculatedMaxHp = Mathf.RoundToInt(_definition.BaseHealth * finalScale);

        // 5. Inicializace subsystémů (zde dochází k Collider.enabled = true)
        if (_health != null)
        {
            _health.InitializeHealth(def, tier, calculatedMaxHp);
            _health.SetEnemyTier(tier);
        }

        SetEnemyVisualsClientRpc(finalScale, tier);
        StablePreferredSide = CalculateStablePreferredSide();
        // 6. Finální přesun a opětovná aktivace kontroleru
        WarpAgentToPosition(pos);

        BeginSpawn();
    }

    private float CalculateStablePreferredSide()
    {
        unchecked
        {
            ulong id = NetworkObjectId != 0 ? NetworkObjectId : (ulong)(uint)GetInstanceID();
            uint hash = (uint)id;

            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;

            float side = (hash & 1u) == 0u ? -1f : 1f;
            float strength = 0.65f + (((hash >> 8) & 255u) / 255f) * 0.35f;
            return side * strength;
        }
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
        if (_isSpawning.Value)
            return;

        Vector3 targetHorizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);

        if (_statusReceiver != null && _statusReceiver.IsStunned)
            targetHorizontalVelocity = Vector3.zero;

        float maxDelta = targetHorizontalVelocity.sqrMagnitude > _smoothedHorizontalVelocity.sqrMagnitude
            ? _movementAcceleration * Time.deltaTime
            : _movementDeceleration * Time.deltaTime;

        _smoothedHorizontalVelocity = Vector3.MoveTowards(
            _smoothedHorizontalVelocity,
            targetHorizontalVelocity,
            maxDelta
        );

        if (_controller != null && _controller.enabled)
        {
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -2.0f;
            else
                _verticalVelocity += Physics.gravity.y * Time.deltaTime;

            Vector3 finalVelocity = _smoothedHorizontalVelocity;
            finalVelocity.y = _verticalVelocity;

            CollisionFlags flags = _controller.Move(finalVelocity * Time.deltaTime);

            if ((flags & CollisionFlags.Below) != 0 && _verticalVelocity < 0f)
                _verticalVelocity = -2.0f;
        }

        Vector3 flatVelocity = _smoothedHorizontalVelocity;
        if (flatVelocity.sqrMagnitude > _minimumRotationSpeed * _minimumRotationSpeed)
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

    protected void RotateToPoint(Vector3 worldPoint)
    {
        Vector3 dir = (worldPoint - MyTransform.position);
        dir.y = 0f;

        if (dir.sqrMagnitude < 0.0001f)
            return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized);
        MyTransform.rotation = Quaternion.RotateTowards(
            MyTransform.rotation,
            targetRot,
            _rotationSpeed * Time.deltaTime
        );
    }

    private void ForceUnstuck()
    {
        if (TargetPlayer == null)
            return;

        Vector3 escapeVector = TargetPlayer.position - MyTransform.position;
        escapeVector.y = 0f;

        if (escapeVector.sqrMagnitude < 0.0001f)
            escapeVector = MyTransform.forward;
        else
            escapeVector.Normalize();

        // Původních +1.5m nahoru způsobovalo nepřirozené poskočení.
        // Tohle je jemný nudge; skutečnou práci má dělat flow field + separation.
        Vector3 nudge = (escapeVector * 0.35f) + (Vector3.up * 0.15f);

        if (_controller != null && _controller.enabled)
            _controller.Move(nudge);
        else
            MyTransform.position += nudge;

        _positionSnapshot = MyTransform.position;
        _smoothedHorizontalVelocity = Vector3.zero;
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