using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Basic Damage")]
public class DamageEffect : HitEffect
{
    [Header("Damage Settings")]
    [Tooltip("Pokud je true, použije poškození z aktuálních statů zbraně (WeaponStats).")]
    public bool UseWeaponStatsDamage = true;

    [Tooltip("Fixní poškození, které se použije pokud je UseWeaponStatsDamage vypnuté, NEBO jako bonusový flat damage.")]
    public int BaseDamageAmount = 0;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        // 1. Výpočet celkového poškození na základě statů zbraně
        int finalDamage = BaseDamageAmount;
        if (UseWeaponStatsDamage && manager != null)
        {
            finalDamage += manager.CurrentRuntimeStats.Damage;
        }

        bool validHit = false;
        GameObject actualTarget = target;

        // 2. Vyhodnocení zásahu včetně vyhledání komponenty v rodičovských objektech
        if (target.TryGetComponent(out EnemyHealth enemy) || (enemy = target.GetComponentInParent<EnemyHealth>()))
        {
            enemy.TakeDamage(finalDamage, attacker.OwnerClientId);
            actualTarget = enemy.gameObject;
            validHit = true;
        }
        else if (target.TryGetComponent(out PlayerAttributes player) || (player = target.GetComponentInParent<PlayerAttributes>()))
        {
            if (player.NetworkObjectId != attacker.NetworkObjectId)
            {
                player.TakeDamageServerRpc(finalDamage, attacker.OwnerClientId);
                actualTarget = player.gameObject;
                validHit = true;
            }
        }
        else if (target.TryGetComponent(out DestructibleProp prop) || (prop = target.GetComponentInParent<DestructibleProp>()))
        {
            prop.TakeHit();
            actualTarget = prop.gameObject;
            validHit = true;
        }

        // 3. Kaskádování zbývajících efektů ve frontě
        if (validHit && remainingPayload != null && remainingPayload.Count > 0)
        {
            HitEffect nextEffect = remainingPayload[0];

            // Alokace a zkopírování zbývající fronty bez GC zátěže v RemoveAt(0)
            List<HitEffect> nextPayload = new List<HitEffect>(remainingPayload.Count - 1);
            for (int i = 1; i < remainingPayload.Count; i++)
            {
                nextPayload.Add(remainingPayload[i]);
            }

            nextEffect.OnHit(hitPosition, actualTarget, attacker, manager, nextPayload);
        }
    }

    public override string GetDescription()
    {
        string damageInfo = UseWeaponStatsDamage ? "Weapon Damage" : "";
        string bonusInfo = (BaseDamageAmount > 0) ? $" + <color=#FF4444>{BaseDamageAmount}</color> flat damage" : "";

        return $"<color=#FF4444><b>Strike:</b></color> Deals <color=white>{damageInfo}{bonusInfo}</color> to the target.";
    }
}