using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections;
using System.Collections.Generic;

public class DirectorSpawner : NetworkBehaviour
{
    public static DirectorSpawner Instance { get; private set; }

    public enum SpawnerMode
    {
        Continuous,
        Wave
    }

    [Header("Mode Settings")]
    [Tooltip("Určuje, zda se hraje normální přežití nebo aréna / vlny.")]
    public SpawnerMode CurrentMode = SpawnerMode.Continuous;

    [Header("Difficulty")]
    [SerializeField] private DifficultyProfile _currentDifficultyProfile;

    [Header("Active Spawn Pool")]
    [SerializeField] private SpawnPool _currentSpawnPool;

    [Header("Danger Rhythm")]
    [SerializeField] private bool _useDangerRhythm = true;

    [Tooltip("Jak často se přepočítává phase info pro UI a elite pulses.")]
    [SerializeField] private float _phaseUpdateInterval = 0.25f;

    [Header("Stats Scaling")]
    [SerializeField] private float _exponentialScalingFactor = 1.1f;

    [Header("Performance Limits")]
    [SerializeField] private int _maxSpawnsPerFrame = 5;

    [Header("Spawn Distance")]
    [SerializeField] private float _minSpawnDistanceFromPlayer = 15f;
    [SerializeField] private float _maxSpawnDistanceFromPlayer = 50f;

    [Header("Terrain")]
    [SerializeField] private string _terrainLayerName = "Enviroment";
    [SerializeField] private float _spawnRaycastHeight = 20f;
    [SerializeField] private float _spawnRaycastDistance = 40f;
    [SerializeField] private float _spawnGroundOffset = 0.2f;

    [Header("Safe Zone")]
    [SerializeField] private float _safeZoneRadius = 15.0f;
    [SerializeField] private bool _canPauseGame = false;
    [SerializeField] private float _checkPlayersInterval = 1.0f;

    [Header("Tiers")]
    [Range(0, 1)][SerializeField] private float _eliteChance = 0.1f;
    [Range(0, 1)][SerializeField] private float _championChance = 0.02f;

    [Header("Wave Settings (Arena)")]
    [SerializeField] private int _maxWaves = 10;
    [SerializeField] private int _baseWaveEnemies = 10;
    [SerializeField] private int _waveEnemyMultiplier = 5;
    [SerializeField] private float _waveSpawnRate = 3.0f;
    [SerializeField] private float _timeBetweenWaves = 5.0f;
    [SerializeField] private float _wavePowerScale = 0.1f;

    [Header("Debug")]
    [SerializeField] private bool _debugPhaseChanges = true;
    [SerializeField] private bool _debugSpawns = false;

    // --- Runtime state ---
    private float _gameTimeMinutes = 0f;
    private float _difficultyMultiplier = 1.0f;
    private float _spawnAccumulator = 0f;

    private bool _hasGameStarted = false;
    private float _lastPlayerCheckTime;
    private bool _arePlayersActive = false;
    private bool _isWaitingForNextWave = false;

    private int _terrainLayerMask;

    private float _nextPhaseUpdateTime;
    private int _currentPhaseIndex = -999;
    private DangerRhythmPhase _currentPhase;
    private float _nextElitePulseTime;
    private Coroutine _elitePulseRoutine;

    private readonly HashSet<EnemySpawnPoint> _spawnPoints = new HashSet<EnemySpawnPoint>();
    private readonly List<EnemySpawnPoint> _validPointsBuffer = new List<EnemySpawnPoint>(64);

    // --- Network Variables ---
    public NetworkVariable<int> EnemiesAliveNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> CurrentWaveNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> EnemiesYetToSpawnNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> TotalWaveEnemiesNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<bool> IsWaveActiveNetVar = new NetworkVariable<bool>(false);
    public NetworkVariable<float> WaveCountdownNetVar = new NetworkVariable<float>(0f);

    public NetworkVariable<int> CurrentMaxEnemiesNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<float> CurrentSpawnRatePerSecondNetVar = new NetworkVariable<float>(0f);

    public NetworkVariable<int> CurrentDifficultyPercent = new NetworkVariable<int>(100);

