using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic; // Přidáno pro List

[CreateAssetMenu(fileName = "MeleeAttack", menuName = "Attacks/Melee Logic")]
public class MeleeAttackLogic : AttackLogic
{
    private static readonly Collider[] _hitBuffer = new Collider[50];

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats, int projectileCountBonus = 0)
    {
        Vector3 origin = attacker.transform.position + Vector3.up * 1.0f;
        Vector3 forward = attacker.transform.forward;
        float range = stats.Range > 0 ? stats.Range : 2.0f;

        int hitCount = Physics.OverlapSphereNonAlloc(origin, range, _hitBuffer);
        bool hitSomething = false;
        
        // --- MULTISHOT (MULTI-STRIKE) LOGIKA ---
        int strikeCount = stats.ProjectileCount + projectileCountBonus; // Kolikrát dostane ránu

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = _hitBuffer[i];
            if (hit.gameObject == attacker.gameObject || hit.isTrigger) continue;

            Vector3 dirToTarget = (hit.transform.position - origin).normalized;
            float angle = Vector3.Angle(forward, dirToTarget);

            if (angle <= stats.AttackAngle / 2f)
            {
                // Udělíme poškození X-krát (podle ProjectileCount)
                for (int strike = 0; strike < strikeCount; strike++)
                {
                    bool isCrit = Random.value < stats.CritChance;
                    int finalDamage = Mathf.RoundToInt(stats.Damage * (isCrit ? stats.CritMultiplier : 1.0f));
                    bool entityHit = false;

                    // A) Zásah Nepřítele
                    if (hit.TryGetComponent(out EnemyHealth enemy) || (enemy = hit.GetComponentInParent<EnemyHealth>()))
                    {
                        enemy.TakeDamage(finalDamage, attacker.OwnerClientId); // Přidán attacker.OwnerClientId
                        if (stats.Knockback > 0 && strike == 0) // Knockback jen při první ráně, ať neletí na Mars
                        {
                            Vector3 knockDir = (hit.transform.position - attacker.transform.position);
                            knockDir.y = 0;
                            enemy.ApplyKnockback(knockDir.normalized * stats.Knockback);
                        }
                        entityHit = true;
                    }
                    // B) Zásah Hráče
                    else if (hit.TryGetComponent(out PlayerAttributes player))
                    {
                        player.TakeDamageServerRpc(finalDamage, attacker.OwnerClientId); // Přidán attacker.OwnerClientId
                        entityHit = true;
                    }

                    // C) Aplikace Status Efektu
                    if (entityHit && stats.Effect != null && stats.Effect.Type != StatusEffectType.None)
                    {
                        if (hit.TryGetComponent(out StatusEffectReceiver receiver)) receiver.ApplyStatusEffect(stats.Effect);
                    }

                    // D) SPUŠTĚNÍ EFEKTŮ Z BATOHU (Každý strike = nový Meteor/Ricochet!)
                    if (stats.OnHitEffects != null && stats.OnHitEffects.Count > 0)
                    {
                        // 1. Vezmeme POUZE PRVNÍ efekt na řadě
                        HitEffect activeEffect = stats.OnHitEffects[0];

                        // 2. Vytvoříme zbytek fronty (vše kromě prvního efektu)
                        List<HitEffect> remainingPayload = new List<HitEffect>();
                        for (int p = 1; p < stats.OnHitEffects.Count; p++)
                        {
                            remainingPayload.Add(stats.OnHitEffects[p]);
                        }

                        // 3. Spustíme aktivní efekt a předáme mu zbytek batohu
                        if (activeEffect != null)
                        {
                            activeEffect.OnHit(hit.ClosestPoint(origin), hit.gameObject, attacker, weaponManager, remainingPayload);
                        }
                    }

                    // E) Vizuál a prostředí
                    if (strike == 0) // Tyto věci stačí spustit jednou za švih
                    {
                        if (entityHit)
                        {
                            hitSomething = true;
                            weaponManager.SpawnMeleeImpact(hit.ClosestPoint(origin));
                        }
                        else if (hit.TryGetComponent(out DestructibleProp prop))
                        {
                            prop.TakeHit();
                            hitSomething = true;
                        }
                        else if (!hit.isTrigger)
                        {
                            hitSomething = true;
                            weaponManager.SpawnMeleeImpact(hit.ClosestPoint(origin));
                        }
                    }
                }
            }
        }

        System.Array.Clear(_hitBuffer, 0, hitCount);
        weaponManager.OnWeaponFiredServerRpc(stats.Cooldown);

        if (hitSomething && attacker.TryGetComponent(out PlayerAudio audio))
        {
            audio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_HIT_DEALT);
        }
    }
}