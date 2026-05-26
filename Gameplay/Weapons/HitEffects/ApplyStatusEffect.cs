using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Apply Status")]
public class ApplyStatusEffect : HitEffect
{
    [Header("Status Settings")]
    [Tooltip("Status efekt, který se má aplikovat při průchodu tímto nodem.")]
    public StatusEffectData StatusToApply;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        bool validHit = false;
        GameObject actualTarget = target;

        // 1. Vyhodnocení cíle a aplikace statusu (s hledáním v parentovi jako u DamageEffectu)
        if (target.TryGetComponent(out EnemyHealth enemy) || (enemy = target.GetComponentInParent<EnemyHealth>()))
        {
            if (StatusToApply != null)
            {
                enemy.ApplyStatusEffect(StatusToApply);
            }
            actualTarget = enemy.gameObject;
            validHit = true;
        }
        // Odkomentuj, pokud se mají statusy aplikovat i na hráče v PvP
        // else if (target.TryGetComponent(out PlayerAttributes player) || (player = target.GetComponentInParent<PlayerAttributes>()))
        // { ... }

        // 2. Kaskádování zbývajících efektů ve frontě
        if (validHit && remainingPayload != null && remainingPayload.Count > 0)
        {
            HitEffect nextEffect = remainingPayload[0];

            // Alokace a zkopírování zbývající fronty bez GC zátěže
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
        if (StatusToApply == null)
            return "<color=#F0E68C><b>Apply Status:</b></color> <color=red>No status selected!</color>";

        List<string> details = new List<string>();

        // 1. Základní info (Jméno, trvání, případně stacky)
        string stackInfo = StatusToApply.IsStackable ? $" (Max <color=white>{StatusToApply.MaxStacks}</color> stacks)" : "";
        details.Add($"Applies <color=#FFA500>{StatusToApply.EffectName}</color> for <color=white>{StatusToApply.Duration:F1}s</color>{stackInfo}");

        // 2. Poškození v čase (DoT)
        if (StatusToApply.DamagePerTick > 0)
        {
            string dmgType = StatusToApply.IsDamagePercentage ? "% Max HP" : " dmg";
            details.Add($"Deals <color=#FF4444>{StatusToApply.DamagePerTick}{dmgType}</color> every <color=white>{StatusToApply.TickInterval:F1}s</color>");
        }

        // 3. Modifikátory rychlosti
        if (StatusToApply.SpeedMultiplier == 0f)
        {
            details.Add("<color=#87CEFA>Roots</color> the target");
        }
        else if (StatusToApply.SpeedMultiplier < 1.0f)
        {
            int slowPercent = Mathf.RoundToInt((1.0f - StatusToApply.SpeedMultiplier) * 100);
            details.Add($"<color=#87CEFA>Slows</color> by <color=white>{slowPercent}%</color>");
        }

        // 4. Modifikátory zranění
        if (StatusToApply.DamageReceivedMultiplier > 1.0f)
        {
            int extraDmg = Mathf.RoundToInt((StatusToApply.DamageReceivedMultiplier - 1.0f) * 100);
            details.Add($"Target takes <color=#FF8C00>{extraDmg}%</color> more damage");
        }
        if (StatusToApply.DamageDealtMultiplier < 1.0f)
        {
            int lessDmg = Mathf.RoundToInt((1.0f - StatusToApply.DamageDealtMultiplier) * 100);
            details.Add($"Target deals <color=#FF8C00>{lessDmg}%</color> less damage");
        }

        // 5. Hard CC
        if (StatusToApply.IsStun)
        {
            details.Add("<color=#FF00FF>Stuns</color> the target");
        }
        if (StatusToApply.IsSilenced)
        {
            details.Add("<color=#9370DB>Silences</color> the target");
        }

        // Sloučení všech validních informací oddělených čárkou
        return $"<color=#F0E68C><b>Status:</b></color> " + string.Join(", ", details) + ".";
    }
}