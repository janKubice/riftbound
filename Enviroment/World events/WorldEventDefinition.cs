using UnityEngine;

[CreateAssetMenu(fileName = "NewWorldEventDefinition", menuName = "World Events/Event Definition")]
public class WorldEventDefinition : ScriptableObject
{
    [Header("Identity")]
    public string EventName = "Mana Shrine";
    public WorldPOICategory Category = WorldPOICategory.Shrine;

    [Header("Prefab")]
    public WorldPOIBase Prefab;

    [Header("Spawn Settings")]
    [Tooltip("Lokální vertikální posun od nalezené země. Řeší rozdílné pivoty prefabů.")]
    public float VerticalSpawnOffset = 0f;


    [Header("Pre-spawn")]
    public bool CanPreSpawn = true;

    [Tooltip("Relativní šance při rozmístění na začátku runu.")]
    [Min(0f)]
    public float PreSpawnWeight = 1f;

    [Tooltip("Maximum dormant/active instancí tohoto typu na mapě.")]
    [Min(0)]
    public int MaxInstances = 3;

    [Header("Dynamic Spawn")]
    public bool CanDynamicSpawn = false;

    [Tooltip("Relativní šance při dynamickém dospawnu.")]
    [Min(0f)]
    public float DynamicSpawnWeight = 1f;

    [Header("Activation Rules")]
    [Min(0f)]
    public float MinRunMinute = 0f;

    [Min(0f)]
    public float MaxRunMinute = 0f;

    [Tooltip("Jak dlouho je event aktivní. 0 = nikdy automaticky nevyprší.")]
    [Min(0f)]
    public float ActiveDurationSeconds = 90f;

    [Tooltip("Maximum současně aktivních instancí tohoto eventu.")]
    [Min(1)]
    public int MaxActiveInstances = 1;

    public bool IsAvailableAtMinute(float runMinute)
    {
        if (runMinute < MinRunMinute)
            return false;

        if (MaxRunMinute > 0f && runMinute > MaxRunMinute)
            return false;

        return Prefab != null;
    }
}