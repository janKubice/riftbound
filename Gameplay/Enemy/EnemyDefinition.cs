using UnityEngine;

public enum EnemyRole
{
    Swarm,
    Melee,
    Ranged,
    Flying,
    Healer,
    Mage,
    Dasher,
    Shielder,
    Summoner,
    Exploder,
    Boss
}

public enum EnemyElement
{
    Neutral,
    Fire,
    Frost,
    Lightning,
    Void,
    Nature,
    Poison,
    Arcane
}

[CreateAssetMenu(fileName = "NewEnemyDef", menuName = "AI/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public bool _isTrainingDummy = false;
    [Header("Identity")]
    public string Name;
    public EnemyRole Role = EnemyRole.Melee;
    public EnemyElement Element = EnemyElement.Neutral;

    [TextArea(2, 5)]
    public string Description;

    [Header("Prefab")]
    public GameObject Prefab;
    public float defaultScale = 1f;

    [Header("Spawn Rules")]
    [Tooltip("Kolik spawn budgetu tento enemy stojí. Slabý melee může stát 1, healer 3, elite-type enemy 5+.")]
    [Min(0.1f)]
    public float SpawnCost = 1f;

    [Tooltip("Od jaké minuty runu se tento enemy smí objevovat.")]
    [Min(0f)]
    public float MinRunMinute = 0f;

    [Tooltip("Do jaké minuty runu se tento enemy smí objevovat. 0 = bez limitu.")]
    [Min(0f)]
    public float MaxRunMinute = 0f;

    [Tooltip("Může tento enemy dostat mutation? Bossové nebo speciální enemy mohou mít false.")]
    public bool CanReceiveMutations = true;

    [Range(0f, 1f)]
    [Tooltip("Jak často tento enemy dostává mutace vůči ostatním. 1 = normálně, 0 = nikdy.")]
    public float MutationWeight = 1f;

    [Header("Base Stats")]
    [Min(1)]
    public int BaseHealth = 100;

    [Min(0)]
    public int BaseDamage = 10;

    [Min(0f)]
    public float BaseSpeed = 3.5f;

    [Header("Combat Stats")]
    [Tooltip("Kolik útoků za sekundu.")]
    [Min(0.01f)]
    public float BaseAttackRate = 1.0f;

    [Tooltip("Dosah útoku. Pro melee třeba 1.5, pro ranged/mage třeba 8-16.")]
    [Min(0f)]
    public float AttackRange = 1.5f;

    [Tooltip("0 = letí jako papír, 1 = nepohne se.")]
    [Range(0f, 1f)]
    public float BaseKnockbackResistance = 0f;

    [Header("Defense / Status")]
    [Range(0f, 1f)]
    public float FireResistance = 0f;

    [Range(0f, 1f)]
    public float FrostResistance = 0f;

    [Range(0f, 1f)]
    public float LightningResistance = 0f;

    [Range(0f, 1f)]
    public float VoidResistance = 0f;

    [Range(0f, 1f)]
    public float NatureResistance = 0f;

    [Header("Rewards")]
    [Min(0)]
    public int BaseXPDrop = 10;

    [Min(0)]
    public int BaseGoldDrop = 0;

    [Header("Loot")]
    public LootTable _lootTable;
    [Range(0f, 1f)]public float _lootChance = 0.3f;

    public float _aggroRange = 10000f;
    public float _rotationSpeed = 720f;
    public float _spawnDuration = 0.1f; // Kratší spawn, když není animace

    [Header("Visuals")]
    public Color IdentityColor = Color.white;

    [Tooltip("Volitelný spawn efekt.")]
    public GameObject SpawnVFX;

    [Tooltip("Efekty")]
    public GameObject DeathVFX;
    public GameObject HitVFX;
    public GameObject[] GorePrefabs; // Prefaby pro rozstřílení nepřítele (krev, kusy masa, kosti apod.)

    [Header("Audio")]
    public AudioClip SpawnSfx;
    public AudioClip DeathSfx;
    public AudioClip HitSfx;

    public bool IsAvailableAtMinute(float runMinute)
    {
        if (runMinute < MinRunMinute)
            return false;

        if (MaxRunMinute > 0f && runMinute > MaxRunMinute)
            return false;

        return true;
    }

    public float GetResistance(EnemyElement element)
    {
        return element switch
        {
            EnemyElement.Fire => FireResistance,
            EnemyElement.Frost => FrostResistance,
            EnemyElement.Lightning => LightningResistance,
            EnemyElement.Void => VoidResistance,
            EnemyElement.Nature => NatureResistance,
            EnemyElement.Poison => NatureResistance,
            EnemyElement.Arcane => VoidResistance,
            _ => 0f
        };
    }
}