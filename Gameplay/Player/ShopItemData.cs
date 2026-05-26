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

    [Header("Demo")]
    public bool InDemo = true;

    /// <summary>
    /// Vypočítá dynamickou cenu kombinací plošné daně a penalizace za duplikáty.
    /// </summary>
    public int GetDynamicPrice(int totalEffectsCount, int duplicateCount)
    {
        if (IsGlobalUpgrade) return GoldCost;

        // 1. Plošná daň za každý obsazený slot (škálovaná podle rarity kupovaného předmětu)
        int slotTaxBase = Rarity switch
        {
            ItemRarity.Common => 150,
            ItemRarity.Uncommon => 400,
            ItemRarity.Rare => 1000,
            ItemRarity.Epic => 2500,
            ItemRarity.Legendary => 6000,
            _ => 500
        };

        // Exponenciální růst: Mathf.Pow(totalEffectsCount, 1.2f)
        // 0 efektů = 0 * base
        // 1 efekt  = 1.00 * base
        // 3 efekty = 3.73 * base
        // 6 efektů = 8.58 * base
        // Tímto se late-game nákupy přirozeně zastropují, aniž by se zničila early-game ekonomika.
        float taxCurve = Mathf.Pow(totalEffectsCount, 1.18f);
        int slotTax = Mathf.RoundToInt(taxCurve * slotTaxBase);

        // 2. Penalizace za duplikáty (brání spamování např. "Split 3")
        // 1.0f (žádný duplikát) -> 1.5f (1 duplikát) -> 2.25f (2 duplikáty) atd.
        float duplicateMultiplier = 1.5f;
        float duplicateInflation = Mathf.Pow(duplicateMultiplier, duplicateCount);

        // 3. Výpočet celkové ceny
        return Mathf.RoundToInt((GoldCost + slotTax) * duplicateInflation);
    }

    public Color GetRarityColor() => Rarity switch
    {
        ItemRarity.Common => Color.white,
        ItemRarity.Uncommon => Color.green,
        ItemRarity.Rare => new Color(0f, 0.5f, 1f),
        ItemRarity.Epic => new Color(0.75f, 0f, 1f),
        ItemRarity.Legendary => new Color(1f, 0.5f, 0f),
        _ => Color.white
    };
}