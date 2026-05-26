using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
public class StatusEffectReceiver : NetworkBehaviour
{
    // Server-side active effects.
    private readonly List<ActiveEffect> _activeEffects = new List<ActiveEffect>();

    // Client-side VFX instances.
    private readonly Dictionary<string, GameObject> _clientVFXInstances = new Dictionary<string, GameObject>();

    // Prevents stackable effects from gaining stacks every frame when ApplyStatusEffect is called from OnTriggerStay/continuous hazards.
    private readonly Dictionary<string, float> _lastStackIncreaseTimeByEffect = new Dictionary<string, float>();

    private PlayerAttributes _playerAttributes;
    private EnemyHealth _enemyHealth;

    [Header("Debug")]
    [SerializeField] private bool _debugStatusEffects = false;
    [SerializeField] private float _debugSummaryInterval = 1f;

    [Header("Stacking Protection")]
    [Tooltip("Minimal cooldown between stack increases for the same effect on the same receiver. Prevents OnTriggerStay effects from stacking every frame.")]
    [SerializeField] private float _minStackIncreaseCooldown = 0.35f;

    private float _debugLogTimer;

    public float CurrentSpeedMultiplier { get; private set; } = 1.0f;
    public bool IsStunned { get; private set; } = false;

    private void Awake()
    {
        ResolveHealthComponents();
    }

    private void ResolveHealthComponents()
    {
        _playerAttributes = GetComponent<PlayerAttributes>();
        if (_playerAttributes == null)
            _playerAttributes = GetComponentInParent<PlayerAttributes>();
        if (_playerAttributes == null)
            _playerAttributes = GetComponentInChildren<PlayerAttributes>();

        _enemyHealth = GetComponent<EnemyHealth>();
        if (_enemyHealth == null)
            _enemyHealth = GetComponentInParent<EnemyHealth>();
        if (_enemyHealth == null)
            _enemyHealth = GetComponentInChildren<EnemyHealth>();
    }

    public override void OnNetworkDespawn()
    {
        ClearClientVFX();
        _activeEffects.Clear();
        _lastStackIncreaseTimeByEffect.Clear();
        CurrentSpeedMultiplier = 1.0f;
        IsStunned = false;
    }

    private void OnDisable()
    {
        // In multiplayer despawn should be the main cleanup path, but this protects disabled scene objects/pooling too.
        ClearClientVFX();
    }

    private void ClearClientVFX()
    {
        foreach (var kvp in _clientVFXInstances)
        {
            if (kvp.Value != null)
                Destroy(kvp.Value);
        }

        _clientVFXInstances.Clear();
    }

    private void Update()
    {
        if (!IsServer)
            return;

        if (_debugStatusEffects && _activeEffects.Count > 0)
        {
            _debugLogTimer += Time.deltaTime;

            if (_debugLogTimer >= _debugSummaryInterval)
            {
                _debugLogTimer = 0f;

                Debug.Log(
                    $"[StatusEffectReceiver] Processing effects on '{gameObject.name}'. " +
                    $"ActiveEffects: {_activeEffects.Count}, IsServer: {IsServer}, " +
                    $"enabled: {enabled}, activeInHierarchy: {gameObject.activeInHierarchy}"
                );
            }
        }

        ProcessEffects(Time.deltaTime);
    }

    private void ProcessEffects(float delta)
    {
        float newSpeedMult = 1.0f;
        bool isStunnedThisFrame = false;

        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            ActiveEffect effect = _activeEffects[i];

            if (effect == null || effect.Data == null)
            {
                _activeEffects.RemoveAt(i);
                continue;
            }

            effect.Timer -= delta;

            if (effect.Timer <= 0f)
            {
                string key = GetEffectKey(effect.Data);
                RemoveEffectClientRpc(effect.Data.EffectName);
                _lastStackIncreaseTimeByEffect.Remove(key);
                _activeEffects.RemoveAt(i);
                continue;
            }

            ProcessEffectTick(effect, delta);

            if (effect.Data.SpeedMultiplier != 1.0f)
            {
                newSpeedMult *= effect.Data.SpeedMultiplier;
            }

            if (effect.Data.IsStun)
            {
                isStunnedThisFrame = true;
            }
        }

