using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "AreaAttack", menuName = "Attacks/Area Logic")]
public class AreaAttackLogic : AttackLogic
{
    [Header("Area Settings")]
    public float ThrowForce = 10f;
    public float ThrowUpwardForce = 2f;

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats)
    {
        // Kontrola
        if (weaponManager.CurrentWeaponData.ProjectilePrefab == null)
        {
            Debug.LogError("Chybí ProjectilePrefab (Bomba) ve WeaponData!");
            return;
        }

        // 1. Instanciace
        GameObject bombGO = Instantiate(
            weaponManager.CurrentWeaponData.ProjectilePrefab,
            firePoint.position,
            firePoint.rotation
        );

        // 2. Spawn na síti
        var netObj = bombGO.GetComponent<NetworkObject>();
        netObj.Spawn(true);

        // 3. Inicializace a Hod
        if (bombGO.TryGetComponent(out ExplosiveProjectile bombScript))
        {
            // Vypočítáme vektor hodu (dopředu + trochu nahoru)
            Vector3 throwDir = (attacker.transform.forward * ThrowForce) + (Vector3.up * ThrowUpwardForce);
            
            bombScript.Initialize(attacker.OwnerClientId, throwDir, stats);
        }

        // 4. Cooldown report
        weaponManager.OnWeaponFiredServerRpc(stats.Cooldown);
    }
}