using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "LaserAttack", menuName = "Attacks/Laser Logic")]
public class LaserAttackLogic : AttackLogic
{
    [Header("Settings")]
    public float MaxDistance = 50f;
    public LayerMask HitMask;
    public int ManaCost = 0;

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats, int projectileCountBonus = 0)
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

        Quaternion baseRotation = Quaternion.LookRotation(dir);

        int count = stats.ProjectileCount + projectileCountBonus;

        // POJISTKA: Pokud na zbrani zapomeneš nastavit Spread, dáme záchranných 45 stupňů
        float actualSpread = stats.Spread > 0 ? stats.Spread : 45f;

        float startAngle = -actualSpread / 2f;
        float angleStep = count > 1 ? actualSpread / (count - 1) : 0f;
        float range = stats.Range > 0 ? stats.Range : MaxDistance;

        for (int i = 0; i < count; i++)
        {
            float currentAngle = startAngle + (angleStep * i);

            // Aplikujeme lokální rotaci na naši base rotaci
            Quaternion spreadRot = Quaternion.Euler(0, currentAngle, 0);

            // Převedeme zpět na směr (vektor)
            Vector3 finalDir = (baseRotation * spreadRot) * Vector3.forward;

            // 4. Raycast pro tento konkrétní paprsek
            RaycastHit[] hits = Physics.RaycastAll(start, finalDir, range, HitMask, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var hit in hits)
            {
                if (hit.collider.gameObject == attacker.gameObject) continue;

                // A) Damage
                if (hit.collider.TryGetComponent(out EnemyHealth enemy) || (enemy = hit.collider.GetComponentInParent<EnemyHealth>()))
                {
                    enemy.TakeDamage(stats.Damage, attacker.OwnerClientId);
                    if (stats.Effect != null && stats.Effect.Type != StatusEffectType.None) enemy.ApplyStatusEffect(stats.Effect);

                    // SPUŠTĚNÍ EFEKTŮ (Ricochet atd.)
                    ExecutePayload(hit.collider.gameObject, hit.point, attacker, weaponManager, stats);
                }
                else if (hit.collider.TryGetComponent(out PlayerAttributes p))
                {
                    p.TakeDamageServerRpc(stats.Damage);
                    ExecutePayload(hit.collider.gameObject, hit.point, attacker, weaponManager, stats);
                }

                weaponManager.SpawnMeleeImpact(hit.point);
                break; // Laser končí na první překážce
            }
        }
    }

    private void ExecutePayload(GameObject target, Vector3 hitPos, NetworkObject attacker, WeaponManager manager, WeaponStats stats)
    {
        if (stats.OnHitEffects == null) return;
        foreach (var effect in stats.OnHitEffects)
        {
            if (effect != null) effect.OnHit(hitPos, target, attacker, manager);
        }
    }
}