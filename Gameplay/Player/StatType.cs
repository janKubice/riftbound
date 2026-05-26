public enum StatType
{
    // --- PŘEŽITÍ (VITALITY) ---
    MaxHealth,
    HealthRegen,
    Defense,            // Flat Armor
    DamageReduction,    // % Resistance
    Evasion,            // % Dodge chance
    KnockbackResistance,// % Stability

    // --- ZDROJE (RESOURCES) ---
    MaxStamina,
    StaminaRegen,
    StaminaCostReduction, // % snížení ceny sprintu/skoku
    MaxMana,
    ManaRegen,
    ManaCostReduction,

    // --- POHYB (MOBILITY) ---
    MoveSpeed,
    JumpHeight,
    JumpCount,
    
    // --- OFENZIVA (OFFENSE GLOBAL) ---
    DamageMultiplier,
    CritChance,
    CritMultiplier,
    AttackSpeed,        // Cooldown reduction
    LifeSteal,          // % Vampirism
    Thorns,             // Return damage

    // --- SPECIFICKÉ ÚTOKY (SKILL MODIFIERS) ---
    ProjectileCount,
    ProjectileSpeed,    // Rychlost letu (dosah pro SmartProjectile)
    ProjectilePierce,   // Kolik nepřátel střela projde
    AreaSize,           // AoE radius
    StatusDuration,     // Délka trvání efektů
    StatusPotency,      // Síla efektů

    // --- UTILITA (MISC) ---
    PickupRange,
    CharacterSize,
    Luck,               // Lepší loot / Crit roll
    ExperienceGain,      // % XP bonus

    SlamDamage
}

public static class StatTypeExtensions
{
    // Klíčové slovo 'this' znamená, že se metoda "přilepí" na StatType
    public static string GetDisplayName(this StatType statType)
    {
        return statType switch
        {
            // Přežití
            StatType.MaxHealth => "Max Health",
            StatType.HealthRegen => "Health Regeneration",
            StatType.DamageReduction => "Damage Reduction",
            StatType.KnockbackResistance => "Knockback Resistance",

            // Zdroje
            StatType.MaxStamina => "Max Stamina",
            StatType.StaminaRegen => "Stamina Regeneration",
            StatType.StaminaCostReduction => "Stamina Cost Reduction",
            StatType.MaxMana => "Max Mana",
            StatType.ManaRegen => "Mana Regeneration",
            StatType.ManaCostReduction => "Mana Cost Reduction",

            // Pohyb
            StatType.MoveSpeed => "Movement Speed",
            StatType.JumpHeight => "Jump Height",
            StatType.JumpCount => "Extra Jumps",

            // Ofenzíva
            StatType.DamageMultiplier => "Damage Multiplier",
            StatType.CritChance => "Critical Chance",
            StatType.CritMultiplier => "Critical Damage",
            StatType.AttackSpeed => "Attack Speed",
            StatType.LifeSteal => "Life Steal",
            
            // Specifické útoky
            StatType.ProjectileCount => "Projectile Count",
            StatType.ProjectileSpeed => "Projectile Speed",
            StatType.ProjectilePierce => "Projectile Pierce",
            StatType.AreaSize => "Area of Effect Size",
            StatType.StatusDuration => "Status Effect Duration",
            StatType.StatusPotency => "Status Effect Potency",

            // Utilita
            StatType.PickupRange => "Pickup Range",
            StatType.CharacterSize => "Character Size",
            StatType.ExperienceGain => "Experience Gain",
            StatType.SlamDamage => "Slam Damage",

            // Fallback (pokud zapomeneš nějaký přidat, vrátí původní název)
            _ => statType.ToString() 
        };
    }

    public static string GetColorHex(this StatType statType)
    {
        return statType switch
        {
            // Přežití (Vitality) - Zelené tóny
            StatType.MaxHealth or StatType.HealthRegen => "#2ECC71",
            StatType.Defense or StatType.DamageReduction => "#27AE60",
            StatType.KnockbackResistance or StatType.Evasion => "#27AE60",

            // Pohyb (Mobility) - Modré tóny
            StatType.MoveSpeed or StatType.JumpHeight or StatType.JumpCount => "#3498DB",

            // Ofenzíva (Offense) - Červené/Oranžové tóny
            StatType.DamageMultiplier or StatType.SlamDamage => "#E74C3C",
            StatType.CritChance or StatType.CritMultiplier => "#F39C12",
            StatType.AttackSpeed => "#E67E22",
            StatType.LifeSteal or StatType.Thorns => "#D35400",

            // Specifické útoky - Fialové tóny
            StatType.ProjectileCount or StatType.ProjectileSpeed or StatType.ProjectilePierce => "#9B59B6",
            StatType.AreaSize or StatType.StatusDuration or StatType.StatusPotency => "#8E44AD",

            // Zdroje - Žluté/Světle modré
            StatType.MaxMana or StatType.ManaRegen or StatType.ManaCostReduction => "#00FFFF",
            StatType.MaxStamina or StatType.StaminaRegen or StatType.StaminaCostReduction => "#F1C40F",

            // Výchozí barva (Misc)
            _ => "#FFFFFF"
        };
    }
}
