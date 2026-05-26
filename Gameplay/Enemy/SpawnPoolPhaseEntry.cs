using System;
using UnityEngine;

[Serializable]
public class SpawnPoolPhaseEntry
{
    public string EntryName = "Pool Entry";

    [Min(0f)]
    public float Weight = 1f;

    [Tooltip("Lokální čas od začátku této fáze. 0 = hned.")]
    [Min(0f)]
    public float LocalStartSeconds = 0f;

    [Tooltip("Lokální čas od začátku této fáze. 0 = bez limitu.")]
    [Min(0f)]
    public float LocalEndSeconds = 0f;

    public SpawnPool Pool = new SpawnPool();

    public bool IsAvailable(float phaseLocalSeconds)
    {
        if (Weight <= 0f)
            return false;

        if (Pool == null || !Pool.IsUsable())
            return false;

        if (phaseLocalSeconds < LocalStartSeconds)
            return false;

        if (LocalEndSeconds > 0f && phaseLocalSeconds > LocalEndSeconds)
            return false;

        return true;
    }
}