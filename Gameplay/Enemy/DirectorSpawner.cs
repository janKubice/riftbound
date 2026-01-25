using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;

public class DirectorSpawner : NetworkBehaviour
{
    public static DirectorSpawner Instance { get; private set; }

    [Header("Enemy Database")]
    [SerializeField] private List<EnemyDefinition> _allEnemies;

    [Header("Game Pace")]
    [SerializeField] private float _baseCreditsPerSecond = 1.0f;
    [SerializeField] private float _difficultyScaling = 0.1f;
    [SerializeField] private int _maxEnemiesAlive = 200;

    [Header("Performance Limits")]
    [Tooltip("Maximální počet spawnu za jeden frame (brání zásekům).")]
    [SerializeField] private int _maxSpawnsPerFrame = 2;

    [Header("Safe Zone")]
    [SerializeField] private float _safeZoneRadius = 15.0f;
    [SerializeField] private bool _canPauseGame = false;
    [SerializeField] private float _checkPlayersInterval = 1.0f; // Kontrola hráčů jen 1x za sekundu

    [Header("Tiers")]
    [Range(0, 1)][SerializeField] private float _eliteChance = 0.1f;
    [Range(0, 1)][SerializeField] private float _championChance = 0.02f;

    // Runtime stav
    private float _accumulatedCredits = 0;
    private float _gameTime = 0;
    private float _difficultyMultiplier = 1.0f;
    private bool _hasGameStarted = false;
    private float _lastPlayerCheckTime;
    private bool _arePlayersActive = false;

    // Kolekce
    private HashSet<EnemySpawnPoint> _spawnPoints = new HashSet<EnemySpawnPoint>();
    private List<EnemySpawnPoint> _validPointsBuffer = new List<EnemySpawnPoint>(50);

    // Čítač živých (lepší než hledat objekty)
    private NetworkVariable<int> _enemiesAliveNetVar = new NetworkVariable<int>(0);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // --- Registrace SpawnPointů (Volají samy SpawnPointy v Start) ---
    public void RegisterSpawnPoint(EnemySpawnPoint sp) => _spawnPoints.Add(sp);
    public void UnregisterSpawnPoint(EnemySpawnPoint sp) => _spawnPoints.Remove(sp);

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _enemiesAliveNetVar.Value = 0;
            _allEnemies = _allEnemies.OrderBy(x => x.Cost).ToList();

