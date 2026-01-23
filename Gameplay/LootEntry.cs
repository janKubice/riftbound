using UnityEngine;

[System.Serializable]
public struct LootEntry
{
    public GameObject Prefab;   // Prefab Orbu (CollectableOrb)
    public LootType Type;
    [Range(0, 100)] public int DropChanceWeight; // Váha šance (čím víc, tím častěji padá)
    public int MinAmount;
    public int MaxAmount;
}