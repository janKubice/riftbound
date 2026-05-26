using UnityEngine;
using TMPro;

public class DirectorSpawnerUI : MonoBehaviour
{
    [Header("General UI")]
    [SerializeField] private TextMeshProUGUI _difficultyText;
    [SerializeField] private TextMeshProUGUI _runTimeText;
    [SerializeField] private TextMeshProUGUI _phaseText;
    [SerializeField] private TextMeshProUGUI _pressureText;
    [SerializeField] private TextMeshProUGUI _aliveEnemiesText;
    [SerializeField] private TextMeshProUGUI _spawnRateText;

    [Header("Wave UI")]
    [SerializeField] private GameObject _wavePanel;
    [SerializeField] private TextMeshProUGUI _waveNumberText;
    [SerializeField] private TextMeshProUGUI _enemiesRemainingText;

    [Header("Options")]
    [SerializeField] private bool _showContinuousPanel = true;
    [SerializeField] private bool _showWavePanel = true;
    [SerializeField] private bool _hideWhenNoSpawner = true;

    private void Update()
    {
        DirectorSpawner spawner = DirectorSpawner.Instance;

        if (spawner == null)
        {
            if (_hideWhenNoSpawner)
            {
                SetActiveSafe(_wavePanel, false);
            }

            return;
        }

        UpdateGeneralUI(spawner);

        if (spawner.CurrentMode == DirectorSpawner.SpawnerMode.Wave)
        {
            SetActiveSafe(_wavePanel, _showWavePanel);

            UpdateWaveUI(spawner);
        }
        else
        {
            SetActiveSafe(_wavePanel, true);

            UpdateContinuousUI(spawner);
        }

        _wavePanel.SetActive(_showWavePanel);
    }

    private void UpdateGeneralUI(DirectorSpawner spawner)
    {
        if (_difficultyText != null)
        {
            int difficulty = spawner.CurrentDifficultyPercent.Value;
            string color = difficulty switch
            {
                >= 250 => "#FF4040",
                >= 175 => "#FF9A3D",
                >= 125 => "#FFD166",
                _ => "#FFFFFF"
            };

            _difficultyText.text = $"Difficulty: <color={color}>{difficulty}%</color>";
        }

        if (_runTimeText != null)
        {
            int seconds = spawner.RunTimeSecondsNetVar.Value;
            int minutes = seconds / 60;
            int restSeconds = seconds % 60;

            _runTimeText.text = $"Time: {minutes:00}:{restSeconds:00}";
        }

        if (_phaseText != null)
        {
            RunPhaseType phaseType = (RunPhaseType)spawner.CurrentPhaseTypeNetVar.Value;
            string label = GetPhaseLabel(phaseType);
            string color = GetPhaseColor(phaseType);

            _phaseText.text = $"Phase: <color={color}>{label}</color>";
        }

        if (_pressureText != null)
        {
            int pressure = spawner.CurrentPressurePercentNetVar.Value;
            string color = pressure switch
            {
                >= 180 => "#FF4040",
                >= 130 => "#FF9A3D",
                >= 100 => "#FFD166",
                _ => "#90E090"
            };

            _pressureText.text = $"Pressure: <color={color}>{pressure}%</color>";
        }
    }

    private void UpdateContinuousUI(DirectorSpawner spawner)
    {
        if (_aliveEnemiesText != null)
        {
            int alive = spawner.EnemiesAliveNetVar.Value;
            int max = spawner.CurrentMaxEnemiesNetVar.Value;

            if (max > 0)
                _aliveEnemiesText.text = $"Enemies: {alive}/{max}";
            else
                _aliveEnemiesText.text = $"Enemies: {alive}";
        }

        if (_spawnRateText != null)
        {
            float spawnRate = spawner.CurrentSpawnRatePerSecondNetVar.Value;
            _spawnRateText.text = $"Spawn Rate: {spawnRate:0.0}/s";
        }
    }

    private void UpdateWaveUI(DirectorSpawner spawner)
    {
        if (_waveNumberText == null || _enemiesRemainingText == null)
            return;

        int currentWave = spawner.CurrentWaveNetVar.Value;
        bool isWaveActive = spawner.IsWaveActiveNetVar.Value;
        float countdown = spawner.WaveCountdownNetVar.Value;

        if (!isWaveActive && countdown > 0)
        {
            _waveNumberText.text = currentWave == 0
                ? "PREPARE"
                : $"WAVE {currentWave} CLEARED";

            string colorHex = countdown switch
            {
                < 1.5f => "#FF4040",
                < 3.0f => "#FFD166",
                _ => "#FFFFFF"
            };

            _enemiesRemainingText.text = $"Next wave in: <color={colorHex}>{countdown:F1}s</color>";
        }
        else if (isWaveActive)
        {
            _waveNumberText.text = $"<color=#FFA500>WAVE {currentWave}</color>";

            int alive = spawner.EnemiesAliveNetVar.Value;
            int yetToSpawn = spawner.EnemiesYetToSpawnNetVar.Value;
            int totalRemaining = alive + yetToSpawn;
            int totalWaveSize = Mathf.Max(1, spawner.TotalWaveEnemiesNetVar.Value);

            float progress = 1f - ((float)totalRemaining / totalWaveSize);
            progress = Mathf.Clamp01(progress);

            _enemiesRemainingText.text =
                $"Enemies: {totalRemaining} <size=80%>(Done: {progress:P0})</size>";
        }
        else
        {
            _waveNumberText.text = "READY?";
            _enemiesRemainingText.text = "Waiting for players...";
        }
    }

    private static void SetActiveSafe(GameObject obj, bool active)
    {
        if (obj != null && obj.activeSelf != active)
            obj.SetActive(active);
    }

    private static string GetPhaseLabel(RunPhaseType type)
    {
        return type switch
        {
            RunPhaseType.Warmup => "Warmup",
            RunPhaseType.BuildUp => "Build Up",
            RunPhaseType.Pressure => "Pressure",
            RunPhaseType.Spike => "Spike",
            RunPhaseType.Breather => "Breather",
            RunPhaseType.EliteMoment => "Elite",
            RunPhaseType.BossMoment => "Boss",
            _ => type.ToString()
        };
    }

    private static string GetPhaseColor(RunPhaseType type)
    {
        return type switch
        {
            RunPhaseType.Warmup => "#A8E6A3",
            RunPhaseType.BuildUp => "#FFFFFF",
            RunPhaseType.Pressure => "#FFD166",
            RunPhaseType.Spike => "#FF6B4A",
            RunPhaseType.Breather => "#7DDCFF",
            RunPhaseType.EliteMoment => "#C77DFF",
            RunPhaseType.BossMoment => "#FF4040",
            _ => "#FFFFFF"
        };
    }
}