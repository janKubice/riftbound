using UnityEngine;

[CreateAssetMenu(fileName = "NewEnemyDef", menuName = "AI/Enemy Definition")]
public class EnemyDefinition : ScriptableObject
{
    public string Name;
    public GameObject Prefab; // Prefab musí mít EnemyBaseAI
    
    [Header("Spawning Rules")]
    public EnemyRarity Rarity;     // Jak často se losuje
    public int Cost;               // Kolik "kreditů" stojí (např. Zombie=1, Boss=100)
    
    [Header("Base Stats")]
    public int BaseHealth = 100;
    public int BaseDamage = 10;
    public float BaseSpeed = 3.5f;
    public int BaseXPDrop = 10;
}
