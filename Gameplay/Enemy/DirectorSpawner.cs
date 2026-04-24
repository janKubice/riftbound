using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System;
using System.Collections;

public class DirectorSpawner : NetworkBehaviour
{
    public static DirectorSpawner Instance { get; private set; }

    public enum SpawnerMode { Continuous, Wave }

    [Header("Mode Settings")]
    [Tooltip("Určuje, zda se hraje normální přežití nebo aréna (vlny).")]
    public SpawnerMode CurrentMode = SpawnerMode.Continuous;

    [Header("Difficulty")]
    [SerializeField] private DifficultyProfile _currentDifficultyProfile;

    [Header("Active Spawn Pool")]
    [SerializeField] private SpawnPool _currentSpawnPool;

    [Header("Stats Scaling")]
    [SerializeField] private float _exponentialScalingFactor = 1.1f;

    [Header("Performance Limits")]
    [SerializeField] private int _maxSpawnsPerFrame = 5;

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
    [SerializeField] private float _wavePowerScale = 0.1f;      // Jak moc se zvyšuje síla nepřátel specificky s vlnou
    public NetworkVariable<float> WaveCountdownNetVar = new NetworkVariable<float>(0f);

    // --- Vnitřní stavy ---
    private float _gameTimeMinutes = 0;
    private float _difficultyMultiplier = 1.0f;
    private float _spawnAccumulator = 0f;

    private bool _hasGameStarted = false;
    private float _lastPlayerCheckTime;
    private bool _arePlayersActive = false;
    private bool _isWaitingForNextWave = false;

    private HashSet<EnemySpawnPoint> _spawnPoints = new HashSet<EnemySpawnPoint>();
    private List<EnemySpawnPoint> _validPointsBuffer = new List<EnemySpawnPoint>(50);
    private int _terrainLayerMask;

    // --- Network Variables (pro synchronizaci s UI) ---
    public NetworkVariable<int> EnemiesAliveNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> CurrentWaveNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> EnemiesYetToSpawnNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<int> TotalWaveEnemiesNetVar = new NetworkVariable<int>(0);
    public NetworkVariable<bool> IsWaveActiveNetVar = new NetworkVariable<bool>(false);
    public NetworkVariable<int> CurrentDifficultyPercent = new NetworkVariable<int>(100);

