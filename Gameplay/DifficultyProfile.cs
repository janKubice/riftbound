using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "NewDifficultyProfile", menuName = "Spawning/Difficulty Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Header("Base Curves")]
    [Tooltip("Počet nepřátel k vytvoření ZA SEKUNDU v dané minutě.")]
    public AnimationCurve SpawnRateCurve = AnimationCurve.Linear(0, 0.5f, 10, 15f);

    [Tooltip("Maximální povolený počet nepřátel na mapě v dané minutě.")]
    public AnimationCurve MaxEnemiesCurve = AnimationCurve.Linear(0, 10, 10, 200);

    [Header("Danger Rhythm")]
    [Tooltip("Globální pressure multiplier nad base spawn rate. Hodí se pro jemné pulzování difficulty.")]
    public AnimationCurve PressureCurve = new AnimationCurve(
        new Keyframe(0f, 1.0f),
        new Keyframe(1f, 1.0f),
        new Keyframe(2f, 1.15f),
        new Keyframe(3f, 0.8f),
        new Keyframe(4f, 1.25f),
        new Keyframe(6f, 1.45f),
        new Keyframe(8f, 1.65f),
        new Keyframe(10f, 1.9f)
    );

    public List<DangerRhythmPhase> DangerPhases = new List<DangerRhythmPhase>();

    public int GetPhaseIndex(float gameTimeMinutes)
    {
        if (DangerPhases == null || DangerPhases.Count == 0)
            return -1;

        int lastStartedPhase = -1;

        for (int i = 0; i < DangerPhases.Count; i++)
        {
            DangerRhythmPhase phase = DangerPhases[i];

            if (phase == null)
                continue;

            if (gameTimeMinutes >= phase.StartMinute)
                lastStartedPhase = i;

            if (gameTimeMinutes >= phase.StartMinute && gameTimeMinutes < phase.EndMinute)
                return i;
        }

        return lastStartedPhase;
    }

    public DangerRhythmPhase GetPhase(float gameTimeMinutes)
    {
        int index = GetPhaseIndex(gameTimeMinutes);

        if (index < 0 || DangerPhases == null || index >= DangerPhases.Count)
            return null;

        return DangerPhases[index];
    }
}