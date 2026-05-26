using UnityEngine;

public enum EnemySpellElement
{
    Neutral,
    Fire,
    Frost,
    Lightning,
    Void,
    Nature,
    Arcane
}

public enum EnemyTelegraphShape
{
    Circle,
    Rectangle
}

[CreateAssetMenu(fileName = "NewEnemySpell", menuName = "AI/Enemy Spell Definition")]
public class EnemySpellDefinition : ScriptableObject
{
    [Header("Identity")]
    public string SpellName = "Fire Orb";
    public EnemySpellElement Element = EnemySpellElement.Fire;
    public Color SpellColor = new Color(1f, 0.45f, 0.1f);

    [Header("Cast")]
    [Min(0.1f)] public float Cooldown = 3.0f;
    [Min(0.05f)] public float TelegraphDuration = 1.0f;
    [Min(0.5f)] public float CastRange = 12f;

    [Tooltip("Jak silně má spell predikovat pohyb hráče.")]
    [Range(0f, 2f)] public float PredictionFactor = 0.75f;

    [Tooltip("Náhodná odchylka cílového bodu.")]
    [Min(0f)] public float ScatterRadius = 1.5f;

    [Header("Projectile")]
    public bool SpawnProjectile = true;
    public GameObject ProjectilePrefab;
    public WeaponStats ProjectileStats;

    [Header("Ground Zone")]
    public bool SpawnGroundZone = false;
    public GameObject GroundZonePrefab;
    public StatusEffectData ZoneStatusEffect;
    [Range(0f, 1f)]
    public float ZoneStatusApplyChance = 1f;

    [Tooltip("Telegraph radius. Pokud je 0, použije se ZoneRadius.")]
    [Min(0f)] public float TelegraphRadius = 0f;

    [Min(0.25f)] public float ZoneRadius = 2.5f;
    [Min(0.25f)] public float ZoneLifetime = 4f;
    [Min(0)] public int ZoneDamagePerTick = 6;
    [Min(0.05f)] public float ZoneTickInterval = 0.5f;

    [Header("Telegraph Shape")]
    public EnemyTelegraphShape TelegraphShape = EnemyTelegraphShape.Circle;
    public Vector2 TelegraphRectSize = new Vector2(2f, 10f);

    [Header("Impact")]
    public GameObject CastReleaseVFX;
    public GameObject GroundImpactVFX;

    public float GetTelegraphRadius()
    {
        if (TelegraphRadius > 0f)
            return TelegraphRadius;

        return ZoneRadius;
    }
}