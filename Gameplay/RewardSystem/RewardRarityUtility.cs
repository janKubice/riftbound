using UnityEngine;

public static class RewardRarityUtility
{
    public static float GetValueMultiplier(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => 1.0f,
            ItemRarity.Uncommon => 1.5f,
            ItemRarity.Rare => 2.25f,
            ItemRarity.Epic => 3.5f,
            ItemRarity.Legendary => 5.0f,
            _ => 1.0f
        };
    }

    public static Color GetColor(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => Color.white,
            ItemRarity.Uncommon => Color.green,
            ItemRarity.Rare => new Color(0f, 0.5f, 1f),
            ItemRarity.Epic => new Color(0.75f, 0f, 1f),
            ItemRarity.Legendary => new Color(1f, 0.5f, 0f),
            _ => Color.white
        };
    }

    public static string GetLabel(ItemRarity rarity)
    {
        return rarity switch
        {
            ItemRarity.Common => "Common",
            ItemRarity.Uncommon => "Uncommon",
            ItemRarity.Rare => "Rare",
            ItemRarity.Epic => "Epic",
            ItemRarity.Legendary => "Legendary",
            _ => rarity.ToString()
        };
    }

    public static float GetLuckWeightMultiplier(ItemRarity rarity, float luck01)
    {
        luck01 = Mathf.Clamp01(luck01);

        return rarity switch
        {
            ItemRarity.Common => Mathf.Lerp(1.0f, 0.45f, luck01),
            ItemRarity.Uncommon => Mathf.Lerp(1.0f, 1.15f, luck01),
            ItemRarity.Rare => Mathf.Lerp(1.0f, 1.8f, luck01),
            ItemRarity.Epic => Mathf.Lerp(1.0f, 3.0f, luck01),
            ItemRarity.Legendary => Mathf.Lerp(1.0f, 5.0f, luck01),
            _ => 1.0f
        };
    }
}