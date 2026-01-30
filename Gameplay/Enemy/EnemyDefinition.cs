using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyDef", menuName = "AI/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public string Name;
    public GameObject Prefab;
    
    [Header("Spawning Rules")]
    public EnemyRarity Rarity;
    public int Cost;
    
    [Header("Base Stats")]
    public int BaseHealth = 100;
    public int BaseDamage = 10;
    public float BaseSpeed = 3.5f;
    
    [Header("Combat Stats")]
    [Tooltip("Kolik útoků za sekundu.")]
    public float BaseAttackRate = 1.0f; 
    
    [Tooltip("0 = letí jako papír, 1 = nepohne se.")]
    [Range(0f, 1f)] public float BaseKnockbackResistance = 0f;
    
    [Header("Rewards")]
    public int BaseXPDrop = 10;
}