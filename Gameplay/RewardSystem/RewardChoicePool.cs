using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewRewardChoicePool", menuName = "Riftbound/Rewards/Reward Choice Pool")]
public class RewardChoicePool : ScriptableObject
{
    public List<RewardChoiceDefinition> Rewards = new List<RewardChoiceDefinition>();

    public List<GeneratedRewardChoice> GenerateChoices(
        PlayerProgression progression,
        WeaponManager weaponManager,
        float runMinute,
        int count)
    {
        List<GeneratedRewardChoice> results = new List<GeneratedRewardChoice>();
        HashSet<int> usedIndices = new HashSet<int>();

        // Zde záleží na externí logice statů, ale Clamp01 je bezpečný fallback
        float luck = progression != null
            ? Mathf.Clamp01(progression.GetStatBonus(StatType.Luck))
            : 0f;

        int safety = 0;

        while (results.Count < count && safety < 100)
        {
            safety++;

            int index = PickRewardIndex(progression, weaponManager, runMinute, luck, usedIndices);

            // Pokud pool nemá další platné karty, ukončíme hledání
            if (index < 0)
                break;

            RewardChoiceDefinition def = Rewards[index];
            ItemRarity rarity = def.GetRarity(luck);

            results.Add(new GeneratedRewardChoice
            {
                DefinitionIndex = index,
                Rarity = rarity,
                Amount = def.GetFinalAmount(rarity),
                StatValue = def.GetFinalStatValue(rarity)
            });

            usedIndices.Add(index);
        }

        return results;
    }

    private int PickRewardIndex(
        PlayerProgression progression,
        WeaponManager weaponManager,
        float runMinute,
        float luck,
        HashSet<int> excluded)
    {
        float totalWeight = 0f;

        for (int i = 0; i < Rewards.Count; i++)
        {
            if (excluded.Contains(i) || Rewards[i] == null || !Rewards[i].IsAvailable(runMinute, progression, weaponManager))
                continue;

            totalWeight += GetCardWeight(Rewards[i], luck);
        }

        if (totalWeight <= 0f)
            return -1;

        float roll = Random.Range(0f, totalWeight);
        float current = 0f;
        int lastValidIndex = -1;

        for (int i = 0; i < Rewards.Count; i++)
        {
            if (excluded.Contains(i) || Rewards[i] == null || !Rewards[i].IsAvailable(runMinute, progression, weaponManager))
                continue;

            lastValidIndex = i;
            current += GetCardWeight(Rewards[i], luck);

            if (roll <= current)
                return i;
        }

        // Záchrana pro floating-point inaccuracy (pokud roll přesáhl current o mikroskopickou hodnotu)
        return lastValidIndex;
    }

    private float GetCardWeight(RewardChoiceDefinition def, float luck)
    {
        // Karty se statickou raritou ovlivníme luckem (např. víc legendárek v poolu)
        if (!def.RollRarityAtOfferTime)
        {
            return def.Weight * RewardRarityUtility.GetLuckWeightMultiplier(def.FixedRarity, luck);
        }

        // Dynamické karty jsou neutrální k lucku během *výběru karty do nabídky*. 
        // Jejich zhodnocení proběhne až při volání GetRarity()
        return def.Weight;
    }

    public RewardChoiceDefinition GetDefinition(int index)
    {
        if (index < 0 || index >= Rewards.Count) return null;
        return Rewards[index];
    }
}

public struct GeneratedRewardChoice
{
    public int DefinitionIndex;
    public ItemRarity Rarity;
    public int Amount;
    public float StatValue;
}