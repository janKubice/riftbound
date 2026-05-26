using UnityEngine;

[CreateAssetMenu(fileName = "NewRewardChoice", menuName = "Riftbound/Rewards/Reward Choice")]
public class RewardChoiceDefinition : ScriptableObject
{
    [Header("Identity")]
    public string RewardName;
    public Sprite Icon;

    [TextArea(2, 5)]
    public string Description;

    public RewardChoiceType Type;

    [Header("Rarity")]
    [Tooltip("Pokud je true, rarita se vyrolluje podle Lucku. Pokud false, použije se Fixed Rarity.")]
    public bool RollRarityAtOfferTime = true;

    public ItemRarity FixedRarity = ItemRarity.Common;

    [Header("Weight")]
    [Min(0f)]
    public float Weight = 1f;

    [Min(0f)]
    public float MinRunMinute = 0f;

    [Min(0f)]
    public float MaxRunMinute = 0f;

    [Header("Resource Reward")]
    [Tooltip("Základní množství pro XP/Gold/Heal. Finální hodnota = BaseAmount * rarity multiplier.")]
    public int BaseAmount = 25;

    [Header("Stat Upgrade Reward")]
    public StatUpgradeData StatUpgrade;

    [Tooltip("Pokud je 0, použije se ValuePerLevel ze StatUpgradeData.")]
    public float OverrideBaseStatValue = 0f;

    [Header("Weapon Effect Reward")]
    public HitEffect WeaponHitEffect;

    [Tooltip("Pokud je true, karta se nabídne jen když hráč drží zbraň.")]
    public bool RequiresEquippedWeapon = true;

    public bool IsAvailable(float runMinute, PlayerProgression progression, WeaponManager weaponManager)
    {
        if (Weight <= 0f)
            return false;

        if (runMinute < MinRunMinute)
            return false;

        if (MaxRunMinute > 0f && runMinute > MaxRunMinute)
            return false;

        switch (Type)
        {
            case RewardChoiceType.StatUpgrade:
                if (StatUpgrade == null || progression == null)
                    return false;

                int upgradeIndex = progression.GetUpgradeIndex(StatUpgrade);
                if (upgradeIndex < 0)
                    return false;

                int level = progression.GetUpgradeLevel(upgradeIndex);
                return !StatUpgrade.IsMaxLevel(level);

            case RewardChoiceType.WeaponHitEffect:
                if (WeaponHitEffect == null || weaponManager == null)
                    return false;

                if (RequiresEquippedWeapon && !weaponManager.HasWeaponEquipped)
                    return false;

                return true;
        }

        return true;
    }

    public ItemRarity GetRarity(float luck01)
    {
        if (!RollRarityAtOfferTime)
            return FixedRarity;

        return RollRarity(luck01);
    }

    private static ItemRarity RollRarity(float luck01)
    {
        luck01 = Mathf.Clamp01(luck01);

        float common = 60f * RewardRarityUtility.GetLuckWeightMultiplier(ItemRarity.Common, luck01);
        float uncommon = 25f * RewardRarityUtility.GetLuckWeightMultiplier(ItemRarity.Uncommon, luck01);
        float rare = 10f * RewardRarityUtility.GetLuckWeightMultiplier(ItemRarity.Rare, luck01);
        float epic = 4f * RewardRarityUtility.GetLuckWeightMultiplier(ItemRarity.Epic, luck01);
        float legendary = 1f * RewardRarityUtility.GetLuckWeightMultiplier(ItemRarity.Legendary, luck01);

        float totalWeight = common + uncommon + rare + epic + legendary;
        float roll = Random.Range(0f, totalWeight);

        float current = 0f;

        current += legendary;
        if (roll <= current) return ItemRarity.Legendary;

        current += epic;
        if (roll <= current) return ItemRarity.Epic;

        current += rare;
        if (roll <= current) return ItemRarity.Rare;

        current += uncommon;
        if (roll <= current) return ItemRarity.Uncommon;

        return ItemRarity.Common;
    }

    public int GetFinalAmount(ItemRarity rarity)
    {
        // Prevence vracení hodnoty 1 pro odměny, které ignorují Amount
        if (BaseAmount == 0)
            return 0;

        float multiplier = RewardRarityUtility.GetValueMultiplier(rarity);
        return Mathf.Max(1, Mathf.RoundToInt(BaseAmount * multiplier));
    }

    public float GetFinalStatValue(ItemRarity rarity)
    {
        if (StatUpgrade == null)
            return 0f;

        float baseValue = OverrideBaseStatValue > 0f
            ? OverrideBaseStatValue
            : StatUpgrade.ValuePerLevel;

        return baseValue * RewardRarityUtility.GetValueMultiplier(rarity);
    }

    public string BuildTitle(ItemRarity rarity)
    {
        return Type switch
        {
            RewardChoiceType.Gold => $"+{GetFinalAmount(rarity)} Gold",
            RewardChoiceType.XP => $"+{GetFinalAmount(rarity)} XP",
            RewardChoiceType.Heal => $"Heal +{GetFinalAmount(rarity)}",
            RewardChoiceType.StatUpgrade => StatUpgrade != null
                ? $"{StatUpgrade.UpgradeName} +1"
                : RewardName,
            RewardChoiceType.WeaponHitEffect => WeaponHitEffect != null
                ? WeaponHitEffect.EffectName
                : RewardName,
            _ => RewardName
        };
    }

    public string BuildDescription(ItemRarity rarity)
    {
        switch (Type)
        {
            case RewardChoiceType.Gold:
                return $"Gain {GetFinalAmount(rarity)} gold.";

            case RewardChoiceType.XP:
                return $"Gain {GetFinalAmount(rarity)} XP.";

            case RewardChoiceType.Heal:
                return $"Restore {GetFinalAmount(rarity)} health.";

            case RewardChoiceType.StatUpgrade:
                if (StatUpgrade == null)
                    return Description;

                return $"{StatUpgrade.Description}\n\nValue this level: +{GetFinalStatValue(rarity):0.##}";

            case RewardChoiceType.WeaponHitEffect:
                if (WeaponHitEffect == null)
                    return Description;

                return WeaponHitEffect.GetDescription();

            default:
                return Description;
        }
    }
}