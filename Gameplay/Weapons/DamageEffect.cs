using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(menuName = "Effects/Basic Damage")]
public class DamageEffect : HitEffect
{
    public int DamageAmount = 10;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager)
    {
        // 1. Zkusíme EnemyHealth
        if (target.TryGetComponent(out EnemyHealth enemy))
        {
            enemy.TakeDamage(DamageAmount, attacker.OwnerClientId);
        }
        // 2. Zkusíme Player (PvP)
        else if (target.TryGetComponent(out PlayerAttributes player))
        {
            // Friendly Fire check by měl být tady, ale pro jednoduchost:
            if (player.NetworkObjectId != attacker.NetworkObjectId)
            {
                player.TakeDamageServerRpc(DamageAmount);
            }
        }
    }
}