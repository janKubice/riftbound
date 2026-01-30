using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "LaserAttack", menuName = "Attacks/Laser Logic")]
public class LaserAttackLogic : AttackLogic
{
    [Header("Settings")]
    public float MaxDistance = 50f;
    public LayerMask HitMask;
    public int ManaCost = 0;

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats)
    {
        // 1. Mana & Validace
        if (firePoint == null) return;
        if (ManaCost > 0 && attacker.TryGetComponent(out PlayerAttributes attr))
        {
            if (attr.CurrentMana.Value < ManaCost) return;
            //attr.ConsumeManaServerRpc(ManaCost);
        }

        // 2. Raycast (pro Damage)
        Vector3 start = firePoint.position;
        Vector3 dir = firePoint.forward;

        if (attacker.TryGetComponent(out PlayerAiming aiming))
        {
            dir = (aiming.CurrentAimPoint - start).normalized;
        }

        float range = stats.Range > 0 ? stats.Range : MaxDistance;
        RaycastHit[] hits = Physics.RaycastAll(start, dir, range, HitMask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider.gameObject == attacker.gameObject) continue;

            // A) Damage
            if (hit.collider.TryGetComponent(out EnemyHealth enemy))
            {
                enemy.TakeDamage(stats.Damage, attacker.OwnerClientId);
                if (stats.Effect.Type != StatusEffectType.None) enemy.ApplyStatusEffect(stats.Effect);
            }
            else if (hit.collider.TryGetComponent(out PlayerAttributes p))
            {
                p.TakeDamageServerRpc(stats.Damage);
            }

            // B) Impact Effect (Jiskry při dopadu)
            // Toto zavolá ClientRPC ve WeaponManageru, které spawne particle na místě dopadu
            weaponManager.SpawnMeleeImpact(hit.point);

            break; // Laser damage končí u první překážky
        }

        // Poznámka: Žádný SpawnLaserServerRpc zde nevoláme!
        // O vizuál pruhu se stará WeaponVisualsController.
    }
}