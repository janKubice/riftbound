using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "Effects/Spawn Proc (Dynamic)")]
public class SpawnProcEffect : HitEffect
{
    [Header("Co se má spawnout")]
    public GameObject ProjectilePrefab;
    
    [Header("Staty pro nový projektil")]
    public float Speed = 20f;
    public float Range = 30f;

    [Header("Logika Dědičnosti")]
    [Tooltip("Pokud je TRUE, nový projektil zdědí všechny efekty, které má zbraň právě teď.")]
    public bool InheritWeaponEffects = true;

    [Tooltip("Zde můžeš přidat efekty, které má JENOM toto kouzlo navíc (např. vždy exploduje).")]
    public List<HitEffect> BasePayload = new List<HitEffect>();

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager)
    {
        // 1. Spawn
        Vector3 spawnPos = hitPosition + (Vector3.up * 1f); 
        GameObject newProjGO = Instantiate(ProjectilePrefab, spawnPos, Quaternion.identity);
        
        if (newProjGO.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }

        // 2. Příprava Payloadu (Toho batohu s efekty)
        List<HitEffect> finalPayload = new List<HitEffect>();

        // A) Přidáme základní efekty tohoto kouzla (pokud nějaké má natvrdo)
        if (BasePayload != null) finalPayload.AddRange(BasePayload);

        // B) MAGIE: Dědičnost z Runtime Statistik
        // Pokud chceme dědit, vezmeme efekty, které má zbraň PRÁVĚ TEĎ
        if (InheritWeaponEffects && manager != null)
        {
            // manager.CurrentRuntimeStats je to, co se mění během hry
            if (manager.CurrentRuntimeStats.OnHitEffects != null)
            {
                // Pozor: Musíme zabránit nekonečné smyčce!
                // Pokud zbraň má efekt "SpawnProcEffect" (sebe sama), nesmíme ho předat dál, 
                // jinak by raketa spawnovala raketu, která spawnuje raketu...
                
                foreach (var effect in manager.CurrentRuntimeStats.OnHitEffects)
                {
                    // Přidáme všechno KROMĚ spawnovacích efektů (nebo specificky sebe)
                    // Záleží na designu. Pokud chceš "Cluster Bomb", tak to povolíš.
                    // Pro teď pro jistotu vynecháme "SpawnProcEffect", aby se to nezacyklilo.
                    if (effect is SpawnProcEffect) continue; 
                    
                    finalPayload.Add(effect);
                }
            }
        }

        // 3. Inicializace projektilu s namíchaným payloadem
        if (newProjGO.TryGetComponent(out SmartProjectile smartProj))
        {
            WeaponStats procStats = new WeaponStats();
            procStats.ProjectileSpeed = Speed;
            procStats.Range = Range;
            procStats.Damage = 0; 
            
            // Předáme vygenerovaný seznam
            smartProj.Initialize(attacker, Vector3.up, procStats, finalPayload); 
        }
    }
}