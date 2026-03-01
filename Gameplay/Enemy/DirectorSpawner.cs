using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using TMPro;

public class DirectorSpawner : NetworkBehaviour
{
    public static DirectorSpawner Instance { get; private set; }

    [Header("Enemy Database")]
    [SerializeField] private List<EnemyDefinition> _allEnemies;

    [Header("Game Pace")]
    [SerializeField] private float _baseCreditsPerSecond = 1.0f;
    [SerializeField] private float _difficultyScaling = 0.1f;
    [SerializeField] private int _maxEnemiesAlive = 200;
    
    [Header("Scaling Curve")]
    [SerializeField] private float _exponentialScalingFactor = 1.1f;

    [Header("Performance Limits")]
    [SerializeField] private int _maxSpawnsPerFrame = 2;

    [Header("Safe Zone")]
    [SerializeField] private float _safeZoneRadius = 15.0f;
    [SerializeField] private bool _canPauseGame = false;
    [SerializeField] private float _checkPlayersInterval = 1.0f;

    [Header("Tiers")]
    [Range(0, 1)][SerializeField] private float _eliteChance = 0.1f;
    [Range(0, 1)][SerializeField] private float _championChance = 0.02f;

    private float _accumulatedCredits = 0;
    private float _totalCredits = 0;
    private float _gameTime = 0;
    private float _difficultyMultiplier = 1.0f;
    private bool _hasGameStarted = false;
    private float _lastPlayerCheckTime;
    private bool _arePlayersActive = false;
    
    [SerializeField] private TextMeshProUGUI diffText;
    private int _lastDisplayedDifficulty = -1;

    private HashSet<EnemySpawnPoint> _spawnPoints = new HashSet<EnemySpawnPoint>();
    private List<EnemySpawnPoint> _validPointsBuffer = new List<EnemySpawnPoint>(50);
    private NetworkVariable<int> _enemiesAliveNetVar = new NetworkVariable<int>(0);