            var existingPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None);
            foreach (var point in existingPoints)
            {
                RegisterSpawnPoint(point);
            }
            // -------------------------------------------------------------
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

        // 1. Logika Safe Zóny (Throttled check)
        if (Time.time >= _lastPlayerCheckTime + _checkPlayersInterval)
        {
            _lastPlayerCheckTime = Time.time;
            _arePlayersActive = CheckIfPlayersAreActive();

            if (!_hasGameStarted && _arePlayersActive) _hasGameStarted = true;
        }

        // Pokud hra stojí, nic neděláme
        if (!_hasGameStarted) return;
        if (_canPauseGame && !_arePlayersActive) return;

        // 2. Progrese Hry
        float dt = Time.deltaTime;
        _gameTime += dt;

        // Vzorec: Obtížnost roste s časem
        _difficultyMultiplier = 1.0f + (_gameTime * _difficultyScaling / 60f);

        // Přísun kreditů
        _accumulatedCredits += _baseCreditsPerSecond * _difficultyMultiplier * dt;

        // 3. Spawnování (Rozložené v čase)
        ProcessSpawnQueue();
    }

    private void ProcessSpawnQueue()
    {
        // Pokud máme dost nepřátel, šetříme kredity (nebo je můžeme zastropovat)
        if (_enemiesAliveNetVar.Value >= _maxEnemiesAlive) return;

        int spawnsThisFrame = 0;

        // "While" je zde bezpečné, protože je omezeno _maxSpawnsPerFrame (např. 2)
        // Nikdy nezasekne hru na více než pár milisekund.
        while (_accumulatedCredits > 0 &&
               spawnsThisFrame < _maxSpawnsPerFrame &&
               _enemiesAliveNetVar.Value < _maxEnemiesAlive)
        {
            // Zkusíme vybrat nepřítele, na kterého máme
            EnemyDefinition enemyToSpawn = PickAffordableEnemy(_accumulatedCredits);

            // Pokud nemáme ani na nejlevnějšího, končíme pro tento frame a šetříme dál
            if (enemyToSpawn == null) break;

            EnemySpawnPoint sp = GetSmartSpawnPoint();
            if (sp == null) break; // Není kde spawnovat

            // Kalkulace Tieru
            EnemyTier tier = CalculateTier(sp.ZoneDifficulty);
            float tierMult = GetTierMultiplier(tier);
            float finalCost = enemyToSpawn.Cost * tierMult;

            // Double check ceny (kvůli tieru)
            if (_accumulatedCredits >= finalCost)
            {
                SpawnEnemy(enemyToSpawn, tier, sp);
                _accumulatedCredits -= finalCost;
                spawnsThisFrame++;
            }
            else
            {
                // Máme na základní verzi, ale ne na Elite verzi. 
                // Buď přeskočíme, nebo spawneme Normal verzi. Zde počkáme na víc kreditů.
                break;
            }
        }
    }

    // Optimalizovaný výběr nepřítele (nevybíráme to, na co nemáme)
    private EnemyDefinition PickAffordableEnemy(float budget)
    {
        // _allEnemies je seřazený podle ceny.
        // Najdeme všechny, které si můžeme dovolit.
        var affordable = _allEnemies.Where(e => e.Cost <= budget).ToList();

        if (affordable.Count == 0) return null;

        // Z těch dostupných vybereme váženým náhodným výběrem
        return WeightedRandomPick(affordable);
    }

    private EnemyDefinition WeightedRandomPick(List<EnemyDefinition> candidates)
    {
        int totalWeight = candidates.Sum(e => (int)e.Rarity);
        int roll = Random.Range(0, totalWeight);
        int current = 0;

        foreach (var e in candidates)
        {
            current += (int)e.Rarity;
            if (roll < current) return e;
        }
        return candidates[0];
    }

    private void SpawnEnemy(EnemyDefinition def, EnemyTier tier, EnemySpawnPoint sp)
    {
        Vector2 circle = Random.insideUnitCircle * sp.SpawnRadius;
        Vector3 pos = sp.transform.position + new Vector3(circle.x, 0, circle.y);

        // Raycast pro usazení na zem
        if (Physics.Raycast(pos + Vector3.up * 10, Vector3.down, out RaycastHit hit, 20f, LayerMask.GetMask("Default", "Terrain")))
        {
            pos = hit.point;
        }

        // --- POOLING SYSTEM ---
        NetworkObject netObj = null;

        if (NetworkObjectPool.Instance != null)
        {
            netObj = NetworkObjectPool.Instance.GetNetworkObject(def.Prefab, pos, Quaternion.identity);
        }
        else
        {
            // Fallback (Varování v konzoli by bylo vhodné)
            var go = Instantiate(def.Prefab, pos, Quaternion.identity);
            netObj = go.GetComponent<NetworkObject>();
        }

        if (netObj != null)
        {
            if (!netObj.IsSpawned) netObj.Spawn(true);

            // Inicializace AI
            if (netObj.TryGetComponent(out EnemyBaseAI ai))
            {
                float tierMulti = GetTierMultiplier(tier);
                // Vzorec pro staty
                float totalStatMultiplier = tierMulti * _difficultyMultiplier;

                int hp = Mathf.RoundToInt(def.BaseHealth * totalStatMultiplier);
                int dmg = Mathf.RoundToInt(def.BaseDamage * totalStatMultiplier);

                // Speed škálujeme méně agresivně
                float speed = def.BaseSpeed * (1 + (tierMulti * 0.1f)) * (1 + (_gameTime * 0.001f)); // Velmi pomalý nárůst rychlosti časem
                float scale = 1.0f + (tierMulti * 0.15f);

                ai.InitializeEnemy(tier, hp, dmg, speed, scale, tier, pos);
            }

            _enemiesAliveNetVar.Value++;
        }
    }

    // --- UTILITIES ---

    private bool CheckIfPlayersAreActive()
    {
        if (NetworkManager.Singleton.ConnectedClientsList.Count == 0) return false;

        // Optimalizace: Použití sqrMagnitude
        float sqrRadius = _safeZoneRadius * _safeZoneRadius;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                float sqrDist = (transform.position - client.PlayerObject.transform.position).sqrMagnitude;
                if (sqrDist > sqrRadius) return true; // Hráč je venku
            }
        }
        return false;
    }

    private EnemySpawnPoint GetSmartSpawnPoint()
    {
        if (NetworkManager.Singleton.ConnectedClientsList.Count == 0) return null;
        if (_spawnPoints.Count == 0) return null;

        // Náhodný hráč
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        var playerObj = clients[Random.Range(0, clients.Count)].PlayerObject;

        if (playerObj == null) return null;
        Vector3 playerPos = playerObj.transform.position;

        _validPointsBuffer.Clear();

        // Jednoduchá filtrace vzdálenosti (15m - 50m)
        float minDstSqr = 225f; // 15^2
        float maxDstSqr = 2500f; // 50^2

        foreach (var sp in _spawnPoints)
        {
            // Ignorujeme vypnuté body
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

        // Fallback: Pokud není nic v ideální vzdálenosti, vezmi jakýkoliv aktivní
        return _spawnPoints.FirstOrDefault();
    }

    private EnemyTier CalculateTier(float zoneDifficulty)
    {
        float roll = Random.value;
        // Příklad: zoneDiff 1.0 = normal, zoneDiff 2.0 = double chance
        if (roll < _championChance * zoneDifficulty) return EnemyTier.Boss;
        if (roll < _eliteChance * zoneDifficulty) return EnemyTier.Elite;
        return EnemyTier.Normal;
    }

    private float GetTierMultiplier(EnemyTier tier)
    {
        switch (tier)
        {
            case EnemyTier.Elite: return 2.5f;
            case EnemyTier.Champion: return 6.0f;
            case EnemyTier.Boss: return 25.0f;
            default: return 1.0f;
        }
    }

    private void OnGUI()
    {
        if (!IsServer) return;

        GUILayout.BeginArea(new Rect(10, 10, 300, 500));
        GUILayout.Box("DIRECTOR DEBUG");
        GUILayout.Label($"State: {(_hasGameStarted ? "RUNNING" : "WAITING FOR PLAYER MOVE")}");
        GUILayout.Label($"Credits: {_accumulatedCredits:F1}");
        GUILayout.Label($"Alive: {_enemiesAliveNetVar.Value}/{_maxEnemiesAlive}");
        GUILayout.Label($"Registered SpawnPoints: {_spawnPoints.Count}");

        if (!_hasGameStarted)
        {
            GUILayout.Label("MOVE AWAY FROM CENTER TO START!");
        }
        GUILayout.EndArea();
    }
}