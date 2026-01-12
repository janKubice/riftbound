using UnityEngine;

[CreateAssetMenu(fileName = "NewStatusEffect", menuName = "Riftbound/Status Effect Data")]
public class StatusEffectData : ScriptableObject
{
    [Header("Identifikace")]
    public string EffectID; // Unikátní string ID pro lookupy (např. "BURN_01")
    public string EffectName;
    [TextArea] public string Description;
    public Sprite Icon;
    
    // Enum StatusEffectType ze souborů si ponecháme pro rychlou kategorizaci v UI
    public StatusEffectType Type; 

    [Header("Chování")]
    public bool IsStackable = false;
    public int MaxStacks = 1;
    public float Duration = 5.0f;
    
    [Header("Tick Logic (DoT / HoT)")]
    public float TickInterval = 1.0f; 
    public float DamagePerTick = 0f;
    public bool IsDamagePercentage = false; // % z Max HP (dobré proti bossům)

    [Header("Stat Modifikátory")]
    public float SpeedMultiplier = 1.0f; // 1 = normál, 0 = root
    public float DamageReceivedMultiplier = 1.0f; // 1.2 = cíl dostává o 20% větší DMG (Vulnerability)
    public float DamageDealtMultiplier = 1.0f;    // 0.5 = cíl dává poloviční DMG (Weakness)

    [Header("Hard CC")]
    public bool IsStun; // Zastaví pohyb i útoky
    public bool IsSilenced; // Zastaví spelly (pro mágy)

    [Header("Vizuál a Audio")]
    public GameObject EffectVFXPrefab;
    public string AttachBoneName = ""; 
    public AudioClip ApplySound;
    public AudioClip TickSound;
}