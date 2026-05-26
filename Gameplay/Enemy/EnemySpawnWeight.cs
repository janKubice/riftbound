using UnityEngine;

[System.Serializable]
public class EnemySpawnWeight
{
    public EnemyDefinition EnemyDef;

    [Tooltip("Relativní šance na spawn vůči ostatním v poolu.")]
    [Min(0f)]
    public float Weight = 10f;

    [Header("Optional Time Gate Override")]
    [Tooltip("Pokud je větší než 0, přepíše EnemyDefinition.MinRunMinute.")]
    [Min(0f)]
    public float MinRunMinuteOverride = 0f;

    [Tooltip("Pokud je větší než 0, přepíše EnemyDefinition.MaxRunMinute.")]
    [Min(0f)]
    public float MaxRunMinuteOverride = 0f;

    public bool IsAvailable(float runMinute)
    {
        if (EnemyDef == null)
            return false;

        if (Weight <= 0f)
            return false;

        float minMinute = MinRunMinuteOverride > 0f
            ? MinRunMinuteOverride
            : EnemyDef.MinRunMinute;

        float maxMinute = MaxRunMinuteOverride > 0f
            ? MaxRunMinuteOverride
            : EnemyDef.MaxRunMinute;

        if (runMinute < minMinute)
            return false;

        if (maxMinute > 0f && runMinute > maxMinute)
            return false;

        return true;
    }
}