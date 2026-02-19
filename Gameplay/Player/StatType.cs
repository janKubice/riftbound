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
    ExperienceGain      // % XP bonus
}