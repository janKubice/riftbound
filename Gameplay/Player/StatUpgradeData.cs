using UnityEngine;

[CreateAssetMenu(menuName = "Riftbound/Progression/Stat Upgrade Data")]
public class StatUpgradeData : ScriptableObject
{
    [Header("Identifikace")]
    public string UpgradeName; // Např. "Iron Skin"
    public StatType Type;
    public Sprite Icon;
    [TextArea] public string Description;

    [Header("Ekonomika")]
    public int BaseCost = 100;           // Cena prvního levelu
    public float CostMultiplier = 1.5f;  // Jak moc se zdraží další level (100 -> 150 -> 225...)
    
    [Header("Hodnoty")]
    public float BaseValue = 0f;         // Základní hodnota (pokud chceme přepsat default)
    public float ValuePerLevel = 10f;    // O kolik se zvedne stat za každý nákup
    
    // Pomocná metoda pro výpočet ceny
    public int GetCost(int currentLevel)
    {
        // Vzorec: Base * (Multiplier ^ Level)
        return Mathf.RoundToInt(BaseCost * Mathf.Pow(CostMultiplier, currentLevel));
    }

    // Pomocná metoda pro výpočet celkové hodnoty bonusu
    public float GetTotalBonus(int currentLevel)
    {
        return currentLevel * ValuePerLevel;
    }
}