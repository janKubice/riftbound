using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Time Echo")]
public class TimeEchoEffect : HitEffect
{
    [Header("Echo Settings")]
    [Tooltip("Prefab ozvěny (Musí mít NetworkObject a TimeEchoController)")]
    public GameObject EchoPrefab;

    [Tooltip("Jak dlouho ozvěna ve světě vydrží (v sekundách)")]
    public float Duration = 4f;

    [Tooltip("Jak často bude ozvěna pulzovat / střílet (1.0 = každou sekundu)")]
    public float TickInterval = 1f;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (EchoPrefab == null) return;

        // Zvedneme pozici spawnu mírně nad zem/bod dopadu
        Vector3 spawnPos = hitPosition + Vector3.up * 1f;

        // Natočíme ozvěnu stejným směrem, jakým se dívá hráč. 
        // To je super, pokud z ní budou vylétat projektily dopředu!
        Quaternion spawnRot = attacker != null ? attacker.transform.rotation : Quaternion.identity;

        // 1. Instanciace
        GameObject echoGo = Instantiate(EchoPrefab, spawnPos, spawnRot);

        // 2. Síťový spawn
        if (echoGo.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }

        // 3. Inicializace a předání batohu do ozvěny
        if (echoGo.TryGetComponent(out TimeEchoController echo))
        {
            echo.Initialize(attacker, manager, remainingPayload, Duration, TickInterval);
        }
    }

    public override string GetDescription()
    {
        return $"<color=#4682B4><b>Time Echo:</b></color> Leaves an echo at the impact site for <color=white>{Duration}s</color>. " +
               $"The echo pulses every <color=white>{TickInterval}s</color>, " +
               $"replaying all remaining hit effects.";
    }
}