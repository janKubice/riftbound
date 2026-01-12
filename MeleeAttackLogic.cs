using UnityEngine;
using Unity.Netcode;
using Unity.VisualScripting.Antlr3.Runtime.Misc;

[CreateAssetMenu(fileName = "MeleeAttack", menuName = "Attacks/Melee Logic")]
public class MeleeAttackLogic : AttackLogic
{
    private static readonly Collider[] _hitBuffer = new Collider[50];

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats)
    {
        Debug.Log("[MeleeAttackLogic] ExecuteAttack.");
        // --- SERVER SIDE LOGIC ---

        // 1. Definice oblasti útoku
        // Posuneme střed mírně dopředu a nahoru, aby to lépe odpovídalo vizuálu
        Vector3 origin = attacker.transform.position + Vector3.up * 1.0f;
        Vector3 forward = attacker.transform.forward;

        // Použijeme Range ze statů (vylepšitelné)
        float range = stats.Range > 0 ? stats.Range : 2.0f;

        // 2. Detekce kolizí (NonAlloc pro výkon)
        int hitCount = Physics.OverlapSphereNonAlloc(origin, range, _hitBuffer);

        bool hitSomething = false;

        // 3. Procházení zasažených objektů
        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];

            // Ignorujeme sami sebe
            if (hit.gameObject == attacker.gameObject) continue;
            // Ignorujeme Triggery (např. loot na zemi)
            if (hit.isTrigger) continue;

            // 4. Kontrola úhlu (Cone of Fire)
            Vector3 dirToTarget = (hit.transform.position - origin).normalized;
            float angle = Vector3.Angle(forward, dirToTarget);

            // Pokud je v úhlu záběru (polovina úhlu na každou stranu)
            if (angle <= stats.AttackAngle / 2f)
            {
                // --- VÝPOČET POŠKOZENÍ (CRIT) ---
                bool isCrit = Random.value < stats.CritChance;
                int finalDamage = Mathf.RoundToInt(stats.Damage * (isCrit ? stats.CritMultiplier : 1.0f));

                bool entityHit = false;

                // A) Zásah Nepřítele
                if (hit.TryGetComponent(out EnemyHealth enemy))
                {
                    // Aplikace Damage
                    enemy.TakeDamage(finalDamage);

                    // Aplikace Knockbacku (Odhození)
                    if (stats.Knockback > 0)
                    {
                        // Směr od hráče k nepříteli (zploštělý na Y nulu, aby neletěli do nebe)
                        Vector3 knockDir = (hit.transform.position - attacker.transform.position);
                        knockDir.y = 0;
                        enemy.ApplyKnockback(knockDir.normalized * stats.Knockback);
                    }

                    entityHit = true;
                }
                // B) Zásah Hráče (PvP)
                else if (hit.TryGetComponent(out PlayerAttributes player))
                {
                    player.TakeDamageServerRpc(finalDamage);
                    entityHit = true;
                }

                if (hit.TryGetComponent(out StatusEffectReceiver receiver))
                {
                    // Aplikujeme efekt definovaný ve zbrani
                    if (stats.Effect != null && stats.Effect.Type != StatusEffectType.None)
                    {
                        receiver.ApplyStatusEffect(stats.Effect);
                    }
                }

                if (hit.TryGetComponent(out DestructibleProp prop))
                {
                    prop.TakeHit();
                    // Můžeš přidat hitSound / impact effect
                }

                // C) Spawn Hit VFX
                if (entityHit)
                {
                    hitSomething = true;
                    // Najdeme nejbližší bod na collideru pro spawn krve/jisker
                    Vector3 hitPos = hit.ClosestPoint(origin);
                    weaponManager.SpawnMeleeImpact(hitPos);
                }
            }
        }

        // Vyčistíme buffer
        System.Array.Clear(_hitBuffer, 0, hitCount);

        // 5. Zpětná vazba pro útočníka
        // Řekneme klientům, ať přehrají animaci švihu a trail
        weaponManager.OnWeaponFiredServerRpc(stats.Cooldown);

        // Zvuk zásahu (pokud jsme něco trefili)
        if (hitSomething && attacker.TryGetComponent(out PlayerAudio audio))
        {
            audio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_HIT_DEALT);
        }
    }
}