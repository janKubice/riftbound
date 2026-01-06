using UnityEngine;
using Unity.Netcode;

// Základní třída pro VŠECHNY typy útoků
public abstract class AttackLogic : ScriptableObject
{
    /// <summary>
    /// Metoda, kterou server zavolá k provedení útoku.
    /// </summary>
    /// <param name="attacker">NetworkObject hráče, který útočí</param>
    /// <param name="weaponManager">Manažer zbraní (pro volání ClientRpc)</param>
    /// <param name="firePoint">Bod, odkud letí projektily (relevantní jen pro některé útoky)</param>
    public abstract void ExecuteAttack(
        NetworkObject attacker, 
        WeaponManager weaponManager, 
        Transform firePoint,
        WeaponStats stats
    );
}