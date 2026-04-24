using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Split Effect")]
public class SplitEffect : HitEffect
{
    [Header("Split Settings")]
    [Tooltip("Na kolik nových střel se útok rozdělí")]
    public int SplitCount = 3;

    [Tooltip("Úhel vějíře ve stupních (např. 60 = střely se rozletí v 60° úhlu)")]
    public float SpreadAngle = 60f;

    [Tooltip("Kolik % poškození budou mít nové střely oproti původní ráně (0.5 = 50%)")]
    public float DamageMultiplier = 0.5f;

    [Header("Projectile Override")]
    [Tooltip("Pokud je true, pokusí se použít projektil z aktuální zbraně")]
    public bool UseCurrentWeaponProjectile = true;
    public GameObject FallbackProjectilePrefab;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        // Spawnování řeší výhradně server
        if (!NetworkManager.Singleton.IsServer) return;

        GameObject prefabToSpawn = (UseCurrentWeaponProjectile && manager.CurrentWeaponData != null)
            ? manager.CurrentWeaponData.ProjectilePrefab
            : FallbackProjectilePrefab;

        if (prefabToSpawn == null) return;

        // 1. Zjistíme základní směr (od útočníka k cíli)
        // Toto funguje UNIVERZÁLNĚ pro meč, laser, projektil i blesk.
        Vector3 baseDirection = (hitPosition - attacker.transform.position).normalized;
        baseDirection.y = 0; // Chceme, aby se to rozletělo rovnoběžně se zemí

        // 2. Zkopírujeme staty a snížíme damage
        WeaponStats splitStats = manager.CurrentRuntimeStats;
        splitStats.Damage = Mathf.RoundToInt(splitStats.Damage * DamageMultiplier);
        splitStats.PierceCount = 0; // Ochrana: splitnuté střely už nebudou penetrovat donekonečna

        // 3. Výpočet vějíře (Spread)
        float startAngle = -SpreadAngle / 2f;
        float angleStep = SplitCount > 1 ? SpreadAngle / (SplitCount - 1) : 0f;

        Vector3 spawnPos = hitPosition + Vector3.up * 1f; // Zvedneme mírně nad zem

        // 4. Spawn všech střel
        for (int i = 0; i < SplitCount; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            Quaternion spreadRotation = Quaternion.Euler(0, currentAngle, 0);
            Vector3 finalDir = spreadRotation * baseDirection;

            // A) Instanciace
            GameObject newProjGO = Instantiate(prefabToSpawn, spawnPos, Quaternion.LookRotation(finalDir));

            // B) Spawn do sítě (musí být před Initialize kvůli fyzice)
            if (newProjGO.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn(true);
            }

            // C) Inicializace s předáním zbylého batohu (KASKÁDOVÁNÍ)
            if (newProjGO.TryGetComponent(out SmartProjectile smartProj))
            {
                // ZDE SE PŘEDÁVÁ ZBYTEK EFEKTŮ (Výbuchy, Blesky atd.)
                smartProj.Initialize(attacker, finalDir, splitStats, remainingPayload);

                // Zabráníme okamžitému zásahu cíle, ve kterém jsme se právě rozdělili
                smartProj.AddIgnoredTarget(target);
            }
        }
    }

    public override string GetDescription()
    {
        return $"<color=#F0E68C><b>Split:</b></color> On impact, the attack splits into <color=white>{SplitCount}</color> " +
               $"projectiles in a <color=white>{SpreadAngle}°</color> fan, " +
               $"each dealing <color=#FF4444>{DamageMultiplier * 100:F0}% damage</color>.";
    }
}