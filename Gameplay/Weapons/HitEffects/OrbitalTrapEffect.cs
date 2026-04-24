using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Orbital Trap")]
public class OrbitalTrapEffect : HitEffect
{
    [Header("Trap Settings")]
    [Tooltip("Prefab orbitální střely (Musí mít OrbitalProjectile a NetworkObject)")]
    public GameObject OrbitalPrefab;

    [Tooltip("Kolik střel bude kolem cíle kroužit")]
    public int ProjectileCount = 3;

    [Tooltip("Vzdálenost střel od středu (poloměr prstence)")]
    public float OrbitRadius = 3.5f;

    [Tooltip("Rychlost rotace ve stupních za sekundu")]
    public float OrbitSpeed = 360f;

    [Tooltip("Kolik % poškození budou tyto rotující čepele dávat")]
    public float DamageMultiplier = 0.5f;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (OrbitalPrefab == null) return;

        // Zkopírujeme staty zbraně
        WeaponStats orbitStats = manager.CurrentRuntimeStats;
        orbitStats.Damage = Mathf.RoundToInt(orbitStats.Damage * DamageMultiplier);
        orbitStats.ProjectileSpeed = 0f; // Rychlost řídíme my z updatu
        orbitStats.Range = 9999f; // Vypne autodestrukci na limit Range ve SmartProjectile

        // DŮLEŽITÉ: Aby se prstenec nezničil hned o prvního goblina, dáme mu průraz!
        // To zaručí, že střela na orbitě vydrží řezat celých 5 sekund.
        orbitStats.PierceCount = 99;

        float angleStep = 360f / ProjectileCount;

        Transform orbitTarget = target != null ? target.transform : null;
        Vector3 fallbackCenter = target != null ? target.transform.position + Vector3.up * 1f : hitPosition + Vector3.up * 1f;

        for (int i = 0; i < ProjectileCount; i++)
        {
            float startAngle = angleStep * i;
            float rad = startAngle * Mathf.Deg2Rad;

            Vector3 spawnPos = fallbackCenter + new Vector3(Mathf.Cos(rad) * OrbitRadius, 0, Mathf.Sin(rad) * OrbitRadius);

            GameObject newProjGO = Instantiate(OrbitalPrefab, spawnPos, Quaternion.identity);

            // 1. Spawn do sítě
            if (newProjGO.TryGetComponent(out NetworkObject netObj))
            {
                netObj.Spawn(true);
            }

            // 2. Inicializace batohu a oběžné dráhy
            if (newProjGO.TryGetComponent(out OrbitalProjectile orbitalProj))
            {
                // Předáme zbytek batohu do každé orbitální střely!
                orbitalProj.Initialize(attacker, Vector3.forward, orbitStats, remainingPayload);
                orbitalProj.InitializeOrbit(orbitTarget, fallbackCenter, OrbitRadius, OrbitSpeed, startAngle);

                // Střely nesmí trefit cíl, kolem kterého obíhají (ochrana hostitele)
                if (target != null) orbitalProj.AddIgnoredTarget(target);
            }
        }
    }

    public override string GetDescription()
    {
        return $"<color=#B0C4DE><b>Orbital Trap:</b></color> Summons <color=white>{ProjectileCount}</color> projectiles that circle the target, " +
               $"dealing <color=#FF4444>{DamageMultiplier * 100:F0}% Weapon Damage</color> to anything they touch.";
    }
}