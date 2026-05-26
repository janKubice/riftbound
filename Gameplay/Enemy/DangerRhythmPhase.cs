using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class DangerRhythmPhase
{
    [Header("Identity")]
    public string PhaseName = "Warmup";
    public RunPhaseType PhaseType = RunPhaseType.Warmup;

    [Header("Time Window")]
    [Min(0f)] public float StartMinute = 0f;
    [Min(0.01f)] public float EndMinute = 1f;

    [Header("Fallback Pool")]
    [Tooltip("Fallback pool pro tuto fázi. Pokud je Spawn Pool Schedule prázdný, použije se tento.")]
    public SpawnPool OverridePool = new SpawnPool();

    [Header("Spawn Pool Schedule")]
    [Tooltip("Více poolů pro jednu fázi. Director si mezi dostupnými pooly vybírá podle vah.")]
    public List<SpawnPoolPhaseEntry> SpawnPoolSchedule = new List<SpawnPoolPhaseEntry>();

    [Header("Spawn Pressure")]
    [Min(0f)] public float SpawnRateMultiplier = 1f;
    [Min(0f)] public float MaxEnemiesMultiplier = 1f;
    [Min(0f)] public float DifficultyMultiplier = 1f;

    [Header("Tier Pressure")]
    [Min(0f)] public float EliteChanceMultiplier = 1f;
    [Min(0f)] public float ChampionChanceMultiplier = 1f;

    [Header("Elite Pulse")]
    [Min(0f)] public float ElitePulseEverySeconds = 0f;
    [Min(0)] public int ElitePulseCount = 0;
    [Min(0f)] public float FirstElitePulseDelay = 4f;
    public EnemyTier ElitePulseTier = EnemyTier.Elite;

    [Header("Behavior")]
    public bool PauseRegularSpawns = false;

    public bool ContainsTime(float runMinute)
    {
        return runMinute >= StartMinute && runMinute < EndMinute;
    }

    public float GetLocalSeconds(float runMinute)
    {
        return Mathf.Max(0f, (runMinute - StartMinute) * 60f);
    }

    public SpawnPool PickSpawnPool(float runMinute)
    {
        float localSeconds = GetLocalSeconds(runMinute);

        if (SpawnPoolSchedule != null && SpawnPoolSchedule.Count > 0)
        {
            float totalWeight = 0f;

            for (int i = 0; i < SpawnPoolSchedule.Count; i++)
            {
                SpawnPoolPhaseEntry entry = SpawnPoolSchedule[i];

                if (entry != null && entry.IsAvailable(localSeconds))
                    totalWeight += entry.Weight;
            }

            if (totalWeight > 0f)
            {
                float roll = UnityEngine.Random.Range(0f, totalWeight);
                float current = 0f;

                for (int i = 0; i < SpawnPoolSchedule.Count; i++)
                {
                    SpawnPoolPhaseEntry entry = SpawnPoolSchedule[i];

                    if (entry == null || !entry.IsAvailable(localSeconds))
                        continue;

                    current += entry.Weight;

                    if (roll <= current)
                        return entry.Pool;
                }
            }
        }

        if (OverridePool != null && OverridePool.IsUsable())
            return OverridePool;

        return null;
    }
}