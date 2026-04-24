using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic; // Přidáno

[CreateAssetMenu(menuName = "Effects/Meteor Strike")]
public class MeteorStrikeEffect : HitEffect
{
    [Header("Meteor Settings")]
    [Tooltip("Prefab meteoru, který obsahuje komponentu MeteorProjectile a NetworkObject.")]
    public GameObject MeteorPrefab;

    [Tooltip("Jak vysoko nad místem dopadu se má meteor objevit.")]
    public float SpawnHeight = 40f;

    [Header("Meteor Combat Stats")]
    [Tooltip("Statistiky specifické pro meteor (poškození, rychlost pádu, poloměr exploze, síla odhození).")]
    public WeaponStats MeteorStats;

    // Přidán parametr List<HitEffect> remainingPayload
    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager, List<HitEffect> remainingPayload)
    {
        if (!NetworkManager.Singleton.IsServer) return;

        if (MeteorPrefab == null)
        {
            Debug.LogWarning("[MeteorStrikeEffect] Chybí prefab meteoru!");
            return;
        }

        Vector3 spawnPos = hitPosition + (Vector3.up * SpawnHeight);

        GameObject meteorInst = Instantiate(MeteorPrefab, spawnPos, Quaternion.Euler(90f, 0f, 0f));

        if (meteorInst.TryGetComponent(out MeteorProjectile meteorLogic))
        {
            Vector3 fallVelocity = Vector3.down * MeteorStats.ProjectileSpeed;
            // MeteorProjectile nemá logiku pro payload, předáváme mu jen jeho staty
            meteorLogic.Initialize(attacker.NetworkObjectId, fallVelocity, MeteorStats);
        }

        if (meteorInst.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }
    }

    public override string GetDescription()
    {
        return $"<color=#FF4500><b>Meteor Strike:</b></color> Summons a meteor from <color=white>{SpawnHeight}m</color> height, " +
               $"dealing <color=#FF4444>{MeteorStats.Damage} DMG</color> in a <color=white>{MeteorStats.ExplosionRadius}m</color> area.";
    }
}