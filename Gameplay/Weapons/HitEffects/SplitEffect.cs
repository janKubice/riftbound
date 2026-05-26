using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Split Effect")]
public class SplitEffect : HitEffect
{
    [Header("Split Settings")]
    public int SplitCount = 3;
    public float SpreadAngle = 60f;
    public float DamageMultiplier = 0.5f;

    [Header("Projectile Override")]
    public bool UseCurrentWeaponProjectile = true;
    public GameObject FallbackProjectilePrefab;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (ProjectileSpawnQueue.Instance == null)
        {
            Debug.LogError("[SplitEffect] ProjectileSpawnQueue is missing in the scene!");
            return;
        }

        if (manager == null) return;

        GameObject prefabToSpawn = (UseCurrentWeaponProjectile && manager.CurrentWeaponData != null) 
            ? manager.CurrentWeaponData.ProjectilePrefab 
            : FallbackProjectilePrefab;

        if (prefabToSpawn == null) return;

        Vector3 rawDirection = hitPosition - attacker.transform.position;
        if (rawDirection.sqrMagnitude < 0.001f) rawDirection = attacker.transform.forward;
        
        Vector3 baseDirection = rawDirection.normalized;
        baseDirection.y = 0;

        WeaponStats splitStats = manager.CurrentRuntimeStats;
        splitStats.Damage = Mathf.RoundToInt(splitStats.Damage * DamageMultiplier);
        splitStats.PierceCount = 0;

        float startAngle = -SpreadAngle / 2f;
        float angleStep = SplitCount > 1 ? SpreadAngle / (SplitCount - 1) : 0f;
        Vector3 spawnPos = hitPosition + Vector3.up * 1f;

        // Nutné zkopírovat payload jednou, než ho předáme do fronty.
        // Asynchronní zpracování v pozdějších framech potřebuje izolovanou kopii.
        List<HitEffect> clonedPayload = new List<HitEffect>(remainingPayload);

        for (int i = 0; i < SplitCount; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            Quaternion spreadRotation = Quaternion.Euler(0, currentAngle, 0);
            Vector3 finalDir = spreadRotation * baseDirection;

            SpawnRequest request = new SpawnRequest
            {
                Prefab = prefabToSpawn,
                Position = spawnPos,
                Rotation = Quaternion.LookRotation(finalDir),
                Direction = finalDir,
                Attacker = attacker,
                Stats = splitStats,
                Payload = clonedPayload,
                IgnoredTarget = target
            };

            ProjectileSpawnQueue.Instance.EnqueueSpawn(request);
        }
    }

    public override string GetDescription()
    {
        return $"<color=#F0E68C><b>Split:</b></color> On impact, the attack splits into <color=white>{SplitCount}</color> " +
               $"projectiles in a <color=white>{SpreadAngle}°</color> fan, " +
               $"each dealing <color=#FF4444>{DamageMultiplier * 100:F0}% damage</color>.";
    }
}