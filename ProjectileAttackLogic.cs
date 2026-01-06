using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "ProjectileAttack", menuName = "Attacks/Projectile Logic")]
public class ProjectileAttackLogic : AttackLogic
{
    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats)
    {
        // 1. Validace
        if (weaponManager.CurrentWeaponData == null || weaponManager.CurrentWeaponData.ProjectilePrefab == null)
        {
            Debug.LogError("[ProjectileAttack] Chybí ProjectilePrefab ve WeaponData!");
            return;
        }

        // 2. Počet projektilů (pro Brokovnice/Spread)
        // Pokud je ProjectileCount 1, Spread se ignoruje
        // Pokud je > 1, rozpočítáme úhel
        
        int count = Mathf.Max(1, stats.ProjectileCount);
        float startAngle = -stats.Spread / 2f;
        float angleStep = count > 1 ? stats.Spread / (count - 1) : 0f;

        for (int i = 0; i < count; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            
            // Rotace výstřelu podle rozptylu
            Quaternion spreadRot = Quaternion.Euler(0, currentAngle, 0);
            Quaternion finalRot = firePoint.rotation * spreadRot;

            // 3. Instanciace
            GameObject projGO = Instantiate(
                weaponManager.CurrentWeaponData.ProjectilePrefab,
                firePoint.position,
                finalRot
            );

            // 4. Inicializace SmartProjectile
            if (projGO.TryGetComponent(out SmartProjectile smartProj))
            {
                smartProj.GetComponent<NetworkObject>().Spawn(true);
                
                // Inicializujeme s aktuálními staty (včetně Pierce, Speed, Damage)
                smartProj.Initialize(attacker.OwnerClientId, finalRot * Vector3.forward, stats);
            }
            else
            {
                Debug.LogError("ProjectilePrefab nemá komponentu SmartProjectile!");
                projGO.NetDestroy();
            }
        }

        // 5. Zpětná vazba
        weaponManager.OnWeaponFiredServerRpc(stats.Cooldown);
    }
} 