    public static event Action OnEnemySpawned;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _terrainLayerMask = LayerMask.GetMask("Enviroment");
    }

    public void RegisterSpawnPoint(EnemySpawnPoint sp) => _spawnPoints.Add(sp);
    public void UnregisterSpawnPoint(EnemySpawnPoint sp) => _spawnPoints.Remove(sp);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            EnemiesAliveNetVar.Value = 0;
            CurrentWaveNetVar.Value = 0;
            IsWaveActiveNetVar.Value = false;
        }
    }

    // Voláno z EnemyHealth.cs při smrti
    public void EnemyDied()
    {
        if (IsServer)
        {
            EnemiesAliveNetVar.Value = Mathf.Max(0, EnemiesAliveNetVar.Value - 1);
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        if (Time.time >= _lastPlayerCheckTime + _checkPlayersInterval)
        {
            _lastPlayerCheckTime = Time.time;
            _arePlayersActive = CheckIfPlayersAreActive();

            if (!_hasGameStarted && _arePlayersActive)
            {
                _hasGameStarted = true;
                if (CurrentMode == SpawnerMode.Wave)
                {
                    StartCoroutine(PrepareNextWaveRoutine());
                }
            }
        }

        if (!_hasGameStarted || (_canPauseGame && !_arePlayersActive)) return;

        _gameTimeMinutes += (Time.deltaTime / 60f);
        _difficultyMultiplier = Mathf.Pow(_exponentialScalingFactor, _gameTimeMinutes);

        // Update difficulty pro UI
        CurrentDifficultyPercent.Value = Mathf.FloorToInt(_difficultyMultiplier * 100f);

        if (CurrentMode == SpawnerMode.Continuous)
        {
            ProcessContinuousSpawning();
        }
        else if (CurrentMode == SpawnerMode.Wave)
        {
            ProcessWaveSpawning();
        }
    }

    private void ProcessContinuousSpawning()
    {
        float currentSpawnRate = _currentDifficultyProfile.SpawnRateCurve.Evaluate(_gameTimeMinutes);
        int currentMaxEnemies = Mathf.RoundToInt(_currentDifficultyProfile.MaxEnemiesCurve.Evaluate(_gameTimeMinutes));

        _spawnAccumulator += currentSpawnRate * Time.deltaTime;

        if (EnemiesAliveNetVar.Value >= currentMaxEnemies)
        {
            _spawnAccumulator = Mathf.Lerp(_spawnAccumulator, 0, Time.deltaTime * 0.3f);
            return;
        }

        int spawnsThisFrame = 0;
        while (_spawnAccumulator >= 1.0f && spawnsThisFrame < _maxSpawnsPerFrame && EnemiesAliveNetVar.Value < currentMaxEnemies)
        {
            SpawnSingleEnemy();
            _spawnAccumulator -= 1.0f;
            spawnsThisFrame++;
        }
    }

    private void ProcessWaveSpawning()
    {
        if (!IsWaveActiveNetVar.Value || _isWaitingForNextWave) return;

        if (EnemiesYetToSpawnNetVar.Value > 0)
        {
            _spawnAccumulator += _waveSpawnRate * Time.deltaTime;
            int spawnsThisFrame = 0;

            while (_spawnAccumulator >= 1.0f &&
                   spawnsThisFrame < _maxSpawnsPerFrame &&
                   EnemiesYetToSpawnNetVar.Value > 0)
            {
                SpawnSingleEnemy();
                _spawnAccumulator -= 1.0f;
                spawnsThisFrame++;
            }
        }
        else if (EnemiesAliveNetVar.Value <= 0)
        {
            IsWaveActiveNetVar.Value = false;

            // --- MODIFIED LOGIC: Check for Victory ---
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
        while (timer > 0)
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

    private void SpawnSingleEnemy()
    {
        EnemyDefinition enemyToSpawn = PickEnemyFromPool(_currentSpawnPool);
        if (enemyToSpawn == null) return;

        EnemySpawnPoint sp = GetSmartSpawnPoint();
        if (sp == null) return;

        EnemyTier tier = CalculateTier(sp.ZoneDifficulty);
        float tierMult = GetTierMultiplier(tier);

        SpawnEnemy(enemyToSpawn, tier, sp, tierMult);
    }

    // --- Zbytek původních metod (PickEnemyFromPool, SpawnEnemy, CheckIfPlayersAreActive, GetSmartSpawnPoint, CalculateTier, GetTierMultiplier) zůstává beze změny ---

    private EnemyDefinition PickEnemyFromPool(SpawnPool pool)
    {
        if (pool == null || pool.Enemies.Count == 0) return null;
        float roll = UnityEngine.Random.Range(0, pool.GetTotalWeight());
        float currentWeight = 0;
        for (int i = 0; i < pool.Enemies.Count; i++)
        {
            currentWeight += pool.Enemies[i].Weight;
            if (roll <= currentWeight) return pool.Enemies[i].EnemyDef;
        }
        return pool.Enemies[0].EnemyDef;
    }

    private void SpawnEnemy(EnemyDefinition def, EnemyTier tier, EnemySpawnPoint sp, float tierMulti)
    {
        Vector2 circle = UnityEngine.Random.insideUnitCircle * sp.SpawnRadius;
        Vector3 pos = sp.transform.position + new Vector3(circle.x, 0, circle.y);

        if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out RaycastHit hit, 40f, _terrainLayerMask))
        {
            // Přidáme 0.2f k ose Y, aby collider nezačínal "vnořený" do země
            pos = hit.point + Vector3.up * 0.2f;
        }
        else
        {
            Debug.LogWarning("DirectorSpawner: Nelze najít vhodnou pozici pro spawn nepřítele. Zkontrolujte nastavení spawn pointů a terénu.");
            return;
        }

        OnEnemySpawned?.Invoke();
        NetworkObject netObj = NetworkObjectPool.Instance != null ? NetworkObjectPool.Instance.GetNetworkObject(def.Prefab, pos, Quaternion.identity) : null;

        if (netObj != null)
        {
            if (!netObj.IsSpawned) netObj.Spawn(true);
            if (netObj.TryGetComponent(out EnemyBaseAI ai))
            {
                float timeMulti = _difficultyMultiplier;
                float zoneMulti = Mathf.Sqrt(sp.ZoneDifficulty);
                float waveMulti = (CurrentMode == SpawnerMode.Wave) ? (1f + (CurrentWaveNetVar.Value * 0.07f)) : 1f;
                float powerFactor = tierMulti * timeMulti * zoneMulti * waveMulti;

                int hp = Mathf.RoundToInt(def.BaseHealth * powerFactor);
                int dmg = Mathf.RoundToInt(def.BaseDamage * powerFactor);
                int xp = Mathf.CeilToInt(def.BaseXPDrop * (powerFactor * 0.8f));
                float speed = def.BaseSpeed * (1 + (timeMulti * 0.03f) + (tierMulti * 0.05f));
                float atkRate = def.BaseAttackRate * (1 + Mathf.Clamp((tierMulti - 1) * 0.2f, 0, 0.5f));
                float kbRes = def.BaseKnockbackResistance + (1 - def.BaseKnockbackResistance) * (1 - (1 / tierMulti));

                float baseScale = 1.0f;
                float tierBonus = (tierMulti - 1.0f) * 0.2f;
                float zoneBonus = (sp.ZoneDifficulty - 1) * 0.05f;
                float finalScale = Mathf.Clamp(baseScale + tierBonus + zoneBonus + UnityEngine.Random.Range(-0.1f, 0.1f), 0.8f, 3.0f);

                ai.InitializeEnemy(tier, hp, dmg, speed, finalScale, atkRate, kbRes, xp, pos);
            }
            EnemiesYetToSpawnNetVar.Value--;
            EnemiesAliveNetVar.Value++;
        }
    }

    private bool CheckIfPlayersAreActive()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count == 0) return false;
        float sqrRadius = _safeZoneRadius * _safeZoneRadius;
        Vector3 directorPos = transform.position;
        for (int i = 0; i < clients.Count; i++)
        {
            var playerObj = clients[i].PlayerObject;
            if (playerObj != null && (directorPos - playerObj.transform.position).sqrMagnitude > sqrRadius) return true;
        }
        return false;
    }

    private EnemySpawnPoint GetSmartSpawnPoint()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count == 0 || _spawnPoints.Count == 0) return null;
        var playerObj = clients[UnityEngine.Random.Range(0, clients.Count)].PlayerObject;
        if (playerObj == null) return null;

        Vector3 playerPos = playerObj.transform.position;
        _validPointsBuffer.Clear();

        foreach (var sp in _spawnPoints)
        {
            if (!sp.gameObject.activeSelf) continue;
            float distSqr = (sp.transform.position - playerPos).sqrMagnitude;
            if (distSqr > 225f && distSqr < 2500f) _validPointsBuffer.Add(sp);
        }

        if (_validPointsBuffer.Count > 0) return _validPointsBuffer[UnityEngine.Random.Range(0, _validPointsBuffer.Count)];
        using (var enumerator = _spawnPoints.GetEnumerator()) if (enumerator.MoveNext()) return enumerator.Current;
        return null;
    }

    private EnemyTier CalculateTier(float zoneDifficulty)
    {
        float roll = UnityEngine.Random.value;
        float zoneFactor = zoneDifficulty * 0.005f;
        if (roll < Mathf.Clamp(_championChance + (zoneFactor * 0.2f), 0f, 0.15f)) return EnemyTier.Boss;
        if (roll < Mathf.Clamp(_championChance + (zoneFactor * 0.2f), 0f, 0.15f) + Mathf.Clamp(_eliteChance + zoneFactor, 0f, 0.6f)) return EnemyTier.Elite;
        return EnemyTier.Normal;
    }

    /// <summary>
    /// Stops the game loop and notifies all clients of the victory.
    /// </summary>
    private void TriggerDemoVictory()
    {
        _hasGameStarted = false; // Stops difficulty scaling and further updates

        // Notify all clients to show the end screen
        ShowVictoryScreenClientRpc();
    }

    [ClientRpc]
    private void ShowVictoryScreenClientRpc()
    {
        Debug.Log("[Client] All waves completed! Triggering Victory Screen.");

        if (EndScreenUI.Instance != null)
        {
            EndScreenUI.Instance.Show("VICTORY!", "You have survived all waves and completed the demo.\nThank you for playing!");
        }
        else
        {
            Debug.LogError("EndScreenUI Instance is missing from the scene!");
        }
    }

    private float GetTierMultiplier(EnemyTier tier) => tier switch { EnemyTier.Elite => 2.5f, EnemyTier.Champion => 6.0f, EnemyTier.Boss => 25.0f, _ => 1.0f };

    public void SetActivePool(SpawnPool newPool) { _currentSpawnPool = newPool; _currentSpawnPool.GetTotalWeight(); }
    public void SetDifficultyProfile(DifficultyProfile newProfile) { _currentDifficultyProfile = newProfile; }
}