using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Parasitic Spores")]
public class ParasiticSporesEffect : HitEffect
{
    [Header("Parasite Settings")]
    [Tooltip("Jak dlouho parazit na nepříteli žije (v sekundách)?")]
    public float Duration = 5f;

    [Tooltip("Jak často odpálí zbytek batohu? (1.0 = každou sekundu)")]
    public float TickInterval = 1f;

    [Tooltip("Poškození samotného hostitele při každém tiku (DoT)")]
    public int TickDamage = 10;

    [Header("Visuals")]
    [Tooltip("Prefab částic (např. zelené výtrusy), který se přilepí na nepřítele. Nemusí mít NetworkObject.")]
    public GameObject SporeVisualPrefab;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // Parazita lze přisát jen na živého nepřítele
        if (target.TryGetComponent(out EnemyHealth enemy) || (enemy = target.GetComponentInParent<EnemyHealth>()))
        {
            if (enemy.CurrentHealth.Value <= 0) return;

            // Ochrana: Aby na sobě nemohl mít jeden nepřítel 50 stejných parazitů naráz
            if (enemy.GetComponent<ParasiteController>() != null) return;

            // Přilepíme na něj parazita a předáme mu náš batoh!
            ParasiteController parasite = enemy.gameObject.AddComponent<ParasiteController>();
            parasite.Initialize(attacker, manager, remainingPayload, Duration, TickInterval, TickDamage, SporeVisualPrefab);
        }
    }

    public override string GetDescription()
    {
        return $"<color=#7CFC00><b>Parasitic Spores:</b></color> Attaches spores to the target for " +
               $"<color=white>{Duration}s</color>. Deals <color=#FF4444>{TickDamage} DoT</color> and triggers " +
               $"remaining effects every <color=white>{TickInterval}s</color>.";
    }
}