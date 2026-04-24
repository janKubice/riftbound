using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public abstract class HitEffect : ScriptableObject
{   
    public string EffectName; // Pro zobrazení v UI

    // Každý efekt musí umět "udělat něco" při zásahu
    // Parametr 'remainingPayload' nese frontu zbývajících efektů pro kaskádování do dalších entit
    public abstract void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload);

    // Virtuální metoda pro formátovaný výpis
    public abstract string GetDescription();

    
}