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

        // --- OPRAVA SMĚRU STŘELBY ---
        // Místo spoléhání se na rotaci zbraně (která se nemusí hýbat nahoru/dolů)
        // si vypočítáme rotaci přímo k bodu, kam hráč míří.
        
        Quaternion attackRotation = firePoint.rotation; // Fallback (pro AI nebo chyby)

        if (attacker.TryGetComponent(out PlayerAiming aiming))
        {
            // Získáme bod v prostoru, kam hráč právě kouká (synchronizováno přes síť)
            Vector3 targetPoint = aiming.CurrentAimPoint;
            
            // Vypočítáme vektor od hlavně k tomuto bodu
            Vector3 directionToTarget = (targetPoint - firePoint.position).normalized;

            // Vytvoříme novou rotaci, která kouká přesně tam
            if (directionToTarget != Vector3.zero)
            {
                attackRotation = Quaternion.LookRotation(directionToTarget);
            }
        }

        // 2. Počet projektilů (Spread logic)
        int count = Mathf.Max(1, stats.ProjectileCount);
        float startAngle = -stats.Spread / 2f;
        float angleStep = count > 1 ? stats.Spread / (count - 1) : 0f;

        for (int i = 0; i < count; i++)
        {
            float currentAngle = startAngle + (angleStep * i);
            
            // Rotace výstřelu podle rozptylu (přičítáme k naší vypočítané attackRotation)
            // Pozor: Spread aplikujeme lokálně k rotaci (proto násobení zprava)
            Quaternion spreadRot = Quaternion.Euler(0, currentAngle, 0);
            Quaternion finalRot = attackRotation * spreadRot;

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
                
                // Inicializujeme s vypočítanou rotací
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