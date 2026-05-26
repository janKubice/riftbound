using UnityEngine;

[CreateAssetMenu(menuName = "Riftbound/Progression/StatUpgradeData")]
public class StatUpgradeData : ScriptableObject
{
    [Header("Identifikace")]
    public string UpgradeName; // Např. "Iron Skin"
    public StatType Type;
    public Sprite Icon;
    [TextArea] public string Description;

    [SerializeField] public bool inDemo = false;

    [Header("Ekonomika")]
    public int BaseCost = 100;           // Cena prvního levelu
    public float CostMultiplier = 1.5f;  // Jak moc se zdraží další level (100 -> 150 -> 225...)

    [Header("Hodnoty")]
    public float BaseValue = 0f;         // Základní hodnota
    public float ValuePerLevel = 10f;    // O kolik se zvedne stat za každý nákup
    public bool IsPercentage = false;    // Určuje, zda se jedná o % (např. 0.02 = 2%)

    [Header("Limity")]
    public int MaxLevel = 10;

    // Pomocná metoda pro výpočet ceny
    public int GetCost(int currentLevel)
    {
        return Mathf.RoundToInt(BaseCost * Mathf.Pow(CostMultiplier, currentLevel));
    }

    // Pomocná metoda pro výpočet celkové hodnoty bonusu
    public float GetTotalBonus(int currentLevel)
    {
        return currentLevel * ValuePerLevel;
    }

    public bool IsMaxLevel(int currentLevel)
    {
        return currentLevel >= MaxLevel;
    }

    // Zapouzdřené formátování hodnoty
    private string FormatValue(float value)
    {
        if (IsPercentage)
        {
            // Vynásobení 100 pro zobrazení (0.02 -> 2)
            // "0.##" zaručí zobrazení až 2 desetinných míst, pokud existují, jinak je vynechá.
            return $"{(value * 100f).ToString("0.##")}%";
        }
        else
        {
            return value.ToString("0.##");
        }
    }

    // Dynamický text pro UI (např. "10 -> 15" nebo "2% -> 4%")
    public string GetValuePreview(int currentLevel)
    {
        float currentVal = GetTotalBonus(currentLevel);

        if (IsMaxLevel(currentLevel))
        {
            return $"<color=orange>Total: +{FormatValue(currentVal)}</color>";
        }

        float nextVal = GetTotalBonus(currentLevel + 1);
        
        return $"{FormatValue(currentVal)} -> <color=#00FF00>+{FormatValue(nextVal)}</color>";
    }
}