    [Header("Network UI - Danger Rhythm")]
    public NetworkVariable<int> RunTimeSecondsNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> CurrentPhaseIndexNetVar = new NetworkVariable<int>(-1);
    public NetworkVariable<int> CurrentPhaseTypeNetVar = new NetworkVariable<int>((int)RunPhaseType.Warmup);
    public NetworkVariable<int> CurrentPressurePercentNetVar = new NetworkVariable<int>(100);
    [Header("Network UI - Time Info")]
    public NetworkVariable<float> PhaseEndTimeSecondsNetVar = new NetworkVariable<float>(0f);
    public NetworkVariable<bool> IsSpawningPausedNetVar = new NetworkVariable<bool>(false);

    public static event Action OnEnemySpawned;
    public static event Action<DangerRhythmPhase> OnServerPhaseChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        _terrainLayerMask = LayerMask.GetMask(_terrainLayerName);

        if (_terrainLayerMask == 0)
        {
            Debug.LogWarning(
                $"DirectorSpawner: Terrain layer mask for '{_terrainLayerName}' is 0. " +
                "Zkontrolujte název layeru. Ve vašem projektu je možná překlep 'Enviroment'.",
                this
            );
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer)
            return;

        EnemiesAliveNetVar.Value = 0;
        CurrentWaveNetVar.Value = 0;
        EnemiesYetToSpawnNetVar.Value = 0;
        TotalWaveEnemiesNetVar.Value = 0;
        IsWaveActiveNetVar.Value = false;
        WaveCountdownNetVar.Value = 0f;
        CurrentMaxEnemiesNetVar.Value = 0;
        CurrentSpawnRatePerSecondNetVar.Value = 0f;
        PhaseEndTimeSecondsNetVar.Value = 0f;

        CurrentDifficultyPercent.Value = 100;
        RunTimeSecondsNetVar.Value = 0;
        CurrentPhaseIndexNetVar.Value = -1;
        CurrentPhaseTypeNetVar.Value = (int)RunPhaseType.Warmup;
        CurrentPressurePercentNetVar.Value = 100;

