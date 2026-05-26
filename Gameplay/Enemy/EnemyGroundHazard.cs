using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class EnemyGroundHazard : EnemySpellBehaviour, IEnemySpellZone
{
    [Header("References")]
    [SerializeField] private Collider _damageTrigger;
    [SerializeField] private Transform _visualRoot;
    [SerializeField] private GameObject[] _effectObjects;
    [SerializeField] private Light _hazardLight;

    [Header("Fallback Defaults")]
    [SerializeField] private float _defaultRadius = 2.5f;
    [SerializeField] private float _defaultLifetime = 4f;
    [SerializeField] private int _defaultDamagePerTick = 5;
    [SerializeField] private float _defaultTickInterval = 0.5f;

    [Header("Default Status Effect")]
    [SerializeField] private StatusEffectData _defaultStatusEffectOnTick;
    [SerializeField, Range(0f, 1f)] private float _defaultStatusApplyChance = 1f;

    [Header("Collider")]
    [SerializeField] private float _triggerHeight = 2.5f;

    [Header("Visual Scaling")]
    [SerializeField] private bool _scaleVisualRoot = true;

    private Rigidbody _rb;

    private float _radius;
    private float _lifetime;
    private int _damagePerTick;
    private float _tickInterval;
    private ulong _sourceClientId;
    private bool _initialized;

    private StatusEffectData _statusEffectOnTick;
    private float _statusApplyChance = 1f;

    private Vector3 _visualOriginalScale = Vector3.one;
    private bool _cachedOriginalVisualScale;

    private readonly Dictionary<ulong, float> _nextDamageTimes = new Dictionary<ulong, float>();

    private readonly NetworkVariable<bool> _netInitialized = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<float> _netRadius = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<float> _netLifetime = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<int> _netDamagePerTick = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<float> _netTickInterval = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<ulong> _netSourceClientId = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.isKinematic = true;
        _rb.useGravity = false;
        _rb.interpolation = RigidbodyInterpolation.None;
        _rb.collisionDetectionMode = CollisionDetectionMode.Discrete;

        if (_damageTrigger == null)
            _damageTrigger = GetComponentInChildren<Collider>(true);

        if (_damageTrigger != null)
            _damageTrigger.isTrigger = true;

        CacheOriginalVisualScale();
    }

    public void InitializeFromSpell(EnemySpellDefinition spell, ulong sourceClientId, Vector3 castDirection)
    {
        if (spell == null)
            return;

        Initialize(
            spell.ZoneDamagePerTick,
            spell.ZoneTickInterval,
            spell.ZoneLifetime,
            spell.ZoneRadius,
            sourceClientId,
            spell.ZoneStatusEffect,
            spell.ZoneStatusApplyChance
        );
    }

    public void Initialize(
        int damagePerTick,
        float tickInterval,
        float lifetime,
        float radius,
        ulong sourceClientId,
        StatusEffectData statusEffectOnTick,
        float statusApplyChance = 1f)
    {
        _damagePerTick = Mathf.Max(0, damagePerTick);
        _tickInterval = Mathf.Max(0.05f, tickInterval);
        _lifetime = Mathf.Max(0.1f, lifetime);
        _radius = Mathf.Max(0.25f, radius);
        _sourceClientId = sourceClientId;

        _statusEffectOnTick = statusEffectOnTick != null
            ? statusEffectOnTick
            : _defaultStatusEffectOnTick;

        _statusApplyChance = statusApplyChance < 0f
            ? Mathf.Clamp01(_defaultStatusApplyChance)
            : Mathf.Clamp01(statusApplyChance);

        _initialized = true;

        InitializeSpellBase(_sourceClientId);

        if (IsServer)
        {
            _netDamagePerTick.Value = _damagePerTick;
            _netTickInterval.Value = _tickInterval;
            _netLifetime.Value = _lifetime;
            _netRadius.Value = _radius;
            _netSourceClientId.Value = _sourceClientId;
            _netInitialized.Value = true;
        }

        ApplySetup();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (!_initialized)
        {
            if (_netInitialized.Value)
            {
                LoadFromNetworkValues();
            }
            else
            {
                LoadDefaults();
            }

            ApplySetup();
        }

        if (IsServer)
            StartCoroutine(ServerDespawnAfter(_lifetime));
    }

    private void LoadFromNetworkValues()
    {
        _damagePerTick = Mathf.Max(0, _netDamagePerTick.Value);
        _tickInterval = Mathf.Max(0.05f, _netTickInterval.Value);
        _lifetime = Mathf.Max(0.1f, _netLifetime.Value);
        _radius = Mathf.Max(0.25f, _netRadius.Value);
        _sourceClientId = _netSourceClientId.Value;

        _statusEffectOnTick = _defaultStatusEffectOnTick;
        _statusApplyChance = Mathf.Clamp01(_defaultStatusApplyChance);

        _initialized = true;

        InitializeSpellBase(_sourceClientId);
    }

    private void LoadDefaults()
    {
        _damagePerTick = _defaultDamagePerTick;
        _tickInterval = Mathf.Max(0.05f, _defaultTickInterval);
        _lifetime = Mathf.Max(0.1f, _defaultLifetime);
        _radius = Mathf.Max(0.25f, _defaultRadius);
        _sourceClientId = ulong.MaxValue;

        _statusEffectOnTick = _defaultStatusEffectOnTick;
        _statusApplyChance = Mathf.Clamp01(_defaultStatusApplyChance);

        _initialized = true;

        InitializeSpellBase(_sourceClientId);
    }

    private void CacheOriginalVisualScale()
    {
        if (_visualRoot == null || _cachedOriginalVisualScale)
            return;

        _visualOriginalScale = _visualRoot.localScale;
        _cachedOriginalVisualScale = true;
    }

    private void ApplySetup()
    {
        CacheOriginalVisualScale();

        if (_damageTrigger != null)
        {
            _damageTrigger.isTrigger = true;

            if (_damageTrigger is SphereCollider sphere)
            {
                sphere.radius = _radius;
            }
            else if (_damageTrigger is CapsuleCollider capsule)
            {
                capsule.radius = _radius;
                capsule.height = Mathf.Max(_triggerHeight, capsule.radius * 2f);
            }
            else if (_damageTrigger is BoxCollider box)
            {
                box.size = new Vector3(_radius * 2f, _triggerHeight, _radius * 2f);
            }
        }

        if (_visualRoot != null && _scaleVisualRoot)
        {
            float diameter = _radius * 2f;

            _visualRoot.localScale = new Vector3(
                _visualOriginalScale.x * diameter,
                _visualOriginalScale.y,
                _visualOriginalScale.z * diameter
            );
        }

        if (_effectObjects != null)
        {
            for (int i = 0; i < _effectObjects.Length; i++)
            {
                if (_effectObjects[i] != null)
                    _effectObjects[i].SetActive(true);
            }
        }

        if (_hazardLight != null)
        {
            _hazardLight.enabled = true;
            _hazardLight.range = Mathf.Max(_hazardLight.range, _radius * 2f);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryApplyTick(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryApplyTick(other);
    }

    private void TryApplyTick(Collider other)
    {
        if (!IsServer || !_initialized)
            return;

        if (_damagePerTick <= 0 && _statusEffectOnTick == null)
            return;

        PlayerAttributes player = FindPlayer(other);

        if (player == null)
            return;

        NetworkObject playerNetObj = player.GetComponentInParent<NetworkObject>();

        if (playerNetObj == null)
            return;

        ulong targetId = playerNetObj.NetworkObjectId;

        if (_nextDamageTimes.TryGetValue(targetId, out float nextAllowedTime))
        {
            if (Time.time < nextAllowedTime)
                return;
        }

        _nextDamageTimes[targetId] = Time.time + _tickInterval;

        if (_damagePerTick > 0)
            DamagePlayerFromServer(player, _damagePerTick);

        if (_statusEffectOnTick != null && Random.value <= _statusApplyChance)
            ApplyStatusFromServer(player, _statusEffectOnTick);
    }
}