    private EnemyDefinition[] _cachedEnemiesList;
    private int _terrainLayerMask;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        _terrainLayerMask = LayerMask.GetMask("Default", "Terrain");
    }

    public void RegisterSpawnPoint(EnemySpawnPoint sp) => _spawnPoints.Add(sp);
    public void UnregisterSpawnPoint(EnemySpawnPoint sp) => _spawnPoints.Remove(sp);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _enemiesAliveNetVar.Value = 0;
            
            // Seřazení a cachování do pole pro zamezení LINQ volání v Update
            _allEnemies.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            _cachedEnemiesList = _allEnemies.ToArray();
        }
    }

    public void EnemyDied()
    {
        if (IsServer)
        {
            _enemiesAliveNetVar.Value = Mathf.Max(0, _enemiesAliveNetVar.Value - 1);
        }
    }

    private void Update()
    {
        if (!IsServer) return;

        if (Time.time >= _lastPlayerCheckTime + _checkPlayersInterval)
        {
            _lastPlayerCheckTime = Time.time;
            _arePlayersActive = CheckIfPlayersAreActive();

            if (!_hasGameStarted && _arePlayersActive) _hasGameStarted = true;
        }

        if (!_hasGameStarted || (_canPauseGame && !_arePlayersActive)) return;

        float dt = Time.deltaTime;
        _gameTime += dt;
        float minutes = _gameTime / 60f;

        _difficultyMultiplier = Mathf.Pow(_exponentialScalingFactor, minutes);

        float waveMultiplier = 1.0f + (Mathf.Sin(Time.time * 0.1f) * 0.5f);
        float creditsIncome = _baseCreditsPerSecond * _difficultyMultiplier * dt * waveMultiplier;

        _accumulatedCredits += creditsIncome;
        _totalCredits += creditsIncome;

        ProcessSpawnQueue();
        UpdateUI();
    }

    private void ProcessSpawnQueue()
    {
        if (_enemiesAliveNetVar.Value >= _maxEnemiesAlive)
        {
            _accumulatedCredits = Mathf.Lerp(_accumulatedCredits, 0, Time.deltaTime * 0.3f);
            return;
        }

        int spawnsThisFrame = 0;

        while (_accumulatedCredits > 0 &&
               spawnsThisFrame < _maxSpawnsPerFrame &&
               _enemiesAliveNetVar.Value < _maxEnemiesAlive)
        {
            EnemyDefinition enemyToSpawn = PickAffordableEnemy(_accumulatedCredits);
            if (enemyToSpawn == null) break;

            EnemySpawnPoint sp = GetSmartSpawnPoint();
            if (sp == null) break;

            EnemyTier tier = CalculateTier(sp.ZoneDifficulty);
            float tierMult = GetTierMultiplier(tier);
            float finalCost = enemyToSpawn.Cost * tierMult;

            if (_accumulatedCredits < finalCost && tier != EnemyTier.Normal)
            {
                tier = EnemyTier.Normal;
                tierMult = 1.0f;
                finalCost = enemyToSpawn.Cost;
            }

            if (_accumulatedCredits >= finalCost)
            {
                SpawnEnemy(enemyToSpawn, tier, sp, tierMult);
                _accumulatedCredits -= finalCost;
                spawnsThisFrame++;
            }
            else
            {
                break;
            }
        }
    }

    private EnemyDefinition PickAffordableEnemy(float budget)
    {
        int totalWeight = 0;
        int validCount = 0;

        // O(n) iterace přes pole, ukončí se jakmile narazí na dražší entitu (pole je seřazené)
        for (int i = 0; i < _cachedEnemiesList.Length; i++)
        {
            if (_cachedEnemiesList[i].Cost <= budget)
            {
                totalWeight += (int)_cachedEnemiesList[i].Rarity;
                validCount++;
            }
            else break;
        }

        if (validCount == 0) return null;

        int roll = Random.Range(0, totalWeight);
        int current = 0;

        for (int i = 0; i < validCount; i++)
        {
            current += (int)_cachedEnemiesList[i].Rarity;
            if (roll < current) return _cachedEnemiesList[i];
        }

        return _cachedEnemiesList[0];
    }

    private void SpawnEnemy(EnemyDefinition def, EnemyTier tier, EnemySpawnPoint sp, float tierMulti)
    {
        Vector2 circle = Random.insideUnitCircle * sp.SpawnRadius;
        Vector3 pos = sp.transform.position + new Vector3(circle.x, 0, circle.y);

        if (Physics.Raycast(pos + Vector3.up * 10, Vector3.down, out RaycastHit hit, 20f, _terrainLayerMask))
        {
            pos = hit.point;
        }

        NetworkObject netObj = NetworkObjectPool.Instance != null 
            ? NetworkObjectPool.Instance.GetNetworkObject(def.Prefab, pos, Quaternion.identity) 
            : null;

        if (netObj != null)
        {
            if (!netObj.IsSpawned) netObj.Spawn(true);

            if (netObj.TryGetComponent(out EnemyBaseAI ai))
            {
                float timeMulti = _difficultyMultiplier;
                float zoneMulti = Mathf.Sqrt(sp.ZoneDifficulty);
                float powerFactor = tierMulti * timeMulti * zoneMulti;

                int hp = Mathf.RoundToInt(def.BaseHealth * powerFactor);
                int dmg = Mathf.RoundToInt(def.BaseDamage * powerFactor);
                int xp = Mathf.CeilToInt(def.BaseXPDrop * (powerFactor * 0.8f));
                
                float speed = def.BaseSpeed * (1 + (timeMulti * 0.03f) + (tierMulti * 0.05f));
                float atkRate = def.BaseAttackRate * (1 + Mathf.Clamp((tierMulti - 1) * 0.2f, 0, 0.5f));
                float kbRes = def.BaseKnockbackResistance + (1 - def.BaseKnockbackResistance) * (1 - (1 / tierMulti));

                float baseScale = 1.0f;
                float tierBonus = (tierMulti - 1.0f) * 0.2f;
                float zoneBonus = (sp.ZoneDifficulty - 1) * 0.05f;
                float randomJitter = Random.Range(-0.1f, 0.1f);
                float finalScale = Mathf.Clamp(baseScale + tierBonus + zoneBonus + randomJitter, 0.8f, 3.0f);

                ai.InitializeEnemy(tier, hp, dmg, speed, finalScale, atkRate, kbRes, xp, pos);
            }

            _enemiesAliveNetVar.Value++;
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
            if (playerObj != null)
            {
                if ((directorPos - playerObj.transform.position).sqrMagnitude > sqrRadius) return true;
            }
        }
        return false;
    }

    private EnemySpawnPoint GetSmartSpawnPoint()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count == 0 || _spawnPoints.Count == 0) return null;

        var playerObj = clients[Random.Range(0, clients.Count)].PlayerObject;
        if (playerObj == null) return null;
        
        Vector3 playerPos = playerObj.transform.position;
        _validPointsBuffer.Clear();

        float minDstSqr = 225f; 
        float maxDstSqr = 2500f; 

        foreach (var sp in _spawnPoints)
        {
            if (!sp.gameObject.activeSelf) continue;

            float distSqr = (sp.transform.position - playerPos).sqrMagnitude;
            if (distSqr > minDstSqr && distSqr < maxDstSqr)
            {
                _validPointsBuffer.Add(sp);
            }
        }

        if (_validPointsBuffer.Count > 0)
        {
            return _validPointsBuffer[Random.Range(0, _validPointsBuffer.Count)];
        }

        using (var enumerator = _spawnPoints.GetEnumerator())
        {
            if (enumerator.MoveNext()) return enumerator.Current;
        }

        return null;
    }

    private EnemyTier CalculateTier(float zoneDifficulty)
    {
        float roll = Random.value;
        float zoneFactor = zoneDifficulty * 0.005f;

        float finalEliteChance = Mathf.Clamp(_eliteChance + zoneFactor, 0f, 0.6f);
        float finalChampionChance = Mathf.Clamp(_championChance + (zoneFactor * 0.2f), 0f, 0.15f);

        if (roll < finalChampionChance) return EnemyTier.Boss;
        if (roll < finalChampionChance + finalEliteChance) return EnemyTier.Elite;

        return EnemyTier.Normal;
    }

    private float GetTierMultiplier(EnemyTier tier)
    {
        return tier switch
        {
            EnemyTier.Elite => 2.5f,
            EnemyTier.Champion => 6.0f,
            EnemyTier.Boss => 25.0f,
            _ => 1.0f,
        };
    }

    private void UpdateUI()
    {
        if (diffText == null) return;

        int currentDiff = Mathf.FloorToInt(_totalCredits);
        if (currentDiff != _lastDisplayedDifficulty)
        {
            _lastDisplayedDifficulty = currentDiff;
            diffText.SetText("Difficulty: <color=red>{0:F1}</color>", _totalCredits);
        }
    }
}