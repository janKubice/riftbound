using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Ricochet Effect")]
public class RicochetEffect : HitEffect
{
    [Header("Dynamic Ricochet Settings")]
    public bool UseCurrentWeaponProjectile = true;
    public GameObject FallbackProjectilePrefab;

    [Header("Stats")]
    public int MaxBounces = 2;
    public float SearchRadius = 15f;
    public float BounceSpeed = 20f;
    public float DamageMultiplier = 0.8f;
    public LayerMask EnemyLayer;

    // Optimalizace paměti pro fyziku (zabrání GC alloc v každém framu)
    private static readonly Collider[] _hitBuffer = new Collider[32];

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer || MaxBounces <= 0) return;

        GameObject prefabToSpawn = (UseCurrentWeaponProjectile && manager.CurrentWeaponData != null)
            ? manager.CurrentWeaponData.ProjectilePrefab
            : FallbackProjectilePrefab;

        if (prefabToSpawn == null) return;

        GameObject nextTarget = FindNextTarget(hitPosition, target);
        if (nextTarget == null) return;

        Vector3 spawnPos = hitPosition + (nextTarget.transform.position - hitPosition).normalized * 0.5f + Vector3.up * 1f;
        Vector3 aimPoint = nextTarget.transform.position + Vector3.up * 1f;
        Vector3 direction = (aimPoint - spawnPos).normalized;

        // 1. Instanciace lokálně
        GameObject newProjGO = Instantiate(prefabToSpawn, spawnPos, Quaternion.LookRotation(direction));

        // 2. SÍŤOVÝ SPAWN (Přesunuto před inicializaci!)
        // Tímto zajistíme, že Unity Netcode neanuluje fyziku (linearVelocity) při spawnu
        if (newProjGO.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }

        // 3. Nastavení projektilu a sestavení payloadu
        if (newProjGO.TryGetComponent(out SmartProjectile smartProj))
        {
            // Zkopírujeme staty původní zbraně, abychom nepřišli o Crity, Knockback atd.
            WeaponStats bounceStats = manager.CurrentRuntimeStats;

            // Přepíšeme pouze to, co se mění odrazem
            bounceStats.Damage = Mathf.RoundToInt(bounceStats.Damage * DamageMultiplier);

            // POJISTKA: Pokud je BounceSpeed v Inspectoru 0, zdědíme rychlost původní střely
            bounceStats.ProjectileSpeed = BounceSpeed > 0 ? BounceSpeed : bounceStats.ProjectileSpeed;
            bounceStats.Range = SearchRadius * 2f;
            bounceStats.PierceCount = 0;

            List<HitEffect> newPayload = new List<HitEffect>();

            // Pokud se má střela odrazit ještě víckrát
            if (this.MaxBounces > 1)
            {
                RicochetEffect downgradedRicochet = Instantiate(this);
                downgradedRicochet.MaxBounces = this.MaxBounces - 1;
                newPayload.Add(downgradedRicochet);
            }

            // Přidáme zbytek efektů (Výbuchy, Blesky...)
            if (remainingPayload != null && remainingPayload.Count > 0)
            {
                newPayload.AddRange(remainingPayload);
            }

            // Aplikujeme rychlost a data do už spawnutého objektu
            smartProj.Initialize(attacker, direction, bounceStats, newPayload);
            smartProj.AddIgnoredTarget(target);
        }
    }
    private GameObject FindNextTarget(Vector3 center, GameObject hitTarget)
    {
        int hitCount = Physics.OverlapSphereNonAlloc(center, SearchRadius, _hitBuffer, EnemyLayer);
        GameObject bestTarget = null;
        float closestDist = float.MaxValue;

        // Identifikace rootu původního cíle pro přesné porovnání
        Transform hitTargetRoot = hitTarget.transform.root;

        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCol = _hitBuffer[i];

            // 1. Striktní kontrola: Je to skutečně nepřítel a ne projektil nebo prostředí?
            if (!hitCol.TryGetComponent<EnemyHealth>(out EnemyHealth enemyHealth))
                continue;

            // 2. Kontrola: Není to ten samý nepřítel? (Porovnání přes EnemyHealth i Root zaručí filtraci)
            if (enemyHealth.gameObject == hitTarget || enemyHealth.transform.root == hitTargetRoot)
                continue;

            // 3. Kontrola: Není mrtvý?
            if (enemyHealth.CurrentHealth.Value <= 0)
                continue;

            float d = Vector3.Distance(center, hitCol.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = enemyHealth.gameObject;
            }
        }

        // Vyčištění bufferu
        System.Array.Clear(_hitBuffer, 0, hitCount);

        return bestTarget;
    }

    public override string GetDescription()
    {
        return $"<color=#FFD700><b>Ricochet:</b></color> Projectiles bounce up to <color=white>{MaxBounces}x</color> " +
               $"towards enemies within <color=white>{SearchRadius}m</color>. " +
               $"Bounced shots deal <color=#FF4444>{DamageMultiplier * 100:F0}% damage</color>.";
    }

}