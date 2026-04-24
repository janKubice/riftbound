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
        // 1. Spawn
        Vector3 spawnPos = hitPosition + (Vector3.up * 1f);
        GameObject newProjGO = Instantiate(ProjectilePrefab, spawnPos, Quaternion.identity);

        // 2. Příprava Payloadu pro nový projektil
        List<HitEffect> finalPayload = new List<HitEffect>();

        // Přidáme specifické efekty pro tento proc (např. vždy exploduje)
        if (BasePayload != null)
        {
            finalPayload.AddRange(BasePayload);
        }

        // Pokud má dědit efekty, vezme ZBYTEK fronty, ne efekty ze zbraně!
        if (InheritWeaponEffects && remainingPayload != null)
        {
            foreach (var effect in remainingPayload)
            {
                // Ochrana proti zacyklení (Spawn proc nespawne další Spawn proc)
                if (effect is SpawnProcEffect) continue;
                finalPayload.Add(effect);
            }
        }

        float baseDamage = manager.CurrentWeaponData != null ? manager.CurrentWeaponData.BaseStats.Damage : 0f;

        float calculatedDamage = baseDamage * DamageMultiplier;
        float finalDamage = calculatedDamage > 0f ? calculatedDamage : DefaultDamage;

        // 3. Inicializace projektilu s namíchaným payloadem
        if (newProjGO.TryGetComponent(out SmartProjectile smartProj))
        {
            WeaponStats procStats = new WeaponStats();
            procStats.ProjectileSpeed = Speed;
            procStats.Range = Range;
            procStats.Damage = (int)finalDamage;

            // Předáme vygenerovaný seznam a přidáme ignorovaný cíl, ať ho to hned netrefí
            smartProj.Initialize(attacker, Vector3.up, procStats, finalPayload);
            smartProj.AddIgnoredTarget(target);
        }

        if (newProjGO.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }
    }

    public override string GetDescription()
    {
        string inheritText = InheritWeaponEffects ? "inherited and " : "";
        return $"<color=#00CED1><b>Proc - {ProjectilePrefab.name}:</b></color> Spawns a secondary projectile on hit " +
               $"with <color=white>{inheritText}custom</color> effects.";
    }
}