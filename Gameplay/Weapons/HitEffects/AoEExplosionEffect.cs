using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/AoE Status Explosion")]
public class AoEExplosionEffect : HitEffect
{
    [Header("Explosion Settings")]
    [Tooltip("Základní poloměr výbuchu (přičítá se k němu stat AreaSize)")]
    public float BaseRadius = 4f;
    [Tooltip("Vrstvy, které může výbuch zasáhnout (Nepřátelé, zničitelné objekty...)")]
    public LayerMask TargetLayers;

    [Header("Status Effect")]
    [Tooltip("Status efekt, který se aplikuje na všechny zasažené (např. Poison)")]
    public StatusEffectData StatusToApply;

    // Optimalizace: Pre-alokovaný statický buffer zamezuje GC alloc při častých výbuších
    private static readonly Collider[] _hitBuffer = new Collider[64];

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        WeaponStats currentStats = manager.CurrentRuntimeStats;
        float finalRadius = BaseRadius; //+ currentStats.AreaSize;
        int explosionDamage = currentStats.Damage;

        int hitCount = Physics.OverlapSphereNonAlloc(hitPosition, finalRadius, _hitBuffer, TargetLayers);

        // Příprava další vrstvy kaskády před cyklem, aby se minimalizovaly iterativní alokace
        HitEffect nextEffect = null;
        List<HitEffect> nextPayload = null;

        if (remainingPayload != null && remainingPayload.Count > 0)
        {
            nextEffect = remainingPayload[0];
            nextPayload = new List<HitEffect>(remainingPayload.Count - 1);
            for (int i = 1; i < remainingPayload.Count; i++)
            {
                nextPayload.Add(remainingPayload[i]);
            }
        }

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitBuffer[i];
            GameObject hitTarget = col.gameObject;
            bool validHit = false;

            if (col.TryGetComponent(out EnemyHealth enemy) || (enemy = col.GetComponentInParent<EnemyHealth>()))
            {
                enemy.TakeDamage(explosionDamage, attacker.OwnerClientId);

                if (StatusToApply != null && StatusToApply.Type != StatusEffectType.None)
                {
                    enemy.ApplyStatusEffect(StatusToApply);
                }

                hitTarget = enemy.gameObject;
                validHit = true;
            }
            else if (col.TryGetComponent(out DestructibleProp prop))
            {
                prop.TakeHit();
                validHit = true;
            }
            else if (col.TryGetComponent(out PlayerAttributes player) && player.NetworkObjectId != attacker.NetworkObjectId)
            {
                player.TakeDamageServerRpc(explosionDamage, attacker.OwnerClientId);
                hitTarget = player.gameObject;
                validHit = true;
            }

            // Aplikace zbývajících efektů pouze na validní cíle
            if (validHit && nextEffect != null)
            {
                // Předáváme pozici cíle jako novou hitPosition pro další kaskádování
                nextEffect.OnHit(col.transform.position, hitTarget, attacker, manager, nextPayload);
            }
        }
    }

    public override string GetDescription()
    {
        string statusInfo = (StatusToApply != null && StatusToApply.Type != StatusEffectType.None)
            ? $"\nApplies <color=#FF00FF>{StatusToApply.Type}</color> to all targets."
            : "";

        return $"<color=#FF8C00><b>Explosive Blast:</b></color> Triggers an explosion with " +
               $"<color=white>{BaseRadius}m</color> radius dealing " +
               $"<color=#FF4444>Weapon Damage</color>.{statusInfo}";
    }
}