        CurrentSpeedMultiplier = newSpeedMult;
        IsStunned = isStunnedThisFrame;
    }

    private void ProcessEffectTick(ActiveEffect effect, float delta)
    {
        if (effect == null || effect.Data == null)
            return;

        if (effect.Data.TickInterval <= 0f)
            return;

        bool hasTickEffect =
            effect.Data.DamagePerTick > 0f ||
            effect.Data.HealPerTick > 0f;

        if (!hasTickEffect)
            return;

        effect.TickTimer += delta;

        while (effect.TickTimer >= effect.Data.TickInterval)
        {
            if (_debugStatusEffects)
            {
                Debug.Log(
                    $"[StatusEffect Tick] {effect.Data.EffectName} | " +
                    $"Target: {gameObject.name}, Timer: {effect.Timer:F2}, " +
                    $"TickInterval: {effect.Data.TickInterval:F2}, " +
                    $"DamagePerTick: {effect.Data.DamagePerTick}, " +
                    $"Stacks: {effect.Stacks}, IsDamagePercentage: {effect.Data.IsDamagePercentage}"
                );
            }

            if (effect.Data.DamagePerTick > 0f)
            {
                ApplyTickDamage(effect);
            }

            if (effect.Data.HealPerTick > 0f)
            {
                ApplyTickHeal(effect);
            }

            effect.TickTimer -= effect.Data.TickInterval;
        }
    }

    public void ApplyStatusEffect(StatusEffectData data)
    {
        if (data == null)
        {
            if (_debugStatusEffects)
                Debug.LogWarning($"[StatusEffect Apply] Tried to apply null effect to '{gameObject.name}'.");

            return;
        }

        if (_debugStatusEffects)
        {
            Debug.Log(
                $"[StatusEffect Apply State] Object: '{gameObject.name}', " +
                $"Effect: '{data.EffectName}', " +
                $"IsServer: {IsServer}, IsSpawned: {IsSpawned}, " +
                $"enabled: {enabled}, isActiveAndEnabled: {isActiveAndEnabled}, " +
                $"activeInHierarchy: {gameObject.activeInHierarchy}, " +
                $"ReceiverCountOnObject: {GetComponents<StatusEffectReceiver>().Length}, " +
                $"ReceiverCountInChildren: {GetComponentsInChildren<StatusEffectReceiver>(true).Length}"
            );
        }

        if (!IsServer)
            return;

        ActiveEffect existing = _activeEffects.Find(e =>
            e != null &&
            e.Data != null &&
            (
                e.Data == data ||
                (!string.IsNullOrEmpty(data.EffectID) && e.Data.EffectID == data.EffectID) ||
                e.Data.EffectName == data.EffectName
            )
        );

        string effectKey = GetEffectKey(data);

        if (existing != null)
        {
            // Reapplying an active effect should refresh duration.
            // This is important for fire pools / hazards / projectiles that touch the same target again.
            existing.Timer = data.Duration;

            if (data.IsStackable)
            {
                int maxStacks = Mathf.Max(1, data.MaxStacks);

                if (existing.Stacks < maxStacks && CanIncreaseStack(data, effectKey))
                {
                    existing.Stacks = Mathf.Min(existing.Stacks + 1, maxStacks);
                    _lastStackIncreaseTimeByEffect[effectKey] = Time.time;

                    if (_debugStatusEffects)
                    {
                        Debug.Log(
                            $"[StatusEffect Stack] {data.EffectName} on '{gameObject.name}' increased to {existing.Stacks}/{maxStacks}."
                        );
                    }
                }
            }

            return;
        }

        ActiveEffect newEffect = new ActiveEffect(data);
        _activeEffects.Add(newEffect);

        // Important: mark stack time now so OnTriggerStay/continuous hazards do not add another stack on the very next frame.
        _lastStackIncreaseTimeByEffect[effectKey] = Time.time;

        AddEffectClientRpc(data.EffectName);
    }

    private bool CanIncreaseStack(StatusEffectData data, string effectKey)
    {
        float cooldown = Mathf.Max(_minStackIncreaseCooldown, data.TickInterval);

        if (!_lastStackIncreaseTimeByEffect.TryGetValue(effectKey, out float lastStackTime))
            return true;

        return Time.time - lastStackTime >= cooldown;
    }

    private void ApplyTickDamage(ActiveEffect effect)
    {
        if (effect == null || effect.Data == null)
            return;

        if (_playerAttributes == null && _enemyHealth == null)
        {
            ResolveHealthComponents();
        }

        int stacks = Mathf.Max(1, effect.Stacks);
        float damage = effect.Data.DamagePerTick * stacks;

        if (effect.Data.IsDamagePercentage && _playerAttributes != null)
        {
            // Supports both inspector styles:
            // 2    = 2% max HP
            // 0.02 = 2% max HP
            float percentRatio = NormalizePercentageValue(effect.Data.DamagePerTick);
            damage = _playerAttributes.MaxHealth.Value * percentRatio * stacks;
        }

        int finalDamage = Mathf.CeilToInt(damage);

        if (finalDamage <= 0)
            return;

        if (_debugStatusEffects)
        {
            /**Debug.Log(
                $"[StatusEffect Damage] Target: {gameObject.name}, Effect: {effect.Data.EffectName}, " +
                $"Damage: {finalDamage}, Stacks: {stacks}, " +
                $"PlayerAttributes: {_playerAttributes != null}, EnemyHealth: {_enemyHealth != null}, " +
                $"IsDamagePercentage: {effect.Data.IsDamagePercentage}"
            );*/
        }

        if (_playerAttributes != null)
        {
            _playerAttributes.TakeDamageFromServer(finalDamage);
            return;
        }

        if (_enemyHealth != null)
        {
            _enemyHealth.TakeDamage(finalDamage);
            return;
        }

        Debug.LogWarning(
            $"StatusEffectReceiver on '{gameObject.name}' has no PlayerAttributes or EnemyHealth."
        );
    }

    private void ApplyTickHeal(ActiveEffect effect)
    {
        if (effect == null || effect.Data == null)
            return;

        if (_playerAttributes == null && _enemyHealth == null)
        {
            ResolveHealthComponents();
        }

        int stacks = Mathf.Max(1, effect.Stacks);
        float heal = effect.Data.HealPerTick * stacks;

        if (effect.Data.IsDamagePercentage && _playerAttributes != null)
        {
            // Kept compatible with the current StatusEffectData flag naming.
            // 2    = 2% max HP
            // 0.02 = 2% max HP
            float percentRatio = NormalizePercentageValue(effect.Data.HealPerTick);
            heal = _playerAttributes.MaxHealth.Value * percentRatio * stacks;
        }

        int finalHeal = Mathf.CeilToInt(heal);

        if (finalHeal <= 0)
            return;

        if (_playerAttributes != null)
        {
            _playerAttributes.Heal(finalHeal);
        }
        else if (_enemyHealth != null)
        {
            _enemyHealth.Heal(finalHeal);
        }
    }

    private static float NormalizePercentageValue(float value)
    {
        // Prevents the common inspector mistake where 2 is intended as 2%, not 200%.
        // Also remains compatible with decimal ratios such as 0.02.
        if (value > 1f)
            return value / 100f;

        return value;
    }

    private static string GetEffectKey(StatusEffectData data)
    {
        if (data == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(data.EffectID))
            return data.EffectID;

        return data.EffectName;
    }

    [ClientRpc]
    private void AddEffectClientRpc(string effectName)
    {
        StatusEffectData data = GameEffectDatabase.GetEffectByName(effectName);

        if (data == null || data.EffectVFXPrefab == null)
            return;

        if (_clientVFXInstances.ContainsKey(effectName))
            return;

        GameObject vfx = Instantiate(data.EffectVFXPrefab, transform.position, Quaternion.identity);

        Transform targetBone = transform;
        if (!string.IsNullOrEmpty(data.AttachBoneName))
        {
            Transform bone = FindDeepChild(transform, data.AttachBoneName);
            if (bone != null)
                targetBone = bone;
        }

        vfx.transform.SetParent(targetBone);
        vfx.transform.localPosition = Vector3.zero;
        vfx.transform.localRotation = Quaternion.identity;

        _clientVFXInstances.Add(effectName, vfx);
    }

    [ClientRpc]
    private void RemoveEffectClientRpc(string effectName)
    {
        if (!_clientVFXInstances.TryGetValue(effectName, out GameObject vfx))
            return;

        if (vfx != null)
        {
            ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
            if (ps != null)
            {
                ps.Stop();
                Destroy(vfx, 2.0f);
            }
            else
            {
                Destroy(vfx);
            }
        }

        _clientVFXInstances.Remove(effectName);
    }

    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name)
                return child;

            Transform result = FindDeepChild(child, name);
            if (result != null)
                return result;
        }

        return null;
    }
}
