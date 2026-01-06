using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "SpellAttack", menuName = "Attacks/Spell Logic")]
public class SpellAttackLogic : AttackLogic
{
    [Header("Spell Settings")]
    public bool HealsAllies = true;
    public int HealAmount = 5; // Kolik vyléčí
    public Color SpellColor = Color.cyan; // Pro vizuál

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats)
    {
        // 1. Oblast působení (kolem hráče)
        float radius = stats.Range > 0 ? stats.Range : 4.0f;
        Vector3 origin = attacker.transform.position;

        Collider[] hits = Physics.OverlapSphere(origin, radius);
        bool hitSomething = false;

        foreach (var hit in hits)
        {
            // A) Nepřátelé -> DAMAGE
            if (hit.TryGetComponent(out EnemyHealth enemy))
            {
                // Magický útok může mít bonusy
                enemy.TakeDamage(stats.Damage);
                
                // Spelly často dávají status efekty (Slow/Freeze)
                if (stats.Effect.Type != StatusEffectType.None)
                {
                    enemy.ApplyStatusEffect(stats.Effect);
                }
                hitSomething = true;
            }
            
            // B) Hráči -> HEAL / BUFF
            else if (HealsAllies && hit.TryGetComponent(out PlayerAttributes player))
            {
                // Můžeme se rozhodnout, jestli léčit i sebe, nebo jen spojence
                // Zde léčíme každého hráče v dosahu
                player.Heal(HealAmount);
                
                // Zde by šel přidat i Buff (např. SpeedUp)
                hitSomething = true;
            }
        }

        // 2. Vizuální efekt (Magický kruh)
        // Použijeme HitVFXPrefab ze zbraně jako "Explozivní efekt spellu"
        if (weaponManager.CurrentWeaponData.HitVFXPrefab != null)
        {
            // Spawneme efekt na pozici hráče (nebo na zemi pod ním)
            SpawnSpellVFX(weaponManager, origin, radius);
        }

        weaponManager.OnWeaponFiredServerRpc(stats.Cooldown);
    }

    private void SpawnSpellVFX(WeaponManager wm, Vector3 pos, float radius)
    {
        // Tohle pošleme přes existující RPC pro Impact, 
        // ale v budoucnu by to chtělo vlastní RPC pro "AreaEffect" s nastavením velikosti.
        wm.SpawnMeleeImpact(pos); 
    }
}