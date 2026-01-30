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

    [Header("Settings")]
    public bool IsRanged = false;
    public bool IsTwoHanded = false;

    [Tooltip("Pokud je true, WeaponManager bude řešit vizuál jako kontinuální paprsek.")]
    public bool IsContinuous = false;
}