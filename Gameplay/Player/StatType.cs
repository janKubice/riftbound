public enum StatType
{
    // Přežití
    MaxHealth,
    HealthRegen,
    MaxStamina,
    StaminaRegen,
    MaxMana,
    ManaRegen,
    
    // Pohyb
    MoveSpeed,
    JumpHeight,
    JumpCount, // Double jump, triple jump...
    
    // Útok (Global)
    DamageMultiplier, // % bonus ke všemu dmg
    CritChance,
    CritMultiplier,
    AttackSpeed, // Cooldown reduction
    
    // Útok (Specifické)
    ProjectileCount, // Multishot
    ProjectileSize,  // Větší hitboxy
    AreaSize,        // Větší exploze/dosah meče
    
    // Utility
    PickupRange,     // Dosah sbírání XP
    CharacterSize,   // Změna velikosti postavy (Bonus HP, ale větší hitbox)
    Luck             // Lepší dropy (do budoucna)
}