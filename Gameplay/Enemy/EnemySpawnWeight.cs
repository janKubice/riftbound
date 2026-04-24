using UnityEngine;

[System.Serializable]
public class EnemySpawnWeight
{
    public EnemyDefinition EnemyDef;
    [Tooltip("Relativní šance na spawn vůči ostatním v poolu.")]
    public float Weight = 10f; 
}