        _gameTimeMinutes = 0f;
        _difficultyMultiplier = 1f;
        _spawnAccumulator = 0f;
        _currentPhaseIndex = -999;
        _currentPhase = null;
    }

    public void RegisterSpawnPoint(EnemySpawnPoint sp)
    {
        if (sp != null)
            _spawnPoints.Add(sp);
    }

    public void UnregisterSpawnPoint(EnemySpawnPoint sp)
    {
        if (sp != null)
            _spawnPoints.Remove(sp);
    }

    public void EnemyDied()
    {
        if (!IsServer)
            return;

        EnemiesAliveNetVar.Value = Mathf.Max(0, EnemiesAliveNetVar.Value - 1);
    }

    private void AwardStatToAllConnectedPlayers(string statApiName, int amount)
    {
        if (!IsServer || SteamStatsManager.Instance == null || !SteamStatsManager.Instance.IsSpawned)
            return;

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            SteamStatsManager.Instance.IncrementStatForClient(
                client.ClientId,
                statApiName,
                amount
            );
        }
    }

    private void Update()
    {
        if (!IsServer)
            return;

        UpdatePlayerActivity();

        if (!_hasGameStarted || (_canPauseGame && !_arePlayersActive))
            return;

        _gameTimeMinutes += Time.deltaTime / 60f;

        UpdateDifficultyAndPhase();

        if (CurrentMode == SpawnerMode.Continuous)
        {
            ProcessContinuousSpawning();
            ProcessElitePulse();
        }
        else if (CurrentMode == SpawnerMode.Wave)
        {
            ProcessWaveSpawning();
        }
    }

    private void UpdatePlayerActivity()
    {
        if (Time.time < _lastPlayerCheckTime + _checkPlayersInterval)
            return;

        _lastPlayerCheckTime = Time.time;
        _arePlayersActive = CheckIfPlayersAreActive();

        if (!_hasGameStarted && _arePlayersActive)
        {
            _hasGameStarted = true;

            if (CurrentMode == SpawnerMode.Wave)
                StartCoroutine(PrepareNextWaveRoutine());
        }
    }

    private void UpdateDifficultyAndPhase()
    {
        if (_currentDifficultyProfile == null)
            return;

        float phaseDifficultyMultiplier = _currentPhase != null
            ? Mathf.Max(0f, _currentPhase.DifficultyMultiplier)
            : 1f;

        _difficultyMultiplier =
            Mathf.Pow(_exponentialScalingFactor, _gameTimeMinutes) *
            phaseDifficultyMultiplier;

        CurrentDifficultyPercent.Value = Mathf.FloorToInt(_difficultyMultiplier * 100f);
        RunTimeSecondsNetVar.Value = Mathf.FloorToInt(_gameTimeMinutes * 60f);

        if (Time.time < _nextPhaseUpdateTime)
            return;

        _nextPhaseUpdateTime = Time.time + _phaseUpdateInterval;

        UpdateCurrentPhase();
    }

    private void UpdateCurrentPhase()
    {
        if (!_useDangerRhythm || _currentDifficultyProfile == null)
        {
            SetPhaseNetworkValues(-1, null);
            return;
        }

        int newPhaseIndex = _currentDifficultyProfile.GetPhaseIndex(_gameTimeMinutes);
        DangerRhythmPhase newPhase = _currentDifficultyProfile.GetPhase(_gameTimeMinutes);

        if (newPhaseIndex != _currentPhaseIndex)
        {
            _currentPhaseIndex = newPhaseIndex;
            _currentPhase = newPhase;

            float firstPulseDelay = _currentPhase != null
                ? Mathf.Max(0f, _currentPhase.FirstElitePulseDelay)
                : 0f;

            _nextElitePulseTime = Time.time + firstPulseDelay;

            SetPhaseNetworkValues(_currentPhaseIndex, _currentPhase);

            if (_debugPhaseChanges && _currentPhase != null)
            {
                Debug.Log(
                    $"[DirectorSpawner] Phase changed to '{_currentPhase.PhaseName}' " +
                    $"at {_gameTimeMinutes:0.00} min.",
                    this
                );
            }

            OnServerPhaseChanged?.Invoke(_currentPhase);
        }

        float pressure = GetCurrentPressureMultiplier();
        CurrentPressurePercentNetVar.Value = Mathf.RoundToInt(pressure * 100f);
    }

    private void SetPhaseNetworkValues(int phaseIndex, DangerRhythmPhase phase)
    {
        CurrentPhaseIndexNetVar.Value = phaseIndex;
        CurrentPhaseTypeNetVar.Value = phase != null ? (int)phase.PhaseType : (int)RunPhaseType.Warmup;
        PhaseEndTimeSecondsNetVar.Value = phase != null ? phase.EndMinute * 60f : 0f;
        IsSpawningPausedNetVar.Value = phase != null && phase.PauseRegularSpawns;
    }

    private float GetCurrentPressureMultiplier()
    {
        if (_currentDifficultyProfile == null || _currentDifficultyProfile.PressureCurve == null)
            return 1f;

        float pressure = Mathf.Max(0f, _currentDifficultyProfile.PressureCurve.Evaluate(_gameTimeMinutes));

        if (_currentPhase != null)
            pressure *= Mathf.Max(0f, _currentPhase.SpawnRateMultiplier);

        return pressure;
    }

    private void ProcessContinuousSpawning()
    {
        if (_currentDifficultyProfile == null)
            return;

        if (_currentSpawnPool == null || !_currentSpawnPool.IsUsable())
            return;

        if (_currentPhase != null && _currentPhase.PauseRegularSpawns)
            return;

        float baseSpawnRate = Mathf.Max(0f, _currentDifficultyProfile.SpawnRateCurve.Evaluate(_gameTimeMinutes));
        float pressureMultiplier = GetCurrentPressureMultiplier();

        float currentSpawnRate = baseSpawnRate * pressureMultiplier;

        float maxEnemyMultiplier = _currentPhase != null
            ? Mathf.Max(0f, _currentPhase.MaxEnemiesMultiplier)
            : 1f;

        int currentMaxEnemies = Mathf.RoundToInt(
            Mathf.Max(0f, _currentDifficultyProfile.MaxEnemiesCurve.Evaluate(_gameTimeMinutes)) *
            maxEnemyMultiplier
        );

        CurrentMaxEnemiesNetVar.Value = currentMaxEnemies;
        CurrentSpawnRatePerSecondNetVar.Value = currentSpawnRate;

        if (currentMaxEnemies <= 0)
            return;

        _spawnAccumulator += currentSpawnRate * Time.deltaTime;

        if (EnemiesAliveNetVar.Value >= currentMaxEnemies)
        {
            _spawnAccumulator = Mathf.Lerp(_spawnAccumulator, 0f, Time.deltaTime * 0.3f);
            return;
        }

        int spawnsThisFrame = 0;

        while (_spawnAccumulator >= 1.0f &&
               spawnsThisFrame < _maxSpawnsPerFrame &&
               EnemiesAliveNetVar.Value < currentMaxEnemies)
        {
            bool spawned = SpawnSingleEnemy(null, null);

            if (!spawned)
                break;

            _spawnAccumulator -= 1.0f;
            spawnsThisFrame++;
        }
    }

    private void ProcessElitePulse()
    {
        if (_currentPhase == null)
            return;

        if (_currentPhase.ElitePulseEverySeconds <= 0f || _currentPhase.ElitePulseCount <= 0)
            return;

        if (_elitePulseRoutine != null)
            return;

        if (Time.time < _nextElitePulseTime)
            return;

        _nextElitePulseTime = Time.time + _currentPhase.ElitePulseEverySeconds;
        _elitePulseRoutine = StartCoroutine(ElitePulseRoutine(_currentPhase));
    }

    private IEnumerator ElitePulseRoutine(DangerRhythmPhase phase)
    {
        int count = Mathf.Max(0, phase.ElitePulseCount);

        for (int i = 0; i < count; i++)
        {
            SpawnSingleEnemy(phase.OverridePool, phase.ElitePulseTier);
            yield return new WaitForSeconds(0.12f);
        }

        _elitePulseRoutine = null;
    }

    private void ProcessWaveSpawning()
    {
        if (!IsWaveActiveNetVar.Value || _isWaitingForNextWave)
            return;

        if (EnemiesYetToSpawnNetVar.Value > 0)
        {
            _spawnAccumulator += _waveSpawnRate * Time.deltaTime;
            int spawnsThisFrame = 0;

            CurrentMaxEnemiesNetVar.Value = TotalWaveEnemiesNetVar.Value;
            CurrentSpawnRatePerSecondNetVar.Value = _waveSpawnRate;

            while (_spawnAccumulator >= 1.0f &&
                   spawnsThisFrame < _maxSpawnsPerFrame &&
                   EnemiesYetToSpawnNetVar.Value > 0)
            {
                bool spawned = SpawnSingleEnemy(null, null);

                if (!spawned)
                    break;

                _spawnAccumulator -= 1.0f;
                spawnsThisFrame++;
            }
        }
        else if (EnemiesAliveNetVar.Value <= 0)
        {
            IsWaveActiveNetVar.Value = false;
            AwardStatToAllConnectedPlayers(SteamStatIds.WavesCompleted, 1);

            if (CurrentWaveNetVar.Value >= _maxWaves)
            {
                TriggerDemoVictory();
            }
            else
            {
                StartCoroutine(PrepareNextWaveRoutine());
            }
        }
    }

    private IEnumerator PrepareNextWaveRoutine()
    {
        _isWaitingForNextWave = true;

        float timer = _timeBetweenWaves;

        while (timer > 0f)
        {
            WaveCountdownNetVar.Value = timer;
            yield return new WaitForSeconds(0.1f);
            timer -= 0.1f;
        }

        WaveCountdownNetVar.Value = 0f;

        CurrentWaveNetVar.Value++;

        int enemiesForWave = _baseWaveEnemies + (_waveEnemyMultiplier * (CurrentWaveNetVar.Value * CurrentWaveNetVar.Value));

        EnemiesYetToSpawnNetVar.Value = enemiesForWave;
        TotalWaveEnemiesNetVar.Value = enemiesForWave;

        IsWaveActiveNetVar.Value = true;
        _isWaitingForNextWave = false;
    }

    private bool SpawnSingleEnemy(SpawnPool poolOverride, EnemyTier? forcedTier)
    {
        SpawnPool pool = GetEffectiveSpawnPool(poolOverride);

        EnemyDefinition enemyToSpawn = PickEnemyFromPool(pool);

        if (enemyToSpawn == null)
            return false;

        EnemySpawnPoint sp = GetSmartSpawnPoint();

        if (sp == null)
            return false;

        EnemyTier tier = forcedTier ?? CalculateTier(sp.ZoneDifficulty);
        float tierMult = GetTierMultiplier(tier);

        return SpawnEnemy(enemyToSpawn, tier, sp, tierMult);
    }

    private SpawnPool GetEffectiveSpawnPool(SpawnPool poolOverride)
    {
        if (poolOverride != null && poolOverride.IsUsable())
            return poolOverride;

        if (_currentPhase != null)
        {
            SpawnPool phasePool = _currentPhase.PickSpawnPool(_gameTimeMinutes);

            if (phasePool != null && phasePool.IsUsable())
                return phasePool;
        }

        if (_currentSpawnPool != null && _currentSpawnPool.IsUsable())
            return _currentSpawnPool;

        return null;
    }

    private EnemyDefinition PickEnemyFromPool(SpawnPool pool)
    {
        if (pool == null || pool.Enemies == null || pool.Enemies.Count == 0)
            return null;

        float totalWeight = 0f;

        for (int i = 0; i < pool.Enemies.Count; i++)
        {
            EnemySpawnWeight entry = pool.Enemies[i];

            if (entry == null)
                continue;

            if (!entry.IsAvailable(_gameTimeMinutes))
                continue;

            totalWeight += entry.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float currentWeight = 0f;

        for (int i = 0; i < pool.Enemies.Count; i++)
        {
            EnemySpawnWeight entry = pool.Enemies[i];

            if (entry == null)
                continue;

            if (!entry.IsAvailable(_gameTimeMinutes))
                continue;

            currentWeight += entry.Weight;

            if (roll <= currentWeight)
                return entry.EnemyDef;
        }

        return null;
    }

    private bool SpawnEnemy(EnemyDefinition def, EnemyTier tier, EnemySpawnPoint sp, float tierMulti)
    {
        if (def == null || def.Prefab == null || sp == null)
            return false;

        Vector2 circle = UnityEngine.Random.insideUnitCircle * sp.SpawnRadius;
        Vector3 pos = sp.transform.position + new Vector3(circle.x, 0f, circle.y);

        if (Physics.Raycast(
                pos + Vector3.up * _spawnRaycastHeight,
                Vector3.down,
                out RaycastHit hit,
                _spawnRaycastDistance,
                _terrainLayerMask))
        {
            pos = hit.point + Vector3.up * _spawnGroundOffset;
        }
        else
        {
            Debug.LogWarning(
                "DirectorSpawner: Nelze najít vhodnou pozici pro spawn nepřítele. " +
                "Zkontrolujte spawn point, terén a terrain layer.",
                this
            );

            return false;
        }

        NetworkObject netObj = NetworkObjectPool.Instance != null
            ? NetworkObjectPool.Instance.GetNetworkObject(def.Prefab, pos, Quaternion.identity)
            : null;

        if (netObj == null)
            return false;

        if (!netObj.IsSpawned)
            netObj.Spawn(true);

        if (netObj.TryGetComponent(out EnemyBaseAI ai))
        {
            float baseScale = 1.0f;
            float tierBonus = (tierMulti - 1.0f) * 0.2f;
            float zoneBonus = (sp.ZoneDifficulty - 1f) * 0.05f;

            float finalScale = Mathf.Clamp(
                baseScale + tierBonus + zoneBonus + UnityEngine.Random.Range(-0.1f, 0.1f),
                0.8f,
                3.0f
            );

            ai.InitializeEnemy(tier, def, finalScale, pos);
        }

        if (CurrentMode == SpawnerMode.Wave && EnemiesYetToSpawnNetVar.Value > 0)
            EnemiesYetToSpawnNetVar.Value--;

        EnemiesAliveNetVar.Value++;
        OnEnemySpawned?.Invoke();

        if (_debugSpawns)
            Debug.Log($"[DirectorSpawner] Spawned {def.name} as {tier} at {pos}.", this);

        return true;
    }

    private bool CheckIfPlayersAreActive()
    {
        if (NetworkManager.Singleton == null)
            return false;

        IReadOnlyList<NetworkClient> clients = NetworkManager.Singleton.ConnectedClientsList;

        if (clients == null || clients.Count == 0)
            return false;

        float sqrRadius = _safeZoneRadius * _safeZoneRadius;
        Vector3 directorPos = transform.position;

        for (int i = 0; i < clients.Count; i++)
        {
            NetworkObject playerObj = clients[i].PlayerObject;

            if (playerObj == null)
                continue;

            if ((directorPos - playerObj.transform.position).sqrMagnitude > sqrRadius)
                return true;
        }

        return false;
    }

    private EnemySpawnPoint GetSmartSpawnPoint()
    {
        if (NetworkManager.Singleton == null)
            return null;

        IReadOnlyList<NetworkClient> clients = NetworkManager.Singleton.ConnectedClientsList;

        if (clients == null || clients.Count == 0 || _spawnPoints.Count == 0)
            return null;

        NetworkObject playerObj = clients[UnityEngine.Random.Range(0, clients.Count)].PlayerObject;

        if (playerObj == null)
            return null;

        Vector3 playerPos = playerObj.transform.position;
        float minSqr = _minSpawnDistanceFromPlayer * _minSpawnDistanceFromPlayer;
        float maxSqr = _maxSpawnDistanceFromPlayer * _maxSpawnDistanceFromPlayer;

        _validPointsBuffer.Clear();

        foreach (EnemySpawnPoint sp in _spawnPoints)
        {
            if (sp == null || !sp.gameObject.activeInHierarchy)
                continue;

            float distSqr = (sp.transform.position - playerPos).sqrMagnitude;

            if (distSqr >= minSqr && distSqr <= maxSqr)
                _validPointsBuffer.Add(sp);
        }

        // 1. Priorita: Vybrat náhodný bod ve správné vzdálenosti
        if (_validPointsBuffer.Count > 0)
            return _validPointsBuffer[UnityEngine.Random.Range(0, _validPointsBuffer.Count)];

        // 2. Fallback: Vybrat náhodný bod z celkového poolu (místo fixního prvního prvku)
        int randomIndex = UnityEngine.Random.Range(0, _spawnPoints.Count);
        int currentIndex = 0;

        foreach (EnemySpawnPoint sp in _spawnPoints)
        {
            if (currentIndex == randomIndex)
                return sp;
            currentIndex++;
        }

        return null;
    }

    private EnemyTier CalculateTier(float zoneDifficulty)
    {
        float roll = UnityEngine.Random.value;

        float zoneFactor = zoneDifficulty * 0.005f;

        float eliteMultiplier = _currentPhase != null
            ? Mathf.Max(0f, _currentPhase.EliteChanceMultiplier)
            : 1f;

        float championMultiplier = _currentPhase != null
            ? Mathf.Max(0f, _currentPhase.ChampionChanceMultiplier)
            : 1f;

        float championChance = Mathf.Clamp(
            (_championChance * championMultiplier) + (zoneFactor * 0.2f),
            0f,
            0.15f
        );

        float eliteChance = Mathf.Clamp(
            (_eliteChance * eliteMultiplier) + zoneFactor,
            0f,
            0.6f
        );

        // Původní verze tady vracela Boss pro champion roll.
        // To je moc brutální pro běžný spawn. Boss necháme pro speciální eventy.
        if (roll < championChance)
            return EnemyTier.Champion;

        if (roll < championChance + eliteChance)
            return EnemyTier.Elite;

        return EnemyTier.Normal;
    }

    private float GetTierMultiplier(EnemyTier tier)
    {
        return tier switch
        {
            EnemyTier.Elite => 2.5f,
            EnemyTier.Champion => 6.0f,
            EnemyTier.Boss => 25.0f,
            _ => 1.0f
        };
    }

    private void TriggerDemoVictory()
    {
        _hasGameStarted = false;
        AwardStatToAllConnectedPlayers(SteamStatIds.DemoVictories, 1);
        ShowVictoryScreenClientRpc();
    }

    [ClientRpc]
    private void ShowVictoryScreenClientRpc()
    {
        Debug.Log("[Client] All waves completed! Triggering Victory Screen.");

        if (EndScreenUI.Instance != null)
        {
            EndScreenUI.Instance.Show(
                "VICTORY!",
                "You have survived all waves and completed the demo.\nThank you for playing!"
            );
        }
        else
        {
            Debug.LogError("EndScreenUI Instance is missing from the scene!");
        }
    }

    public void SetActivePool(SpawnPool newPool)
    {
        _currentSpawnPool = newPool;

        if (_currentSpawnPool != null)
            _currentSpawnPool.GetTotalWeight(true);
    }

    public void SetDifficultyProfile(DifficultyProfile newProfile)
    {
        _currentDifficultyProfile = newProfile;
        _currentPhaseIndex = -999;
        _currentPhase = null;
    }

    public float GetRunTimeMinutes()
    {
        return _gameTimeMinutes;
    }

    public DangerRhythmPhase GetCurrentPhase()
    {
        return _currentPhase;
    }
}