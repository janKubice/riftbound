using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Ricochet Effect")]
public class RicochetEffect : HitEffect
{
    [Header("Dynamic Ricochet Settings")]
    [Tooltip("Pokud je TRUE, pokusí se zkopírovat projektil ze zbraně, kterou hráč drží.")]
    public bool UseCurrentWeaponProjectile = true;

    [Tooltip("Záložní prefab, pokud zbraň nemá vlastní projektil, nebo pokud je dynamické kopírování vypnuté.")]
    public GameObject FallbackProjectilePrefab;
    
    [Header("Stats")]
    [Tooltip("Kolikrát se může tento odraz ještě odrazit?")]
    public int MaxBounces = 2;
    
    [Tooltip("Jak daleko může hledat další cíl?")]
    public float SearchRadius = 15f;
    
    [Tooltip("Rychlost odraženého projektilu.")]
    public float BounceSpeed = 20f;
    
    [Tooltip("Procento poškození z původní zbraně (1.0 = 100%, 0.5 = 50%).")]
    public float DamageMultiplier = 0.8f;

    [Tooltip("Vrstvy nepřátel")]
    public LayerMask EnemyLayer;

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        // 1. Zjištění, jaký prefab použít (DYNAMICKY!)
        GameObject prefabToSpawn = null;
        
        if (UseCurrentWeaponProjectile && manager.CurrentWeaponData != null)
        {
            // Vezme projektil ze zbraně (Fireball pro hůlku, "Létající kladivo" pro Mjolnir)
            prefabToSpawn = manager.CurrentWeaponData.ProjectilePrefab;
        }

        // Pokud dynamika selže nebo není zapnutá, použijeme zálohu
        if (prefabToSpawn == null)
        {
            prefabToSpawn = FallbackProjectilePrefab;
        }

        if (prefabToSpawn == null) return; // Failsafe (Nemáme co spawnout)

        // 2. Najdeme nejbližšího dalšího nepřítele
        GameObject nextTarget = FindNextTarget(hitPosition, target);
        if (nextTarget == null) return; // Není kam se odrazit

        // 3. Směr k novému cíli
        Vector3 aimPoint = nextTarget.transform.position + Vector3.up * 1f;
        Vector3 direction = (aimPoint - hitPosition).normalized;

        // 4. Spawnování odraženého projektilu
        GameObject newProjGO = Instantiate(prefabToSpawn, hitPosition + Vector3.up * 1f, Quaternion.LookRotation(direction));
        
        if (newProjGO.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }

        if (newProjGO.TryGetComponent(out SmartProjectile smartProj))
        {
            // 5. Upravíme staty pro odraz
            WeaponStats bounceStats = new WeaponStats();
            bounceStats.Damage = Mathf.RoundToInt(manager.CurrentRuntimeStats.Damage * DamageMultiplier);
            bounceStats.ProjectileSpeed = BounceSpeed;
            bounceStats.Range = SearchRadius * 2f;

            // 6. Zkopírujeme Payload (Batoh), ALE SNÍŽÍME POČET ODRAZŮ
            List<HitEffect> newPayload = new List<HitEffect>();
            if (manager.CurrentRuntimeStats.OnHitEffects != null)
            {
                foreach (var effect in manager.CurrentRuntimeStats.OnHitEffects)
                {
                    if (effect is RicochetEffect currentRicochet)
                    {
                        if (MaxBounces > 0)
                        {
                            RicochetEffect downgradedRicochet = Instantiate(currentRicochet);
                            downgradedRicochet.MaxBounces = MaxBounces - 1;
                            newPayload.Add(downgradedRicochet);
                        }
                    }
                    else
                    {
                        // VŠECHNY OSTATNÍ EFEKTY (Blesk, Meteor, Jed) SE PŘEDÁVAJÍ DÁL!
                        newPayload.Add(effect);
                    }
                }
            }

            smartProj.Initialize(attacker, direction, bounceStats, newPayload);
        }
    }

    private GameObject FindNextTarget(Vector3 center, GameObject hitTarget)
    {
        Collider[] hits = Physics.OverlapSphere(center, SearchRadius, EnemyLayer);
        GameObject bestTarget = null;
        float closestDist = float.MaxValue;

        foreach (var hit in hits)
        {
            if (hit.gameObject == hitTarget || hit.transform.root.gameObject == hitTarget.transform.root.gameObject) 
                continue;

            float d = Vector3.Distance(center, hit.transform.position);
            if (d < closestDist)
            {
                closestDist = d;
                bestTarget = hit.gameObject;
            }
        }
        return bestTarget;
    }
}