using UnityEngine;
using TMPro;

public class DirectorSpawnerUI : MonoBehaviour
{
    [Header("General UI")]
    [SerializeField] private TextMeshProUGUI _difficultyText;

    [Header("Wave UI (Arena Only)")]
    [SerializeField] private GameObject _wavePanel; // Obalový objekt pro zobrazení/skrytí vlnového UI
    [SerializeField] private TextMeshProUGUI _waveNumberText;
    [SerializeField] private TextMeshProUGUI _enemiesRemainingText;

    private void Update()
    {
        if (DirectorSpawner.Instance == null) return;

        // Aktualizace základní obtížnosti
        if (_difficultyText != null)
        {
            _difficultyText.text = $"Difficulty: <color=red>{DirectorSpawner.Instance.CurrentDifficultyPercent.Value}%</color>";
        }

        // Pokud je aktivní mód vln, řešíme Wave UI
        if (DirectorSpawner.Instance.CurrentMode == DirectorSpawner.SpawnerMode.Wave)
        {
            if (_wavePanel != null && !_wavePanel.activeSelf) _wavePanel.SetActive(true);

            UpdateWaveUI();
        }
        else
        {
            // V klasickém Continuous režimu vlnové UI skryjeme
            if (_wavePanel != null && _wavePanel.activeSelf) _wavePanel.SetActive(false);
        }
    }

    private void UpdateWaveUI()
    {
        int currentWave = DirectorSpawner.Instance.CurrentWaveNetVar.Value;
        bool isWaveActive = DirectorSpawner.Instance.IsWaveActiveNetVar.Value;
        float countdown = DirectorSpawner.Instance.WaveCountdownNetVar.Value;

        // 1. STAV: Čekání na další vlnu (Odpočet)
        if (!isWaveActive && countdown > 0)
        {
            _waveNumberText.text = currentWave == 0 ? "PREPARE" : $"WAVE {currentWave} CLEARED";

            // Barevný přechod odpočtu: nad 3s bílá, pod 3s žlutá, pod 1.5s červená
            string colorHex = countdown switch
            {
                < 1.5f => "red",
                < 3.0f => "yellow",
                _ => "white"
            };

            _enemiesRemainingText.text = $"Next wave in: <color={colorHex}>{countdown:F1}s</color>";
        }
        // 2. STAV: Probíhající vlna
        else if (isWaveActive)
        {
            _waveNumberText.text = $"<color=#FFA500>WAVE {currentWave}</color>";

            int alive = DirectorSpawner.Instance.EnemiesAliveNetVar.Value;
            int yetToSpawn = DirectorSpawner.Instance.EnemiesYetToSpawnNetVar.Value;
            int totalRemaining = alive + yetToSpawn;
            int totalWaveSize = DirectorSpawner.Instance.TotalWaveEnemiesNetVar.Value;

            // Procentuální vyjádření postupu vlny
            float progress = 1f - ((float)totalRemaining / totalWaveSize);
            _enemiesRemainingText.text = $"Enemies: {totalRemaining} <size=80%>(Done: {progress:P0})</size>";
        }
        // 3. STAV: Úvodní inicializace nebo vítězství
        else
        {
            _waveNumberText.text = "READY?";
            _enemiesRemainingText.text = "Waiting for players...";
        }
    }
}
