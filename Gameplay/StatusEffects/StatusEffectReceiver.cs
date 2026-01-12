using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class StatusEffectReceiver : NetworkBehaviour
{

    // Seznam aktivních efektů (Spravuje Server)
    private List<ActiveEffect> _activeEffects = new List<ActiveEffect>();

    // Cache pro vizuály na klientovi
    private Dictionary<string, GameObject> _clientVFXInstances = new Dictionary<string, GameObject>();

    // Reference na komponenty (pokusí se najít HP a Movement)
    private PlayerAttributes _playerAttributes;
    // private EnemyHealth _enemyHealth; // Tady doplníme referenci, až budeme dělat Enemies
    // private CharacterController _characterController; // Pro speed modifikaci

    public float CurrentSpeedMultiplier { get; private set; } = 1.0f;
    public bool IsStunned { get; private set; } = false;

    private void Awake()
    {
        _playerAttributes = GetComponent<PlayerAttributes>();
        // _enemyHealth = GetComponent<EnemyHealth>();
    }

    public override void OnNetworkDespawn()
    {
        // Úklid vizuálů při smrti/despawnu
        foreach (var kvp in _clientVFXInstances)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
        }
        _clientVFXInstances.Clear();
    }

    private void Update()
    {
        if (!IsServer) return;

        ProcessEffects(Time.deltaTime);
    }

    private void ProcessEffects(float delta)
    {
        bool statsChanged = false;
        float newSpeedMult = 1.0f;
        bool isStunnedThisFrame = false;

        // Procházíme pozpátku, abychom mohli bezpečně mazat
        for (int i = _activeEffects.Count - 1; i >= 0; i--)
        {
            var effect = _activeEffects[i];
            effect.Timer -= delta;

            // 1. Tick Damage Logic
            if (effect.Data.TickInterval > 0)
            {
                effect.TickTimer += delta;
                if (effect.TickTimer >= effect.Data.TickInterval)
                {
                    ApplyTickDamage(effect);
                    effect.TickTimer = 0f;
                }
            }

            // 2. Kalkulace Speed Multiplieru (všechny efekty se násobí)
            if (effect.Data.SpeedMultiplier != 1.0f)
            {
                newSpeedMult *= effect.Data.SpeedMultiplier;
                statsChanged = true;
            }

            // 3. Expirace
            if (effect.Timer <= 0)
            {
                RemoveEffectClientRpc(effect.Data.EffectName); // Smaže VFX na klientech
                _activeEffects.RemoveAt(i);
                statsChanged = true;
            }

            // KONTROLA STUNU
            // Pokud tento konkrétní efekt má zaškrtnuto "IsStun", tak jsme omráčení.
            if (effect.Data.IsStun)
            {
                isStunnedThisFrame = true;
            }

            IsStunned = isStunnedThisFrame;
        }

        // Aktualizace cache statů
        if (Mathf.Abs(CurrentSpeedMultiplier - newSpeedMult) > 0.01f)
        {
            CurrentSpeedMultiplier = newSpeedMult;
            // Tady bychom mohli poslat NetworkVariable, pokud potřebujeme přesnou synchronizaci rychlosti pro animace
        }
    }

    // --- Public API (Volá se ze zbraní, pastí, atd.) ---

    public void ApplyStatusEffect(StatusEffectData data)
    {
        if (!IsServer) return; // Statusy aplikuje jen server

        var existing = _activeEffects.Find(e => e.Data.EffectName == data.EffectName);

        if (existing != null)
        {
            // Efekt už existuje -> Refresh nebo Stack
            existing.Timer = data.Duration; // Reset času
            if (data.IsStackable)
            {
                existing.Stacks++;
            }
        }
        else
        {
            // Nový efekt
            ActiveEffect newEffect = new ActiveEffect(data);
            _activeEffects.Add(newEffect);

            // Informujeme klienty, ať si zapnou VFX
            // (Posíláme ID nebo Name - Name je jednodušší pro prototyp, ID je lepší pro bandwidth)
            AddEffectClientRpc(data.EffectName);
        }
    }

    private void ApplyTickDamage(ActiveEffect effect)
    {
        float damage = effect.Data.DamagePerTick;

        // Pokud se poškození násobí stacky (volitelné, pro teď jednoduché)
        // damage *= effect.Stacks; 

        if (damage > 0)
        {
            // Aplikace do PlayerAttributes
            if (_playerAttributes != null)
            {
                _playerAttributes.TakeDamageServerRpc((int)damage);
            }
            // Zde později doplníme EnemyHealth.TakeDamage(damage)
        }
    }

    // --- Visuals Sync (Client RPCs) ---

    [ClientRpc]
    private void AddEffectClientRpc(string effectName)
    {
        // Najdeme data efektu (V reálu by to měl být Resource lookup nebo ID list)
        // Pro prototyp to načteme z Resources, nebo musíme mít referenci.
        // ABY TO BYLO ROBUSTNÍ: Uděláme si jednoduchý Singleton "GameEffectManager", který drží seznam všech Data objektů.
        // Pro teď zkusíme najít efekt v projektu (neefektivní) nebo předpokládejme, že ho máme.

        // *OPTIMALIZACE*: Místo stringu posílat int ID. 
        // Pro zjednodušení v tomto kódu předpokládám, že máme statickou metodu na nalezení dat:
        StatusEffectData data = GameEffectDatabase.GetEffectByName(effectName);

        if (data == null || data.EffectVFXPrefab == null) return;
        if (_clientVFXInstances.ContainsKey(effectName)) return; // Už běží

        // Instantiace VFX
        GameObject vfx = Instantiate(data.EffectVFXPrefab, transform.position, Quaternion.identity);

        // Přichycení (Parenting)
        Transform targetBone = transform;
        if (!string.IsNullOrEmpty(data.AttachBoneName))
        {
            // Zkusíme najít kost rekurzivně
            Transform bone = FindDeepChild(transform, data.AttachBoneName);
            if (bone != null) targetBone = bone;
        }

        vfx.transform.SetParent(targetBone);
        vfx.transform.localPosition = Vector3.zero;
        vfx.transform.localRotation = Quaternion.identity;

        _clientVFXInstances.Add(effectName, vfx);
    }

    [ClientRpc]
    private void RemoveEffectClientRpc(string effectName)
    {
        if (_clientVFXInstances.TryGetValue(effectName, out GameObject vfx))
        {
            if (vfx != null)
            {
                // Pokud má particle system stop action, necháme ho dojet, jinak destroy
                ParticleSystem ps = vfx.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    ps.Stop();
                    Destroy(vfx, 2.0f); // Necháme dojet trail
                }
                else
                {
                    Destroy(vfx);
                }
            }
            _clientVFXInstances.Remove(effectName);
        }
    }

    // Pomocná funkce pro hledání kosti
    private Transform FindDeepChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child.name == name) return child;
            Transform result = FindDeepChild(child, name);
            if (result != null) return result;
        }
        return null;
    }
}