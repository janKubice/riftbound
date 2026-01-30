using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "ChainLightning", menuName = "Attacks/Chain Lightning Logic")]
public class ChainLightningLogic : AttackLogic
{
    [Header("Settings")]
    public float MaxCastDistance = 30f;
    public LayerMask EnemyLayer;
    public LayerMask ObstacleLayer;
    public int ManaCost = 10;

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats)
    {
        // 1. Mana Check
        if (ManaCost > 0 && attacker.TryGetComponent(out PlayerAttributes attr))
        {
            if (attr.CurrentMana.Value < ManaCost) return;
            //attr.ConsumeManaServerRpc(ManaCost);
        }

        List<Vector3> chainPositions = new List<Vector3>();
        List<GameObject> hitTargets = new List<GameObject>();

        // Startovní bod
        Vector3 currentPos = firePoint.position;
        chainPositions.Add(currentPos);

        // 2. První zásah
        Vector3 aimDir = firePoint.forward;
        if (attacker.TryGetComponent(out PlayerAiming aiming))
        {
            aimDir = (aiming.CurrentAimPoint - currentPos).normalized;
        }

        RaycastHit firstHit;
        GameObject currentTargetObj = null;
        float firstRange = stats.Range > 0 ? stats.Range : MaxCastDistance;
        int combinedMask = EnemyLayer | ObstacleLayer;

        if (Physics.Raycast(currentPos, aimDir, out firstHit, firstRange, combinedMask))
        {
            chainPositions.Add(firstHit.point);
            currentPos = firstHit.point;

            // Kontrola masky pomocí bitových operací
            if (((1 << firstHit.collider.gameObject.layer) & EnemyLayer) != 0)
            {
                currentTargetObj = firstHit.collider.gameObject;
                ApplyDamage(currentTargetObj, stats, attacker.OwnerClientId);
                hitTargets.Add(currentTargetObj);
                weaponManager.SpawnMeleeImpact(firstHit.point);
            }
        }
        else
        {
            chainPositions.Add(currentPos + aimDir * firstRange);
            weaponManager.SpawnChainLightningServerRpc(chainPositions.ToArray());
            return; 
        }

        // 3. Řetězení
        if (currentTargetObj != null)
        {
            int jumps = stats.ProjectileCount > 0 ? stats.ProjectileCount : 3;
            float bounceRange = 10f; 

            for (int i = 0; i < jumps; i++)
            {
                GameObject nextTarget = FindNextTarget(currentPos, bounceRange, hitTargets);
                
                if (nextTarget != null)
                {
                    // --- OPRAVA ZDE ---
                    // Získáme collider pro výpočet ClosestPoint
                    Vector3 targetHitPos = nextTarget.transform.position; // Fallback na střed
                    
                    if (nextTarget.TryGetComponent(out Collider targetCol))
                    {
                        targetHitPos = targetCol.ClosestPoint(currentPos);
                    }
                    // ------------------

                    Vector3 directionToNext = (targetHitPos - currentPos).normalized;
                    float distToNext = Vector3.Distance(currentPos, targetHitPos);
                    
                    // Raycast check proti zdi
                    if (!Physics.Raycast(currentPos, directionToNext, distToNext, ObstacleLayer))
                    {
                        currentPos = targetHitPos;
                        chainPositions.Add(currentPos);
                        
                        ApplyDamage(nextTarget, stats, attacker.OwnerClientId);
                        hitTargets.Add(nextTarget);
                        
                        weaponManager.SpawnMeleeImpact(currentPos);
                    }
                    else
                    {
                        break; // Zeď
                    }
                }
                else
                {
                    break; 
                }
            }
        }

        // 4. Odeslání vizuálu
        weaponManager.SpawnChainLightningServerRpc(chainPositions.ToArray());
    }

    private GameObject FindNextTarget(Vector3 center, float radius, List<GameObject> ignoreList)
    {
        Collider[] hits = Physics.OverlapSphere(center, radius, EnemyLayer);
        GameObject bestTarget = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            GameObject candidate = hit.gameObject;
            if (ignoreList.Contains(candidate)) continue;

            float d = Vector3.Distance(center, candidate.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = candidate;
            }
        }
        return bestTarget;
    }

    private void ApplyDamage(GameObject target, WeaponStats stats, ulong attackerId)
    {
        if (target.TryGetComponent(out EnemyHealth enemy))
        {
            enemy.TakeDamage(stats.Damage, attackerId);
            if (stats.Effect != null && stats.Effect.Type != StatusEffectType.None)
            {
                enemy.ApplyStatusEffect(stats.Effect);
            }
        }
    }
}