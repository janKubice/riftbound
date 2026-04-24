using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Trigger Attack Logic")]
public class LogicTriggerEffect : HitEffect
{
    [Header("Jakou logiku spustit?")]
    public AttackLogic LogicToTrigger; // Sem přetáhneš ChainLightningLogic

    [Header("S jakými staty?")]
    public WeaponStats OverrideStats; // Staty pro blesk (Damage, Počet skoků...)

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (LogicToTrigger == null) return;

        // AttackLogic vyžaduje "FirePoint" (Transform), odkud útok vychází.
        // My ale máme jen pozici zásahu (Vector3).
        // Vytvoříme dočasný objekt na místě zásahu, který poslouží jako FirePoint.

        GameObject tempFirePoint = new GameObject("Temp_Proc_FirePoint");
        tempFirePoint.transform.position = hitPosition;

        // Pokud známe normálu dopadu nebo směr, mohli bychom ho natočit. 
        // Pro ChainLightning je to jedno, ten si hledá cíle v okolí.
        tempFirePoint.transform.rotation = Quaternion.identity;

        // SPUSTÍME LOGIKU
        // Použijeme OverrideStats definované v efektu, aby blesk měl vlastní damage
        LogicToTrigger.ExecuteAttack(attacker, manager, tempFirePoint.transform, OverrideStats);

        // Uklidíme dočasný bod
        Destroy(tempFirePoint);
    }

    public override string GetDescription()
    {
        string logicName = LogicToTrigger != null ? LogicToTrigger.name : "Sub-Attack";
        return $"<color=#44FFFF><b>Proc - {logicName}:</b></color> Triggers a secondary attack at the point of impact dealing " +
               $"<color=#FF4444>{OverrideStats.Damage} DMG</color>.";
    }
}