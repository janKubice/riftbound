using UnityEngine;
using Unity.Netcode;

[CreateAssetMenu(fileName = "SummonAttack", menuName = "Attacks/Summon Logic")]
public class SummonAttackLogic : AttackLogic
{
    [Header("Summon Settings")]
    [Tooltip("Prefab miniona. Musí mít NetworkObject a SummonedMinion komponentu.")]
    public GameObject MinionPrefab;

    [Tooltip("Maximální počet aktivních minionů na jednoho hráče.")]
    public int MaxMinionsPerPlayer = 3;

    [Tooltip("Jak daleko od hráče se minion objeví.")]
    public float SpawnRadius = 2f;

    public int ManaCost = 0;

    public override void ExecuteAttack(NetworkObject attacker, WeaponManager weaponManager, Transform firePoint, WeaponStats stats, int projectileCountBonus = 0)
    {
        if (MinionPrefab == null || !NetworkManager.Singleton.IsServer) return;

        // Kontrola many
        if (ManaCost > 0 && attacker.TryGetComponent(out PlayerAttributes attr))
        {
            if (attr.CurrentMana.Value < ManaCost) return;
            // attr.ConsumeManaServerRpc(ManaCost);
        }

        ulong ownerId = attacker.OwnerClientId;

        // Validace maximálního počtu (zabrání zahlcení sítě)
        int currentMinionCount = 0;
        SummonedMinion oldestMinion = null;

        for (int i = 0; i < SummonedMinion.ActiveMinions.Count; i++)
        {
            var minion = SummonedMinion.ActiveMinions[i];
            if (minion.OwnerId == ownerId)
            {
                currentMinionCount++;
                if (oldestMinion == null) oldestMinion = minion;
            }
        }

        // Pokud jsme na limitu, zničíme nejstaršího miniona (umožní to dynamickou rotaci)
        if (currentMinionCount >= MaxMinionsPerPlayer && oldestMinion != null)
        {
            oldestMinion.NetworkObject.Despawn(true);
        }

        // Výpočet pozice (náhodný bod v kruhu kolem hráče)
        Vector2 randomCircle = Random.insideUnitCircle.normalized * SpawnRadius;
        Vector3 spawnPos = attacker.transform.position + new Vector3(randomCircle.x, 0, randomCircle.y);

        // Spawn entity
        GameObject minionInstance = Instantiate(MinionPrefab, spawnPos, Quaternion.identity);
        if (minionInstance.TryGetComponent(out SummonedMinion summoned))
        {
            summoned.Initialize(ownerId);
            
            // Propojení statistik zbraně do miniona (volitelné úpravy)
            summoned.Damage = stats.Damage; 
        }

        // Spawn do sítě
        if (minionInstance.TryGetComponent(out NetworkObject netObj))
        {
            netObj.SpawnWithOwnership(ownerId, true);
        }
    }
}