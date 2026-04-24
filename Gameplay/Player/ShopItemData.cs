using UnityEngine;

[CreateAssetMenu(menuName = "Shop/Shop Item")]
public class ShopItemData : ScriptableObject
{
    [Header("Info")]
    public string ItemName;
    public Sprite Icon;
    public ItemRarity Rarity;

    [Header("Cena")]
    public int GoldCost;
    
    [Header("Efekt")]
    public HitEffect EffectPayload;
    public bool IsGlobalUpgrade;

    public Color GetRarityColor() => Rarity switch {
        ItemRarity.Common => Color.white,
        ItemRarity.Uncommon => Color.green,
        ItemRarity.Rare => new Color(0f, 0.5f, 1f), // Modrá
        ItemRarity.Epic => new Color(0.75f, 0f, 1f), // Fialová
        ItemRarity.Legendary => new Color(1f, 0.5f, 0f), // Oranžová
        _ => Color.white
    };
}