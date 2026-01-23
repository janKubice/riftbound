using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewLootTable", menuName = "Loot/Loot Table")]
public class LootTable : ScriptableObject
{
    public List<LootEntry> Entries;

    // Logika pro výběr náhodného předmětu podle váhy
    public bool TryGetLoot(out LootEntry result, out int amount)
    {
        result = default;
        amount = 0;

        if (Entries == null || Entries.Count == 0) return false;

        int totalWeight = 0;
        foreach (var entry in Entries) totalWeight += entry.DropChanceWeight;

        int randomValue = UnityEngine.Random.Range(0, totalWeight);
        int currentWeight = 0;

        foreach (var entry in Entries)
        {
            currentWeight += entry.DropChanceWeight;
            if (randomValue < currentWeight)
            {
                result = entry;
                amount = UnityEngine.Random.Range(entry.MinAmount, entry.MaxAmount + 1);
                return true;
            }
        }
        return false;
    }
}