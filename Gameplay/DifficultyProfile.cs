using UnityEngine;

[CreateAssetMenu(fileName = "NewDifficultyProfile", menuName = "Spawning/Difficulty Profile")]
public class DifficultyProfile : ScriptableObject
{
    [Tooltip("Počet nepřátel k vytvoření ZA SEKUNDU v dané minutě.")]
    public AnimationCurve SpawnRateCurve = AnimationCurve.Linear(0, 0.5f, 10, 15f);

    [Tooltip("Maximální povolený počet nepřátel na mapě v dané minutě.")]
    public AnimationCurve MaxEnemiesCurve = AnimationCurve.Linear(0, 10, 10, 200);
}