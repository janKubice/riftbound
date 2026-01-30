using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public abstract class HitEffect : ScriptableObject
{
    // Každý efekt musí umět "udělat něco" při zásahu
    public abstract void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager);
}