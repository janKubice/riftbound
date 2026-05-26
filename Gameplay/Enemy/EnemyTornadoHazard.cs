using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class EnemyTornadoHazard : EnemySpellBehaviour, IEnemySpellZone
{
    [Header("References")]
    [SerializeField] private SphereCollider _damageTrigger;
    [SerializeField] private Transform _visualRoot;

    [Header("Visual Scaling")]
    [Tooltip("Keep this OFF when the visual prefab was authored to look correct at local scale 1,1,1. The trigger radius will still be controlled by Zone Radius.")]
    [SerializeField] private bool _scaleVisualWithRadius = false;

    [Tooltip("Only used when Scale Visual With Radius is enabled. This is the tornado radius that your visual prefab represents at local scale 1,1,1.")]
    [SerializeField] private float _visualAuthoredRadius = 2.5f;

    [Tooltip("Extra visual-only multiplier. Does not affect damage radius or pull radius.")]
    [SerializeField] private float _visualScaleMultiplier = 1f;

    [Header("Tornado Shape")]
    [Tooltip("Base radius coming from the prefab or EnemySpellDefinition.ZoneRadius. Damage and pull can be smaller via the multipliers below.")]
    [SerializeField] private float _radius = 2.5f;

    [Tooltip("Damage radius = Radius * Damage Radius Multiplier.")]
    [SerializeField, Range(0.05f, 1f)] private float _damageRadiusMultiplier = 0.55f;

    [Tooltip("Pull radius = Radius * Pull Radius Multiplier.")]
    [SerializeField, Range(0.05f, 10f)] private float _pullRadiusMultiplier = 0.75f;

    [Tooltip("World-space vertical offset of the trigger center.")]
    [SerializeField] private float _triggerCenterHeight = 1.2f;

    [Tooltip("Compensates SphereCollider.radius for prefab/root scale so Radius behaves like world units. Keep this ON unless you intentionally scale the whole root object to make the gameplay area larger.")]
    [SerializeField] private bool _compensateColliderForTransformScale = true;

    [Header("Lifetime")]
    [SerializeField] private float _lifetime = 5f;

    [Header("Movement")]
    [SerializeField] private bool _moveForward = true;
    [SerializeField] private float _moveSpeed = 3f;

    [Header("Ground Following")]
    [Tooltip("Keeps the tornado attached to terrain while moving over slopes/hills. This prevents the visual and trigger from sinking into the ground.")]
    [SerializeField] private bool _followGround = true;

    [Tooltip("Set this to Terrain / Environment / Obstacle layers only. Do not include Player, Enemy, Projectile or Spell layers.")]
    [SerializeField] private LayerMask _groundMask = Physics.DefaultRaycastLayers;

    [Tooltip("How high above the detected ground the tornado root should stay. Usually very small, because the visual itself provides height.")]
    [SerializeField] private float _groundOffset = 0.05f;

    [Tooltip("Ray starts this many units above the desired tornado position. Increase this if the tornado still sinks into tall hills.")]
    [SerializeField] private float _groundRaycastStartHeight = 20f;

    [Tooltip("How far below the ray start the tornado searches for ground.")]
    [SerializeField] private float _groundRaycastDistance = 45f;

    [Tooltip("Maximum upward snap speed while following terrain.")]
    [SerializeField] private float _groundFollowMaxUpSpeed = 16f;

    [Tooltip("Maximum downward snap speed while following terrain.")]
    [SerializeField] private float _groundFollowMaxDownSpeed = 24f;

    [Tooltip("Immediately aligns the tornado to the ground when it spawns.")]
    [SerializeField] private bool _snapToGroundOnSpawn = true;

    [Header("Damage")]
    [SerializeField] private int _damagePerTick = 3;

    [Tooltip("Seconds between damage ticks while the player remains inside the effective damage radius.")]
    [SerializeField] private float _tickInterval = 2f;

    [Tooltip("Usually should be OFF for tornadoes. If ON, touching the tornado edge immediately deals damage before the first tick interval.")]
    [SerializeField] private bool _damageImmediatelyOnEnter = false;

    [Tooltip("If Damage Immediately On Enter is OFF, this controls the first damage delay. Negative value means: use Tick Interval.")]
    [SerializeField] private float _firstDamageDelay = -1f;

    [Tooltip("Extra safety minimum between direct tornado damage events per player. This does not control StatusEffectData ticks.")]
    [SerializeField] private float _minimumDirectDamageInterval = 0.5f;

    [Header("Status")]
    [SerializeField] private StatusEffectData _statusEffectOnTick;
    [SerializeField, Range(0f, 1f)] private float _statusApplyChance = 1f;

    [Tooltip("Prevents the tornado from re-applying the same status every damage tick. Useful if the status itself has damage/audio ticks, such as Burn.")]
    [SerializeField] private float _statusReapplyCooldown = 4f;

    [Header("Lift / Pull")]
    [SerializeField] private bool _applyLift = true;

    [Tooltip("Maximum horizontal pull velocity near the inner tornado ring. Keep this fairly low if SetExternalVelocityFromServer overrides player input.")]
    [SerializeField] private float _pullStrength = 0.85f;

    [Tooltip("Maximum upward velocity. High values make the tornado feel like a stun-lock.")]
    [SerializeField] private float _liftSpeed = 0.8f;

    [Tooltip("Small calm area in the center, as a fraction of the effective pull radius. This prevents endless center locking.")]
    [SerializeField, Range(0f, 0.8f)] private float _innerEyeRadiusRatio = 0.32f;

    [Tooltip("How much pull is applied near the outside edge. Lower means players can escape more naturally.")]
    [SerializeField, Range(0f, 1f)] private float _edgePullMultiplier = 0.05f;

    [Tooltip("Adds sideways swirl so the tornado feels like a vortex instead of a pure black hole.")]
    [SerializeField] private float _swirlStrength = 0.35f;

    [Tooltip("How often the server refreshes the external velocity. Do not do this every trigger callback unless you want very sticky movement/audio spam.")]
    [SerializeField] private float _forceApplyInterval = 0.16f;

    [Tooltip("Duration passed to PlayerController.SetExternalVelocityFromServer. Should usually be close to, but slightly below, Force Apply Interval.")]
    [SerializeField] private float _externalVelocityDuration = 0.12f;

    [Tooltip("Short delay before full force is applied after entering. Makes clipping the tornado edge less punishing.")]
    [SerializeField] private float _enterGraceDuration = 0.35f;

    [Tooltip("If enabled, the tornado will not apply pull/lift immediately from OnTriggerEnter. This avoids instant yank + repeated hit/impact feedback when the player barely touches the edge.")]
    [SerializeField] private bool _delayForceUntilNextInterval = true;

    [Tooltip("Very small computed external velocities are ignored. This avoids refreshing external movement for no visible benefit.")]
    [SerializeField] private float _minimumExternalVelocityToApply = 0.08f;

    [Header("Debug")]
    [SerializeField] private bool _debugTornado;

    private Rigidbody _rb;
    private Vector3 _moveDirection;
    private bool _initialized;
    private Vector3 _initialVisualLocalScale = Vector3.one;

    // Keyed by PlayerAttributes.GetInstanceID(), not by collider, so a player with many colliders is still affected only once per tick.
    private readonly Dictionary<int, PlayerAttributes> _activePlayers = new Dictionary<int, PlayerAttributes>();
    private readonly Dictionary<int, float> _nextDamageTimes = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _nextForceTimes = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _nextStatusApplyTimes = new Dictionary<int, float>();
    private readonly Dictionary<int, float> _enteredTimes = new Dictionary<int, float>();
    private readonly Dictionary<int, int> _overlapCounts = new Dictionary<int, int>();
    private readonly List<int> _targetsToRemove = new List<int>();
    private readonly RaycastHit[] _groundHits = new RaycastHit[12];

    private float EffectiveDamageRadius => Mathf.Max(0.05f, _radius * _damageRadiusMultiplier);
    private float EffectivePullRadius => Mathf.Max(0.05f, _radius * _pullRadiusMultiplier);
    private float EffectiveTriggerRadius => Mathf.Max(EffectiveDamageRadius, EffectivePullRadius);

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (_damageTrigger == null)
            _damageTrigger = GetComponent<SphereCollider>();

        _damageTrigger.isTrigger = true;

        if (_visualRoot != null)
            _initialVisualLocalScale = _visualRoot.localScale;
    }

    private void OnValidate()
    {
        if (_damageTrigger == null)
            _damageTrigger = GetComponent<SphereCollider>();

        ApplySetup();
    }

    public void InitializeFromSpell(
        EnemySpellDefinition spell,
        ulong sourceClientId,
        Vector3 castDirection
    )
    {
        if (spell != null)
        {
            _radius = Mathf.Max(0.25f, spell.ZoneRadius);
            _lifetime = Mathf.Max(0.1f, spell.ZoneLifetime);
            _damagePerTick = Mathf.Max(0, spell.ZoneDamagePerTick);
            _tickInterval = Mathf.Max(0.05f, spell.ZoneTickInterval);
            _statusEffectOnTick = spell.ZoneStatusEffect;
            _statusApplyChance = Mathf.Clamp01(spell.ZoneStatusApplyChance);
        }

        if (castDirection.sqrMagnitude < 0.001f)
            castDirection = transform.forward;

        _moveDirection = castDirection.normalized;

        InitializeSpellBase(sourceClientId);

        _initialized = true;

        ApplySetup();
        SnapToGroundIfNeeded();

        if (IsServer)
            StartCoroutine(ServerDespawnAfter(_lifetime));
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ApplySetup();
        SnapToGroundIfNeeded();

        if (IsServer && !_initialized)
        {
            _moveDirection = transform.forward;
            StartCoroutine(ServerDespawnAfter(_lifetime));
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer)
            return;

        if (_moveForward)
            MoveTornadoServer();
        else if (_followGround)
            SnapToGroundIfNeeded();

        ProcessActivePlayersServer();
    }

    private void MoveTornadoServer()
    {
        Vector3 currentPosition = _rb != null ? _rb.position : transform.position;
        Vector3 desiredPosition = currentPosition + _moveDirection * (_moveSpeed * Time.fixedDeltaTime);
        desiredPosition = GetGroundedPosition(desiredPosition, immediate: false);

        if (_rb != null)
            _rb.MovePosition(desiredPosition);
        else
            transform.position = desiredPosition;
    }

    private void SnapToGroundIfNeeded()
    {
        if (!_followGround || !_snapToGroundOnSpawn)
            return;

        Vector3 currentPosition = _rb != null ? _rb.position : transform.position;
        Vector3 groundedPosition = GetGroundedPosition(currentPosition, immediate: true);

        if (_rb != null)
            _rb.position = groundedPosition;

        transform.position = groundedPosition;
    }

    private Vector3 GetGroundedPosition(Vector3 desiredPosition, bool immediate)
    {
        if (!_followGround)
            return desiredPosition;

        if (!TryGetGroundY(desiredPosition, out float groundY))
            return desiredPosition;

        float targetY = groundY + _groundOffset;

        if (immediate)
        {
            desiredPosition.y = targetY;
            return desiredPosition;
        }

        float maxSpeed = targetY > desiredPosition.y ? _groundFollowMaxUpSpeed : _groundFollowMaxDownSpeed;
        desiredPosition.y = Mathf.MoveTowards(desiredPosition.y, targetY, maxSpeed * Time.fixedDeltaTime);

        return desiredPosition;
    }

    private bool TryGetGroundY(Vector3 worldPosition, out float groundY)
    {
        groundY = worldPosition.y;

        float startHeight = Mathf.Max(0.1f, _groundRaycastStartHeight);
        float rayDistance = startHeight + Mathf.Max(0.1f, _groundRaycastDistance);
        Vector3 origin = new Vector3(worldPosition.x, worldPosition.y + startHeight, worldPosition.z);

        int hitCount = Physics.RaycastNonAlloc(
            origin,
            Vector3.down,
            _groundHits,
            rayDistance,
            _groundMask,
            QueryTriggerInteraction.Ignore
        );

        if (hitCount <= 0)
            return false;

        bool foundGround = false;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _groundHits[i];

            if (hit.collider == null)
                continue;

            Transform hitTransform = hit.collider.transform;

            if (hitTransform == transform || hitTransform.IsChildOf(transform))
                continue;

            // Safety: if the ground mask accidentally includes the player, do not let the tornado ride on top of the player.
            if (hit.collider.GetComponentInParent<PlayerAttributes>() != null)
                continue;

            if (hit.distance >= bestDistance)
                continue;

            bestDistance = hit.distance;
            groundY = hit.point.y;
            foundGround = true;
        }

        return foundGround;
    }

    private void ApplySetup()
    {
        _radius = Mathf.Max(0.25f, _radius);
        _damageRadiusMultiplier = Mathf.Clamp(_damageRadiusMultiplier, 0.05f, 1f);
        _pullRadiusMultiplier = Mathf.Clamp(_pullRadiusMultiplier, 0.05f, 10f);
        _tickInterval = Mathf.Max(0.05f, _tickInterval);
        _minimumDirectDamageInterval = Mathf.Max(0.05f, _minimumDirectDamageInterval);
        _statusReapplyCooldown = Mathf.Max(0f, _statusReapplyCooldown);
        _forceApplyInterval = Mathf.Max(0.05f, _forceApplyInterval);
        _externalVelocityDuration = Mathf.Max(0.02f, _externalVelocityDuration);
        _visualScaleMultiplier = Mathf.Max(0.01f, _visualScaleMultiplier);
        _groundOffset = Mathf.Max(-2f, _groundOffset);
        _groundRaycastStartHeight = Mathf.Max(0.1f, _groundRaycastStartHeight);
        _groundRaycastDistance = Mathf.Max(0.1f, _groundRaycastDistance);
        _groundFollowMaxUpSpeed = Mathf.Max(0.1f, _groundFollowMaxUpSpeed);
        _groundFollowMaxDownSpeed = Mathf.Max(0.1f, _groundFollowMaxDownSpeed);
        _minimumExternalVelocityToApply = Mathf.Max(0f, _minimumExternalVelocityToApply);

        if (_damageTrigger != null)
        {
            _damageTrigger.isTrigger = true;

            float colliderScale = _compensateColliderForTransformScale ? GetLargestWorldScale() : 1f;
            float centerScaleY = _compensateColliderForTransformScale ? Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y)) : 1f;

            _damageTrigger.radius = EffectiveTriggerRadius / colliderScale;
            _damageTrigger.center = Vector3.up * (_triggerCenterHeight / centerScaleY);
        }

        ApplyVisualScale();
    }

    private float GetLargestWorldScale()
    {
        Vector3 scale = transform.lossyScale;
        return Mathf.Max(0.0001f, Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
    }

    private void ApplyVisualScale()
    {
        if (_visualRoot == null)
            return;

        if (!_scaleVisualWithRadius)
        {
            _visualRoot.localScale = _initialVisualLocalScale * _visualScaleMultiplier;
            return;
        }

        float authoredRadius = Mathf.Max(0.01f, _visualAuthoredRadius);
        float radiusScale = _radius / authoredRadius;
        _visualRoot.localScale = _initialVisualLocalScale * radiusScale * _visualScaleMultiplier;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer)
            return;

        PlayerAttributes player = FindPlayer(other);

        if (player == null)
            return;

        RegisterPlayer(player, enteredFromTriggerEnter: true);

        if (_damageImmediatelyOnEnter)
        {
            int targetKey = player.GetInstanceID();
            TryDamagePlayer(player, targetKey);
        }

        if (!_delayForceUntilNextInterval)
        {
            int targetKey = player.GetInstanceID();
            TryApplyLiftAndPull(player, targetKey, forceNow: true);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!IsServer)
            return;

        PlayerAttributes player = FindPlayer(other);

        if (player == null)
            return;

        // Do not apply damage or force from OnTriggerStay.
        // Trigger callbacks can fire many times for multi-collider players, which causes noisy feedback and hard-to-debug stickiness.
        RegisterPlayer(player, enteredFromTriggerEnter: false);
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer)
            return;

        PlayerAttributes player = FindPlayer(other);

        if (player == null)
            return;

        int targetKey = player.GetInstanceID();

        if (!_overlapCounts.TryGetValue(targetKey, out int currentOverlapCount))
            return;

        currentOverlapCount--;

        if (currentOverlapCount > 0)
        {
            _overlapCounts[targetKey] = currentOverlapCount;
            return;
        }

        RemoveActivePlayer(targetKey, keepDamageCooldown: true);
    }

    private void RegisterPlayer(PlayerAttributes player, bool enteredFromTriggerEnter)
    {
        int targetKey = player.GetInstanceID();

        if (enteredFromTriggerEnter)
        {
            _overlapCounts.TryGetValue(targetKey, out int currentOverlapCount);
            _overlapCounts[targetKey] = currentOverlapCount + 1;
        }
        else if (!_overlapCounts.ContainsKey(targetKey))
        {
            // Fallback in case the object was already inside the trigger when the tornado spawned.
            _overlapCounts[targetKey] = 1;
        }

        if (_activePlayers.ContainsKey(targetKey))
        {
            _activePlayers[targetKey] = player;
            return;
        }

        _activePlayers[targetKey] = player;
        _enteredTimes[targetKey] = Time.time;
        ScheduleFirstDamage(targetKey);

        if (_delayForceUntilNextInterval)
            _nextForceTimes[targetKey] = Time.time + _forceApplyInterval;
        else
            _nextForceTimes[targetKey] = Time.time;
    }

    private void RemoveActivePlayer(int targetKey, bool keepDamageCooldown)
    {
        _activePlayers.Remove(targetKey);
        _overlapCounts.Remove(targetKey);
        _enteredTimes.Remove(targetKey);
        _nextForceTimes.Remove(targetKey);

        if (!keepDamageCooldown)
        {
            _nextDamageTimes.Remove(targetKey);
            _nextStatusApplyTimes.Remove(targetKey);
        }

        // Keep _nextDamageTimes by default. If the player flickers on the edge, this prevents repeated immediate damage/feedback.
    }

    private void ProcessActivePlayersServer()
    {
        if (_activePlayers.Count == 0)
            return;

        _targetsToRemove.Clear();

        foreach (KeyValuePair<int, PlayerAttributes> pair in _activePlayers)
        {
            int targetKey = pair.Key;
            PlayerAttributes player = pair.Value;

            if (player == null || !player.gameObject.activeInHierarchy)
            {
                _targetsToRemove.Add(targetKey);
                continue;
            }

            TryDamagePlayer(player, targetKey);
            TryApplyLiftAndPull(player, targetKey, forceNow: false);
        }

        for (int i = 0; i < _targetsToRemove.Count; i++)
            RemoveActivePlayer(_targetsToRemove[i], keepDamageCooldown: false);
    }

    private void ScheduleFirstDamage(int targetKey)
    {
        if (_damageImmediatelyOnEnter)
            return;

        float delay = _firstDamageDelay >= 0f ? _firstDamageDelay : _tickInterval;
        delay = Mathf.Max(delay, _enterGraceDuration);
        _nextDamageTimes[targetKey] = Time.time + Mathf.Max(0f, delay);
    }

    private void TryDamagePlayer(PlayerAttributes player, int targetKey)
    {
        if (!IsInsideHorizontalRadius(player.transform.position, EffectiveDamageRadius))
            return;

        if (_nextDamageTimes.TryGetValue(targetKey, out float nextAllowedTime) && Time.time < nextAllowedTime)
            return;

        float nextInterval = Mathf.Max(_tickInterval, _minimumDirectDamageInterval);
        _nextDamageTimes[targetKey] = Time.time + nextInterval;

        if (_damagePerTick > 0)
            DamagePlayerFromServer(player, _damagePerTick);

        TryApplyStatusEffect(player, targetKey);

        if (_debugTornado)
        {
            Debug.Log(
                $"[EnemyTornadoHazard Damage] TargetKey: {targetKey}, Damage: {_damagePerTick}, NextTickIn: {nextInterval:F2}, DamageRadius: {EffectiveDamageRadius:F2}"
            );
        }
    }

    private void TryApplyStatusEffect(PlayerAttributes player, int targetKey)
    {
        if (_statusEffectOnTick == null)
            return;

        if (Random.value > _statusApplyChance)
            return;

        if (_nextStatusApplyTimes.TryGetValue(targetKey, out float nextAllowedTime) && Time.time < nextAllowedTime)
            return;

        _nextStatusApplyTimes[targetKey] = Time.time + _statusReapplyCooldown;
        ApplyStatusFromServer(player, _statusEffectOnTick);
    }

    private void TryApplyLiftAndPull(PlayerAttributes player, int targetKey, bool forceNow)
    {
        if (!_applyLift)
            return;

        if (!IsInsideHorizontalRadius(player.transform.position, EffectivePullRadius))
            return;

        if (!forceNow && _nextForceTimes.TryGetValue(targetKey, out float nextAllowedTime) && Time.time < nextAllowedTime)
            return;

        _nextForceTimes[targetKey] = Time.time + _forceApplyInterval;

        PlayerController controller = player.GetComponentInParent<PlayerController>();

        if (controller == null)
            return;

        Vector3 toCenter = transform.position - player.transform.position;
        toCenter.y = 0f;

        float distance = toCenter.magnitude;

        if (distance <= 0.001f)
            return;

        Vector3 inwardDirection = toCenter / distance;
        Vector3 swirlDirection = Vector3.Cross(Vector3.up, inwardDirection).normalized;

        float edgeToCenter01 = 1f - Mathf.Clamp01(distance / EffectivePullRadius);
        float innerEyeRadius = EffectivePullRadius * _innerEyeRadiusRatio;

        float pull01;
        if (distance <= innerEyeRadius)
        {
            pull01 = 0f;
        }
        else
        {
            float distanceBetweenEdgeAndEye01 = Mathf.InverseLerp(EffectivePullRadius, innerEyeRadius, distance);
            pull01 = Mathf.SmoothStep(_edgePullMultiplier, 1f, distanceBetweenEdgeAndEye01);
        }

        float grace01 = GetEnterGraceMultiplier(targetKey);
        pull01 *= grace01;

        float lift01 = Mathf.SmoothStep(0.1f, 1f, edgeToCenter01) * grace01;
        float swirl01 = Mathf.SmoothStep(0.25f, 1f, edgeToCenter01) * grace01;

        Vector3 pullVelocity = inwardDirection * (_pullStrength * pull01);
        Vector3 swirlVelocity = swirlDirection * (_swirlStrength * swirl01);
        Vector3 liftVelocity = Vector3.up * (_liftSpeed * lift01);

        Vector3 finalVelocity = pullVelocity + swirlVelocity + liftVelocity;

        if (finalVelocity.sqrMagnitude < _minimumExternalVelocityToApply * _minimumExternalVelocityToApply)
            return;

        if (_debugTornado)
        {
            Debug.Log(
                $"[EnemyTornadoHazard Force] TargetKey: {targetKey}, Distance: {distance:F2}, PullRadius: {EffectivePullRadius:F2}, Pull01: {pull01:F2}, Lift01: {lift01:F2}, Velocity: {finalVelocity}"
            );
        }

        controller.SetExternalVelocityFromServer(finalVelocity, _externalVelocityDuration);
    }

    private bool IsInsideHorizontalRadius(Vector3 worldPosition, float radius)
    {
        Vector3 delta = worldPosition - transform.position;
        delta.y = 0f;
        return delta.sqrMagnitude <= radius * radius;
    }

    private float GetEnterGraceMultiplier(int targetKey)
    {
        if (_enterGraceDuration <= 0f)
            return 1f;

        if (!_enteredTimes.TryGetValue(targetKey, out float enteredAt))
            return 1f;

        float timeInside = Time.time - enteredAt;
        return Mathf.Clamp01(timeInside / _enterGraceDuration);
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = transform.position;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, EffectiveDamageRadius);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(center, EffectivePullRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(center, EffectiveTriggerRadius);
    }
}
