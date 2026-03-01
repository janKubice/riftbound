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

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats, int projectileCountBonus = 0)
    {
        if (ManaCost > 0 && attacker.TryGetComponent(out PlayerAttributes attr))
        {
            if (attr.CurrentMana.Value < ManaCost) return;
        }

        Vector3 currentPos = firePoint.position;
        Vector3 baseDir = firePoint.forward;

        if (attacker.TryGetComponent(out PlayerAiming aiming))
        {
            baseDir = (aiming.CurrentAimPoint - currentPos).normalized;
        }

        // --- MULTISHOT LOGIKA ---
        int boltCount = stats.ProjectileCount + projectileCountBonus; // Kolik blesků vyletí naráz
        float startAngle = -stats.Spread / 2f;
        float angleStep = boltCount > 1 ? stats.Spread / (boltCount - 1) : 0f;

        float firstRange = stats.Range > 0 ? stats.Range : MaxCastDistance;
        int jumps = stats.PierceCount > 0 ? stats.PierceCount : 3; // Přeskoky bere z průraznosti!
        int combinedMask = EnemyLayer | ObstacleLayer;

        for (int i = 0; i < boltCount; i++)
        {
            List<Vector3> chainPositions = new List<Vector3>();
            List<GameObject> hitTargets = new List<GameObject>();
            chainPositions.Add(currentPos);

            Vector3 finalDir = Quaternion.Euler(0, startAngle + (angleStep * i), 0) * baseDir;
            GameObject currentTargetObj = null;
            Vector3 jumpStartPos = currentPos;

            // 1. První zásah tohoto blesku
            if (Physics.Raycast(currentPos, finalDir, out RaycastHit firstHit, firstRange, combinedMask))
            {
                chainPositions.Add(firstHit.point);
                jumpStartPos = firstHit.point;

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
                chainPositions.Add(currentPos + finalDir * firstRange);
                weaponManager.SpawnChainLightningServerRpc(chainPositions.ToArray());
                continue; // Tento blesk nic netrefil, jdeme na další
            }

            // 2. Řetězení (Přeskoky)
            if (currentTargetObj != null)
            {
                float bounceRange = 10f;
                for (int j = 0; j < jumps; j++)
                {
                    GameObject nextTarget = FindNextTarget(jumpStartPos, bounceRange, hitTargets);
                    if (nextTarget != null)
                    {
                        Vector3 targetHitPos = nextTarget.transform.position;
                        if (nextTarget.TryGetComponent(out Collider targetCol))
                        {
                            targetHitPos = targetCol.ClosestPoint(jumpStartPos);
                        }

                        Vector3 directionToNext = (targetHitPos - jumpStartPos).normalized;
                        float distToNext = Vector3.Distance(jumpStartPos, targetHitPos);

                        if (!Physics.Raycast(jumpStartPos, directionToNext, distToNext, ObstacleLayer))
                        {
                            jumpStartPos = targetHitPos;
                            chainPositions.Add(jumpStartPos);

                            ApplyDamage(nextTarget, stats, attacker.OwnerClientId);
                            hitTargets.Add(nextTarget);
                            weaponManager.SpawnMeleeImpact(jumpStartPos);
                        }
                        else break; // Zeď
                    }
                    else break; // Žádný další cíl
                }
            }

            // Odeslání vizuálu pro tento jeden blesk
            weaponManager.SpawnChainLightningServerRpc(chainPositions.ToArray());
        }
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