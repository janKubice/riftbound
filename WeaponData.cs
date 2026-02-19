using System.Text;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponData", menuName = "Items/Weapon Data")]
public class WeaponData : ScriptableObject
{
    [Header("Identity")]
    public string WeaponName;
    public Sprite Icon;

    [Header("Stats")]
    public WeaponStats BaseStats; // Výchozí hodnoty

    [Header("Economy")]
    public int GoldPrice = 100;
    public int EssencePrice = 0;

    [Header("Logika")]
    public AttackLogic AttackLogic; // ScriptableObject definující chování (Projectile, Melee, Bomb...)

    [Header("Visuals")]
    public WeaponAnimationData AnimationData; 
    public GameObject ModelPrefab;      // Vizuál do ruky
    public GameObject ProjectilePrefab; // Prefab střely (pokud střílí)
    public GameObject HitVFXPrefab;     // Krev/Výbuch
    public GameObject MuzzleFlashPrefab;// Záblesk

    [Header("Audio")]
    public AudioClip FireSound;       // Zvuk při útoku (švihnutí, výstřel)
    public AudioClip ImpactSound;     // Zvuk při dopadu (zásah do zdi/nepřítele)
    [Range(0f, 1f)] public float FireVolume = 1.0f;
    [Range(0f, 1f)] public float ImpactVolume = 1.0f;

    [Header("Description")]
    [TextArea(3,5)] public string Description;

    [Header("Settings")]
    public bool IsRanged = false;
    public bool IsTwoHanded = false;

    [Tooltip("Pokud je true, WeaponManager bude řešit vizuál jako kontinuální paprsek.")]
    public bool IsContinuous = false;


    public string GetRichTextStats()
    {
        StringBuilder sb = new StringBuilder();

        // Název a Cena
        sb.AppendLine($"<size=120%><b>{WeaponName}</b></size>");
        sb.AppendLine($"<color=#FFD700>Price: {GoldPrice} G</color>");
        sb.AppendLine();

        // Statistiky - Používáme barvy pro čísla
        if (BaseStats.Damage > 0)
            sb.AppendLine($"Damage: <color=#FF4444>{BaseStats.Damage}</color> {GetDamageTypeIcon(BaseStats.DamageType)}");
        
        if (BaseStats.Cooldown > 0)
            sb.AppendLine($"Speed: <color=#44FFFF>{(1f/BaseStats.Cooldown):F1}</color> attacks/s");
        
        if (IsRanged && BaseStats.ProjectileCount > 1)
            sb.AppendLine($"Projectiles: <color=#FFFF44>{BaseStats.ProjectileCount}</color>");

        if (BaseStats.Effect.Type != StatusEffectType.None)
            sb.AppendLine($"Effect: <color=#FF00FF>{BaseStats.Effect.Type}</color>");

        // Lore popis
        if (!string.IsNullOrEmpty(Description))
        {
            sb.AppendLine();
            sb.AppendLine($"<i><color=#AAAAAA>{Description}</color></i>");
        }

        return sb.ToString();
    }

    private string GetDamageTypeIcon(DamageType type)
    {
        // Tady můžeš vrátit string ikony ze Sprite Assetu v TMP, nebo jen text
        switch(type)
        {
            case DamageType.Fire: return "<color=red>(Fire)</color>";
            case DamageType.Ice: return "<color=cyan>(Ice)</color>";
            default: return "";
        }
    }
}