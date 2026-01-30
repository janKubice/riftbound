using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UI;
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
    [Tooltip("Jak agresivní je křivka obtížnosti. (1.1 = +10% každou minutu skládanně)")]
    [SerializeField] private float _exponentialScalingFactor = 1.1f;

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
    private float _totalCredits = 0;
    private float _gameTime = 0;
    private float _difficultyMultiplier = 1.0f;
    private bool _hasGameStarted = false;
    private float _lastPlayerCheckTime;
    private bool _arePlayersActive = false;
    [SerializeField] private TextMeshProUGUI diffText;

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

        float minutes = _gameTime / 60f;

        // Vzorec: (Faktor ^ Minuty)
        // Příklad při faktoru 1.15:
        // 0 min = 1.0x
        // 5 min = 2.0x
        // 10 min = 4.0x
        // 20 min = 16.0x (Tady už to bude masakr)
        _difficultyMultiplier = Mathf.Pow(_exponentialScalingFactor, minutes);

        // Přísun kreditů se také násobí obtížností, aby Director stíhal spawnovat dražší potvory
        float creditsIncome = _baseCreditsPerSecond * _difficultyMultiplier * dt;

        // Volitelné: Vlny (Sine wave) - aby hra "dýchala" (chvíli klid, chvíli peklo)
        float waveMultiplier = 1.0f + (Mathf.Sin(Time.time * 0.1f) * 0.5f); // Kolísá mezi 0.5x a 1.5x

        // Přísun kreditů
        _accumulatedCredits += creditsIncome * waveMultiplier;
        _totalCredits += creditsIncome * waveMultiplier;

        // 3. Spawnování (Rozložené v čase)
        ProcessSpawnQueue();

        updateUI();
    }

    private void ProcessSpawnQueue()
    {
        if (_enemiesAliveNetVar.Value >= _maxEnemiesAlive)
        {
            _accumulatedCredits = Mathf.Lerp(_accumulatedCredits, 0, Time.deltaTime * 0.3f);
            return;
        }

        int spawnsThisFrame = 0;

        // Loop dokud máme kredity a nejsme na limitu
        while (_accumulatedCredits > 0 &&
               spawnsThisFrame < _maxSpawnsPerFrame &&
               _enemiesAliveNetVar.Value < _maxEnemiesAlive)
        {
            // 1. Vybereme TYP nepřítele (Zombie, Střelec...)
            EnemyDefinition enemyToSpawn = PickAffordableEnemy(_accumulatedCredits);
            if (enemyToSpawn == null) break; // Nemáme ani na základ, končíme frame

            EnemySpawnPoint sp = GetSmartSpawnPoint();
            if (sp == null) break;

            // 2. Vypočítáme TIER (Normal, Elite, Boss) podle nové logiky
            EnemyTier tier = CalculateTier(sp.ZoneDifficulty);
            float tierMult = GetTierMultiplier(tier);
            float finalCost = enemyToSpawn.Cost * tierMult;

            // --- POJISTKA PROTI ZASEKNUTÍ ---
            // Pokud Director vylosoval Elite/Boss, ale nemá na něj kredity,
            // donutíme ho spawnout Normal verzi, aby hra nestála.
            if (_accumulatedCredits < finalCost && tier != EnemyTier.Normal)
            {
                tier = EnemyTier.Normal;
                tierMult = 1.0f;
                finalCost = enemyToSpawn.Cost;
            }

            // 3. Finální spawn, pokud máme alespoň na Normal
            if (_accumulatedCredits >= finalCost)
            {
                SpawnEnemy(enemyToSpawn, tier, sp);
                _accumulatedCredits -= finalCost;
                spawnsThisFrame++;
            }
            else
            {
                // Nemáme ani na Normal verzi -> musíme šetřit.
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

            if (netObj.TryGetComponent(out EnemyBaseAI ai))
            {
                // --- 1. Multipliers ---
                float tierMulti = GetTierMultiplier(tier); // Např. 1.0, 1.5, 3.0
                float timeMulti = _difficultyMultiplier;   // Globální čas
                                                           // Zone: Odmocnina tlumí extrémní nárůst HP, ale stále ho zvedá
                float zoneMulti = Mathf.Sqrt(sp.ZoneDifficulty);

                // Kombinovaný multiplikátor pro "Toughness" (HP/DMG)
                float powerFactor = tierMulti * timeMulti * zoneMulti;

                // --- 2. Stat Calculations ---
                int hp = Mathf.RoundToInt(def.BaseHealth * powerFactor);
                int dmg = Mathf.RoundToInt(def.BaseDamage * powerFactor);

                // XP musí škálovat s obtížností, jinak se hráči nevyplatí bojovat v těžkých zónách
                int xp = Mathf.CeilToInt(def.BaseXPDrop * (powerFactor * 0.8f));

                // Rychlost: Opatrně, příliš rychlí nepřátelé rozbíjí gameplay loop (kiting)
                float speed = def.BaseSpeed * (1 + (timeMulti * 0.03f) + (tierMulti * 0.05f));

                // Attack Rate: Elites útočí trochu rychleji (max +50%)
                float atkRate = def.BaseAttackRate * (1 + Mathf.Clamp((tierMulti - 1) * 0.2f, 0, 0.5f));

                // Knockback Resistance: Silnější nepřátelé se hůře odhazují
                // Vzorec zajistí, že se blíží k 1, ale nikdy ji nepřekročí
                float kbRes = def.BaseKnockbackResistance + (1 - def.BaseKnockbackResistance) * (1 - (1 / tierMulti));

                // --- 3. Advanced Scale Logic ---
                // Základní velikost
                float baseScale = 1.0f;

                // Zvětšení podle Tieru (Elite je větší)
                float tierBonus = (tierMulti - 1.0f) * 0.2f;

                // Zvětšení podle Zóny (Vizuální indikace, že nepřátelé v této zóně jsou "drsnější")
                // Dělíme větším číslem, aby vliv zóny nebyl tak drastický jako Tier
                float zoneBonus = (sp.ZoneDifficulty - 1) * 0.05f;

                // Random Jitter: +/- 10% velikosti pro organický vzhled hordy
                float randomJitter = Random.Range(-0.1f, 0.1f);

                // Výsledná velikost (Clampujeme, aby nebyli menší než 0.8 a větší než např. 3.0)
                float finalScale = Mathf.Clamp(baseScale + tierBonus + zoneBonus + randomJitter, 0.8f, 3.0f);

                // --- 4. Initialize ---
                // Zde předáváme nové parametry do AI (ujistěte se, že InitializeEnemy je upravena)
                ai.InitializeEnemy(
                    tier,
                    hp,
                    dmg,
                    speed,
                    finalScale,
                    atkRate,
                    kbRes,
                    xp,
                    pos
                );
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
        float roll = Random.value; // 0.0 až 1.0

        // --- NOVÁ MATEMATIKA PRO ZÓNY 5-80 ---
        // Místo násobení použijeme přičítání s normalizací.
        // Předpokládáme, že ZoneDifficulty 100 je "konec hry".

        // Base šance (z Inspectoru) + bonus za obtížnost zóny
        // Příklad: Zóna 80 přidá (80 * 0.005) = +0.4 (40%) k šanci.
        float zoneFactor = zoneDifficulty * 0.005f;

        float finalEliteChance = Mathf.Clamp(_eliteChance + zoneFactor, 0f, 0.6f); // Max 60% Elite
        float finalChampionChance = Mathf.Clamp(_championChance + (zoneFactor * 0.2f), 0f, 0.15f); // Max 15% Boss

        // Vyhodnocení (od nejvzácnějšího)
        if (roll < finalChampionChance) return EnemyTier.Boss;
        if (roll < finalChampionChance + finalEliteChance) return EnemyTier.Elite;

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

    private void updateUI()
    {
        diffText.text = "Difficulty: " + _totalCredits.ToString();
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