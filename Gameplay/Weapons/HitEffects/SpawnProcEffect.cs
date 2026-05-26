using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Spawn Proc (Dynamic)")]
public class SpawnProcEffect : HitEffect
{
    [Header("Co se má spawnout")]
    public GameObject ProjectilePrefab;

    [Header("Staty pro nový projektil")]
    public float Speed = 20f;
    public float Range = 30f;
    public int DefaultDamage = 10;
    public float DamageMultiplier = 0.7f;

    [Header("Logika Dědičnosti")]
    [Tooltip("Pokud je TRUE, nový projektil zdědí všechny efekty, které má zbraň právě teď.")]
    public bool InheritWeaponEffects = true;

    [Tooltip("Zde můžeš přidat efekty, které má JENOM toto kouzlo navíc (např. vždy exploduje).")]
    public List<HitEffect> BasePayload = new List<HitEffect>();

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (ProjectileSpawnQueue.Instance == null)
        {
            Debug.LogError("[SpawnProcEffect] ProjectileSpawnQueue is missing in the scene!");
            return;
        }

        if (ProjectilePrefab == null) return;

        // 1. Výpočet pozice a směru
        Vector3 spawnPos = hitPosition + (Vector3.up * 1f);
        Vector3 spawnDir = Vector3.up; // Ponecháno z tvého originálu. Zvaž, zda nechceš střílet k cíli nebo od něj.

        // 2. Příprava Payloadu pro nový projektil
        List<HitEffect> finalPayload = new List<HitEffect>();

        if (BasePayload != null)
        {
            finalPayload.AddRange(BasePayload);
        }

        if (InheritWeaponEffects && remainingPayload != null)
        {
            foreach (var effect in remainingPayload)
            {
                // Ochrana proti zacyklení (Spawn proc nespawne další Spawn proc)
                if (effect is SpawnProcEffect) continue;
                finalPayload.Add(effect);
            }
        }

        // 3. Výpočet poškození (s ochranou proti null manageru)
        float baseDamage = (manager != null && manager.CurrentWeaponData != null) 
            ? manager.CurrentWeaponData.BaseStats.Damage 
            : 0f;

        float calculatedDamage = baseDamage * DamageMultiplier;
        float finalDamage = calculatedDamage > 0f ? calculatedDamage : DefaultDamage;

        WeaponStats procStats = new WeaponStats
        {
            ProjectileSpeed = Speed,
            Range = Range,
            Damage = (int)finalDamage
        };

        // 4. Vytvoření requestu a předání frontě
        SpawnRequest request = new SpawnRequest
        {
            Prefab = ProjectilePrefab,
            Position = spawnPos,
            Rotation = Quaternion.identity,
            Direction = spawnDir,
            Attacker = attacker,
            Stats = procStats,
            Payload = finalPayload,
            IgnoredTarget = target
        };

        ProjectileSpawnQueue.Instance.EnqueueSpawn(request);
    }

    public override string GetDescription()
    {
        string inheritText = InheritWeaponEffects ? "inherited and " : "";
        return $"<color=#00CED1><b>Proc - {ProjectilePrefab?.name}:</b></color> Spawns a secondary projectile on hit " +
               $"with <color=white>{inheritText}custom</color> effects.";
    }
}