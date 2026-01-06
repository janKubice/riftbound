using UnityEngine;

[System.Serializable]
public struct WeaponStats
{
    [Header("Base Combat")]
    public int Damage;
    public float Cooldown;
    public float Range; // Dosah pro melee, dolet pro ranged
    public float Knockback;
    public float AttackAngle;
    
    [Header("Criticals")]
    public float CritChance; // 0.0 až 1.0 (10% = 0.1)
    public float CritMultiplier; // 2.0 = 2x damage

    [Header("Projectiles (Ranged)")]
    public float ProjectileSpeed;
    public int ProjectileCount; // Kolik střel vyletí najednou (Shotgun/Wand)
    public float Spread; // Rozptyl ve stupních
    public int PierceCount; // Přes kolik nepřátel to projde (0 = zničí se o prvního)
    public float _fuseTime;

    [Header("Area of Effect")]
    public float ExplosionRadius; // Pro bomby

    [Header("Elemental")]
    public DamageType DamageType;
    public StatusEffectData Effect; // Hlavní efekt zbraně
}