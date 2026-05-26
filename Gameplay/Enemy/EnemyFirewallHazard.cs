using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class EnemyFirewallHazard : EnemySpellBehaviour, IEnemySpellZone
{
    [Header("References")]
    [SerializeField] private BoxCollider _damageTrigger;
    [SerializeField] private Transform _visualRoot;
    [SerializeField] private GameObject[] _effectObjects;

    [Header("Firewall Shape")]
    [SerializeField] private float _width = 2f;
    [SerializeField] private float _length = 10f;
    [SerializeField] private float _height = 2.5f;

    [Header("Fallback")]
    [SerializeField] private float _lifetime = 5f;
    [SerializeField] private int _damagePerTick = 8;
    [SerializeField] private float _tickInterval = 0.4f;
    [SerializeField] private StatusEffectData _statusEffectOnTick;
    [SerializeField, Range(0f, 1f)] private float _statusApplyChance = 1f;

    private bool _initialized;
    private readonly Dictionary<ulong, float> _nextDamageTimes = new Dictionary<ulong, float>();

    private void Awake()
    {
        Rigidbody rb = GetComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        if (_damageTrigger == null)
            _damageTrigger = GetComponent<BoxCollider>();

        _damageTrigger.isTrigger = true;
    }

    public void InitializeFromSpell(
        EnemySpellDefinition spell,
        ulong sourceClientId,
        Vector3 castDirection
    )
    {
        if (spell != null)
        {
            _lifetime = Mathf.Max(0.1f, spell.ZoneLifetime);
            _damagePerTick = Mathf.Max(0, spell.ZoneDamagePerTick);
            _tickInterval = Mathf.Max(0.05f, spell.ZoneTickInterval);
            _statusEffectOnTick = spell.ZoneStatusEffect;
            _statusApplyChance = Mathf.Clamp01(spell.ZoneStatusApplyChance);
        }

        InitializeSpellBase(sourceClientId);

        _initialized = true;

        ApplySetup();

        if (IsServer)
            StartCoroutine(ServerDespawnAfter(_lifetime));
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        ApplySetup();

        if (IsServer && !_initialized)
            StartCoroutine(ServerDespawnAfter(_lifetime));
    }

    private void ApplySetup()
    {
        if (_damageTrigger != null)
        {
            _damageTrigger.isTrigger = true;
            _damageTrigger.size = new Vector3(_width, _height, _length);
            _damageTrigger.center = new Vector3(0f, _height * 0.5f, 0f);
        }

        if (_visualRoot != null)
        {
            _visualRoot.localScale = new Vector3(_width, 1f, _length);
        }

        if (_effectObjects != null)
        {
            foreach (GameObject effectObject in _effectObjects)
            {
                if (effectObject != null)
                    effectObject.SetActive(true);
            }
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
        if (!IsServer)
            return;

        PlayerAttributes player = FindPlayer(other);

        if (player == null)
            return;

        NetworkObject targetNetObj = player.GetComponentInParent<NetworkObject>();

        if (targetNetObj == null)
            return;

        ulong targetId = targetNetObj.NetworkObjectId;

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