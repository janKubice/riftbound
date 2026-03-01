using UnityEngine;
using Unity.Netcode;

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

    public override void OnHit(Vector3 hitPosition, GameObject target, NetworkObject attacker, WeaponManager manager)
    {
        // Spawnování entit řídí výhradně Server
        if (!NetworkManager.Singleton.IsServer) return;

        if (MeteorPrefab == null)
        {
            Debug.LogWarning("[MeteorStrikeEffect] Chybí prefab meteoru!");
            return;
        }

        // Vypočítáme pozici spawnu vysoko nad místem původního zásahu
        Vector3 spawnPos = hitPosition + (Vector3.up * SpawnHeight);
        
        // Spawnujeme s natočením kolmo dolů (pokud má model orientaci po ose Z)
        GameObject meteorInst = Instantiate(MeteorPrefab, spawnPos, Quaternion.Euler(90f, 0f, 0f));

        // Inicializace logiky meteoru před samotným spawnem do sítě
        if (meteorInst.TryGetComponent(out MeteorProjectile meteorLogic))
        {
            Vector3 fallVelocity = Vector3.down * MeteorStats.ProjectileSpeed;
            meteorLogic.Initialize(attacker.NetworkObjectId, fallVelocity, MeteorStats);
        }

        // Zaregistrování objektu v síti
        if (meteorInst.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }
    }
}