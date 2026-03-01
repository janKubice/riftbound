using UnityEngine;
using Unity.Netcode;

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

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager)
    {
        // Výpočet finálního poloměru a poškození z aktuálních statů hráče
        WeaponStats currentStats = manager.CurrentRuntimeStats;
        float finalRadius = BaseRadius; //+ currentStats.AreaSize;
        int explosionDamage = currentStats.Damage;

        // Najdeme všechny objekty v dosahu
        Collider[] hits = Physics.OverlapSphere(hitPosition, finalRadius, TargetLayers);

        foreach (var col in hits)
        {
            // Aplikace na nepřátele
            if (col.TryGetComponent(out EnemyHealth enemy) || (enemy = col.GetComponentInParent<EnemyHealth>()))
            {
                // Základní poškození z výbuchu
                enemy.TakeDamage(explosionDamage, attacker.OwnerClientId);

                // Otrávení (DoT)
                if (StatusToApply != null && StatusToApply.Type != StatusEffectType.None)
                {
                    enemy.ApplyStatusEffect(StatusToApply);
                }
            }
            // Aplikace na zničitelné bedny (volitelné)
            else if (col.TryGetComponent(out DestructibleProp prop))
            {
                prop.TakeHit();
            }
            // PvP (volitelné - zraní ostatní hráče, ale ne sebe)
            else if (col.TryGetComponent(out PlayerAttributes player) && player.NetworkObjectId != attacker.NetworkObjectId)
            {
                player.TakeDamageServerRpc(explosionDamage);
            }
        }
    }
}