using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class HealerEnemyAI : EnemyBaseAI
{
    [Header("Healer Aura")]
    [SerializeField] private float _auraRadius = 5f;
    [SerializeField] private float _tickRate = 1.5f;
    [SerializeField] private StatusEffectData _healAuraEffect;
    [SerializeField] private LayerMask _enemyLayer;

    [Header("Custom Movement Tick")]
    [Tooltip("Zapni, pokud tvůj EnemyMovementManager nevolá BehaviorLogic pro enemy s vypnutým flow-field movementem. Frame guard zabrání dvojitému pohybu, i kdyby manager BehaviorLogic volal také.")]
    [SerializeField] private bool _driveBehaviorFromUpdate = true;

    [Header("Target Selection")]
    [SerializeField] private float _seekRadius = 15f;
    [SerializeField] private float _targetRefreshRate = 0.45f;
    [SerializeField] private float _targetSwitchBias = 0.35f;

    [Header("Support Positioning")]
    [Tooltip("Ideální vzdálenost healera od hráče, když zrovna neutíká.")]
    [SerializeField] private float _preferredPlayerDistance = 10f;

    [Tooltip("Pokud je healer blíž než tato hodnota, bude primárně couvat od hráče.")]
    [SerializeField] private float _minimumPlayerDistance = 6.5f;

    [Tooltip("Pokud je healer dál než tato hodnota a nemá injured ally, vrátí se blíž do boje.")]
    [SerializeField] private float _maximumPlayerDistance = 14f;

    [Tooltip("Healer nestojí přímo na ally, ale kousek za ním směrem od hráče.")]
    [SerializeField] private float _allySupportBackDistance = 2.2f;

    [Tooltip("Malý boční offset, aby healer nestál přesně v jedné linii s ally.")]
    [SerializeField] private float _allySupportSideOffset = 1.2f;

    [Tooltip("Když je healer už dost blízko své cílové pozici, přestane se tlačit dopředu.")]
    [SerializeField] private float _arrivalDistance = 0.35f;

    [Tooltip("Od této vzdálenosti od cílového bodu začne plynule zpomalovat.")]
    [SerializeField] private float _slowdownDistance = 3.2f;

    [Header("Movement Feel")]
    [SerializeField] private float _steeringSmoothTime = 0.08f;
    [SerializeField] private float _idleStrafeSpeedMultiplier = 0.28f;
    [SerializeField] private float _combatStrafeFlipIntervalMin = 2.0f;
    [SerializeField] private float _combatStrafeFlipIntervalMax = 4.5f;
    [SerializeField] private bool _facePlayerWhenHoldingPosition = true;

    [Header("Anti Clumping")]
    [SerializeField] private float _allyAvoidanceRadius = 1.6f;
    [SerializeField] private float _allyAvoidanceStrength = 1.2f;
    [SerializeField] private float _avoidanceRefreshInterval = 0.12f;

    private static readonly Collider[] _alliesInRange = new Collider[40];
    private static readonly Collider[] _avoidanceHits = new Collider[16];

    private readonly HashSet<StatusEffectReceiver> _auraReceivers = new HashSet<StatusEffectReceiver>();
    private readonly HashSet<EnemyHealth> _seenAllies = new HashSet<EnemyHealth>();

    private EnemyHealth _targetAlly;
    private Vector3 _smoothedVelocity;
    private Vector3 _smoothVelocityRef;
    private Vector3 _cachedAvoidanceVelocity;

    private float _nextAvoidanceRefreshTime;
    private float _nextStrafeFlipTime;
    private int _strafeDirection = 1;
    private int _lastBehaviorFrame = -1;

    protected override void Awake()
    {
        base.Awake();

        // Healer má vlastní chování. Nechceme, aby ho tahal základní flow-field jako obyčejného melee enemáka.
        _useFlowFieldMovement = false;
        _strafeDirection = Random.value < 0.5f ? -1 : 1;
        ScheduleNextStrafeFlip();
    }

    protected override void Update()
    {
        base.Update();

        if (IsServer && _driveBehaviorFromUpdate)
            BehaviorLogic();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsServer)
        {
            // Důležité: healer se flow-fieldu neúčastní přes _useFlowFieldMovement = false.
            // IsMovementPaused musí zůstat false, jinak ho některé movement managery začnou po framu/pulsech skipovat.
            IsMovementPaused = false;

            StartCoroutine(AuraTickRoutine());
            StartCoroutine(TargetSelectionRoutine());
        }
    }

    private IEnumerator AuraTickRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0f, Mathf.Max(0.05f, _tickRate)));

        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.05f, _tickRate));
        while (true)
        {
            yield return wait;

            if (_isSpawning.Value || _health == null || _health.CurrentHealth.Value <= 0)
                continue;

            ApplyAura();
        }
    }

    private void ApplyAura()
    {
        if (_healAuraEffect == null)
            return;

        _auraReceivers.Clear();

        int hits = Physics.OverlapSphereNonAlloc(
            transform.position,
            _auraRadius,
            _alliesInRange,
            _enemyLayer,
            QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hits; i++)
        {
            Collider col = _alliesInRange[i];
            if (col == null)
                continue;

            StatusEffectReceiver receiver = col.GetComponentInParent<StatusEffectReceiver>();
            if (receiver == null)
                continue;

            // Pokud má enemy více colliderů, nechceme mu jeden tick aplikovat vícekrát.
            if (_auraReceivers.Add(receiver))
            {
                receiver.ApplyStatusEffect(_healAuraEffect);
            }
        }
    }

    private IEnumerator TargetSelectionRoutine()
    {
        yield return new WaitForSeconds(Random.Range(0f, Mathf.Max(0.05f, _targetRefreshRate)));

        WaitForSeconds wait = new WaitForSeconds(Mathf.Max(0.1f, _targetRefreshRate));
        while (true)
        {
            yield return wait;

            if (_isSpawning.Value || _health == null || _health.CurrentHealth.Value <= 0)
                continue;

            FindBestAllyToSupport();
        }
    }

    private void FindBestAllyToSupport()
    {
        int hits = Physics.OverlapSphereNonAlloc(
            transform.position,
            _seekRadius,
            _alliesInRange,
            _enemyLayer,
            QueryTriggerInteraction.Ignore
        );

        EnemyHealth bestAlly = null;
        float bestScore = float.MinValue;

        _seenAllies.Clear();

        for (int i = 0; i < hits; i++)
        {
            Collider col = _alliesInRange[i];
            if (col == null)
                continue;

            EnemyHealth allyHealth = col.GetComponentInParent<EnemyHealth>();
            if (allyHealth == null || allyHealth == _health)
                continue;

            if (!_seenAllies.Add(allyHealth))
                continue;

            if (allyHealth.CurrentHealth.Value <= 0 || !allyHealth.IsInjured)
                continue;

            float maxHealth = Mathf.Max(1f, allyHealth.MaxHealth);
            float healthPct = Mathf.Clamp01((float)allyHealth.CurrentHealth.Value / maxHealth);
            float missingPct = 1f - healthPct;

            float distance = FlatDistance(transform.position, allyHealth.transform.position);
            float distanceScore = 1f - Mathf.Clamp01(distance / Mathf.Max(0.01f, _seekRadius));

            // Nejvíc rozhoduje zranění, vzdálenost jen pomáhá vybrat rozumný cíl.
            float score = (missingPct * 3f) + (distanceScore * 0.65f);

            // Hysteresis: aktuální target si trochu držíme, aby healer každou půlvteřinu nepřepínal.
            if (allyHealth == _targetAlly)
                score += _targetSwitchBias;

            if (score > bestScore)
            {
                bestScore = score;
                bestAlly = allyHealth;
            }
        }

        _targetAlly = bestAlly;
    }

    public override void BehaviorLogic()
    {
        // BehaviorLogic může volat buď EnemyMovementManager, nebo náš Update().
        // Tohle brání dvojitému pohybu ve stejném framu.
        if (_lastBehaviorFrame == Time.frameCount)
            return;
        _lastBehaviorFrame = Time.frameCount;

        if (TargetPlayer == null || _isSpawning.Value || _health == null || _health.CurrentHealth.Value <= 0)
            return;

        if (_statusReceiver != null && _statusReceiver.IsStunned)
        {
            ManualMove(Vector3.zero);
            return;
        }

        UpdateStrafeDirection();

        Vector3 desiredVelocity = HasValidAllyTarget()
            ? GetSupportAllyVelocity(_targetAlly)
            : GetKitePlayerVelocity();

        desiredVelocity += GetCachedAllyAvoidanceVelocity();
        desiredVelocity = ClampHorizontalVelocity(desiredVelocity, CurrentSpeed);

        _smoothedVelocity = Vector3.SmoothDamp(
            _smoothedVelocity,
            desiredVelocity,
            ref _smoothVelocityRef,
            Mathf.Max(0.01f, _steeringSmoothTime)
        );

        // Volat ManualMove každý frame je důležité. Jinak base EnemyBaseAI nedostane možnost
        // plynule dobrzdit vlastní _smoothedHorizontalVelocity a healer působí jako move-stop-move.
        ManualMove(_smoothedVelocity);

        if (_facePlayerWhenHoldingPosition && _smoothedVelocity.sqrMagnitude <= 0.04f)
            RotateToPoint(TargetPlayer.position);
    }

    private bool HasValidAllyTarget()
    {
        if (_targetAlly == null)
            return false;

        if (_targetAlly.CurrentHealth.Value <= 0 || !_targetAlly.IsInjured)
            return false;

        float distance = FlatDistance(transform.position, _targetAlly.transform.position);
        return distance <= _seekRadius + 2f;
    }

    private Vector3 GetSupportAllyVelocity(EnemyHealth ally)
    {
        Vector3 healerPos = transform.position;
        Vector3 playerPos = TargetPlayer.position;
        Vector3 allyPos = ally.transform.position;

        Vector3 awayFromPlayerAtAlly = FlatDirection(playerPos, allyPos);
        if (awayFromPlayerAtAlly.sqrMagnitude < 0.0001f)
            awayFromPlayerAtAlly = FlatDirection(playerPos, healerPos);
        if (awayFromPlayerAtAlly.sqrMagnitude < 0.0001f)
            awayFromPlayerAtAlly = transform.forward;

        Vector3 side = new Vector3(-awayFromPlayerAtAlly.z, 0f, awayFromPlayerAtAlly.x) * _strafeDirection;

        // Healer si stoupne za ally, ne přímo do něj. Díky tomu pohyb vypadá jako support role.
        Vector3 supportPoint = allyPos
            + (awayFromPlayerAtAlly * _allySupportBackDistance)
            + (side * _allySupportSideOffset);

        float playerDistance = FlatDistance(healerPos, playerPos);
        Vector3 fleeVelocity = Vector3.zero;

        if (playerDistance < _minimumPlayerDistance)
        {
            Vector3 awayFromPlayer = FlatDirection(playerPos, healerPos);
            float danger = 1f - Mathf.Clamp01(playerDistance / Mathf.Max(0.01f, _minimumPlayerDistance));
            fleeVelocity = awayFromPlayer * CurrentSpeed * Mathf.Lerp(0.75f, 1.25f, danger);
        }

        Vector3 toSupportPoint = supportPoint - healerPos;
        toSupportPoint.y = 0f;

        float distanceToSupportPoint = toSupportPoint.magnitude;
        float distanceToAlly = FlatDistance(healerPos, allyPos);

        // Když už je healer v auře a není v nebezpečí, jen jemně strafeuje místo toho, aby furt šlapal do ally.
        if (distanceToAlly <= _auraRadius * 0.82f && distanceToSupportPoint <= _slowdownDistance && playerDistance >= _minimumPlayerDistance)
        {
            Vector3 idleStrafe = side * (CurrentSpeed * _idleStrafeSpeedMultiplier);
            return idleStrafe + fleeVelocity;
        }

        Vector3 approachVelocity = GetArrivalVelocity(toSupportPoint, distanceToSupportPoint);
        return approachVelocity + fleeVelocity;
    }

    private Vector3 GetKitePlayerVelocity()
    {
        Vector3 healerPos = transform.position;
        Vector3 playerPos = TargetPlayer.position;

        Vector3 awayFromPlayer = FlatDirection(playerPos, healerPos);
        if (awayFromPlayer.sqrMagnitude < 0.0001f)
            awayFromPlayer = -transform.forward;

        Vector3 side = new Vector3(-awayFromPlayer.z, 0f, awayFromPlayer.x) * _strafeDirection;
        float distanceToPlayer = FlatDistance(healerPos, playerPos);

        if (distanceToPlayer < _minimumPlayerDistance)
        {
            float danger = 1f - Mathf.Clamp01(distanceToPlayer / Mathf.Max(0.01f, _minimumPlayerDistance));
            return awayFromPlayer * CurrentSpeed * Mathf.Lerp(0.85f, 1.35f, danger);
        }

        if (distanceToPlayer > _maximumPlayerDistance)
        {
            Vector3 preferredPoint = playerPos + awayFromPlayer * _preferredPlayerDistance;
            Vector3 toPreferredPoint = preferredPoint - healerPos;
            toPreferredPoint.y = 0f;
            return GetArrivalVelocity(toPreferredPoint, toPreferredPoint.magnitude);
        }

        // Bez zraněného ally by support enemy neměl tupě běžet na hráče.
        // Jemně orbituje, aby působil živě, ale ne chaoticky.
        return side * (CurrentSpeed * _idleStrafeSpeedMultiplier);
    }

    private Vector3 GetArrivalVelocity(Vector3 toTarget, float distance)
    {
        if (distance <= _arrivalDistance || toTarget.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        float t = Mathf.InverseLerp(_arrivalDistance, Mathf.Max(_arrivalDistance + 0.01f, _slowdownDistance), distance);
        float speed = CurrentSpeed * Mathf.Clamp01(t);
        return toTarget.normalized * speed;
    }

    private Vector3 GetCachedAllyAvoidanceVelocity()
    {
        if (Time.time < _nextAvoidanceRefreshTime)
            return _cachedAvoidanceVelocity;

        _nextAvoidanceRefreshTime = Time.time + Mathf.Max(0.03f, _avoidanceRefreshInterval);
        _cachedAvoidanceVelocity = Vector3.zero;

        if (_allyAvoidanceRadius <= 0f || _allyAvoidanceStrength <= 0f)
            return Vector3.zero;

        int hits = Physics.OverlapSphereNonAlloc(
            transform.position,
            _allyAvoidanceRadius,
            _avoidanceHits,
            _enemyLayer,
            QueryTriggerInteraction.Ignore
        );

        Vector3 avoidance = Vector3.zero;
        int count = 0;

        for (int i = 0; i < hits; i++)
        {
            Collider col = _avoidanceHits[i];
            if (col == null)
                continue;

            EnemyHealth ally = col.GetComponentInParent<EnemyHealth>();
            if (ally == null || ally == _health || ally.CurrentHealth.Value <= 0)
                continue;

            Vector3 away = transform.position - ally.transform.position;
            away.y = 0f;

            float sqrMagnitude = away.sqrMagnitude;
            if (sqrMagnitude < 0.0001f)
                continue;

            float distance = Mathf.Sqrt(sqrMagnitude);
            float weight = 1f - Mathf.Clamp01(distance / _allyAvoidanceRadius);
            avoidance += away.normalized * weight;
            count++;
        }

        if (count > 0 && avoidance.sqrMagnitude > 0.0001f)
        {
            _cachedAvoidanceVelocity = avoidance.normalized * (CurrentSpeed * _allyAvoidanceStrength);
        }

        return _cachedAvoidanceVelocity;
    }

    private void UpdateStrafeDirection()
    {
        if (Time.time < _nextStrafeFlipTime)
            return;

        _strafeDirection *= -1;
        ScheduleNextStrafeFlip();
    }

    private void ScheduleNextStrafeFlip()
    {
        float min = Mathf.Max(0.1f, _combatStrafeFlipIntervalMin);
        float max = Mathf.Max(min, _combatStrafeFlipIntervalMax);
        _nextStrafeFlipTime = Time.time + Random.Range(min, max);
    }

    private static Vector3 ClampHorizontalVelocity(Vector3 velocity, float maxSpeed)
    {
        velocity.y = 0f;
        float maxSqr = maxSpeed * maxSpeed;

        if (velocity.sqrMagnitude > maxSqr)
            velocity = velocity.normalized * maxSpeed;

        return velocity;
    }

    private static Vector3 FlatDirection(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        dir.y = 0f;

        float sqrMagnitude = dir.sqrMagnitude;
        if (sqrMagnitude < 0.0001f)
            return Vector3.zero;

        return dir / Mathf.Sqrt(sqrMagnitude);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.35f);
        Gizmos.DrawWireSphere(transform.position, _auraRadius);

        Gizmos.color = new Color(0.35f, 0.75f, 1f, 0.25f);
        Gizmos.DrawWireSphere(transform.position, _seekRadius);

        if (TargetPlayer != null)
        {
            Gizmos.color = new Color(1f, 0.65f, 0.1f, 0.35f);
            Gizmos.DrawWireSphere(TargetPlayer.position, _minimumPlayerDistance);

            Gizmos.color = new Color(1f, 1f, 0.1f, 0.2f);
            Gizmos.DrawWireSphere(TargetPlayer.position, _preferredPlayerDistance);
        }
    }
#endif
}
