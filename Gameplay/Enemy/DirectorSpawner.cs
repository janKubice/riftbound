using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class DirectorSpawner : NetworkBehaviour
{
    public static DirectorSpawner Instance { get; private set; }

    [Header("Databáze Nepřátel")]
    [SerializeField] private List<EnemyDefinition> _allEnemies;

    [Header("Game Pace")]
    [SerializeField] private float _creditsPerSecond = 1.0f;     // Základní přísun kreditů
    [SerializeField] private float _difficultyScaling = 0.1f;    // Jak rychle hra těžkne (za sekundu)
    [SerializeField] private int _maxEnemiesAlive = 50;

    [Header("Tier Chances (Base)")]
    [Range(0,1)] [SerializeField] private float _eliteChance = 0.1f;
    [Range(0,1)] [SerializeField] private float _championChance = 0.02f;

    private float _currentCredits = 0;
    private float _gameTime = 0;
    private float _difficultyMultiplier = 1.0f;
    
    private List<EnemySpawnPoint> _spawnPoints = new List<EnemySpawnPoint>();
    private int _enemiesAlive = 0;

    private void Awake() 
    { 
        Instance = this; 
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Najde všechny spawnpointy ve scéně
            _spawnPoints = FindObjectsByType<EnemySpawnPoint>(FindObjectsSortMode.None).ToList();
            StartCoroutine(DirectorLoop());
        }
    }

    public void EnemyDied()
    {
        if(IsServer) _enemiesAlive--;
    }

    private IEnumerator DirectorLoop()
    {
        yield return new WaitForSeconds(2.0f); // Start delay

        while (true)
        {
            // 1. Zvyšování obtížnosti a kreditů
            _gameTime += 1.0f;
            _difficultyMultiplier = 1.0f + (_gameTime * _difficultyScaling / 60f); // Každou minutu těžší
            
            // Přičteme kredity (násobeno obtížností)
            _currentCredits += _creditsPerSecond * _difficultyMultiplier;

            // 2. Kontrola limitu nepřátel
            if (_enemiesAlive < _maxEnemiesAlive)
            {
                TrySpawnEnemies();
            }

            // Loop běží každou sekundu
            yield return new WaitForSeconds(1.0f);
        }
    }

    private void TrySpawnEnemies()
    {
        // Dokud máme kredity, nakupujeme
        int attempts = 0;
        while (_currentCredits > 0 && _enemiesAlive < _maxEnemiesAlive && attempts < 10)
        {
            attempts++;

            // A) Vyber SpawnPoint (blízko nějakého hráče)
            EnemySpawnPoint spawnPoint = GetSmartSpawnPoint();
            if (spawnPoint == null) break;

            // B) Vyber Nepřítele podle Rarity
            EnemyDefinition enemyDef = PickRandomEnemy();
            if (enemyDef == null) break;

            // Máme na něj?
            if (_currentCredits >= enemyDef.Cost)
            {
                // C) Urči Tier (Kvalitu)
                EnemyTier tier = CalculateTier(spawnPoint.ZoneDifficulty);
                float tierCostMultiplier = GetTierMultiplier(tier);
                
                // Finální cena (Mocný mob stojí víc)
                float finalCost = enemyDef.Cost * tierCostMultiplier;

                if (_currentCredits >= finalCost)
                {
                    // SPAWN!
                    SpawnEnemy(enemyDef, tier, spawnPoint);
                    _currentCredits -= finalCost;
                }
            }
        }
    }

    private void SpawnEnemy(EnemyDefinition def, EnemyTier tier, EnemySpawnPoint sp)
    {
        // Náhodná pozice v okruhu spawnpointu
        Vector2 circle = Random.insideUnitCircle * sp.SpawnRadius;
        Vector3 pos = sp.transform.position + new Vector3(circle.x, 0, circle.y);
        
        // Raycast na zem
        if (Physics.Raycast(pos + Vector3.up * 10, Vector3.down, out RaycastHit hit, 20f, LayerMask.GetMask("Default", "Terrain")))
        {
            pos = hit.point;
        }

        GameObject go = Instantiate(def.Prefab, pos, Quaternion.identity);
        go.GetComponent<NetworkObject>().Spawn(true);

        // Aplikace Statů
        if (go.TryGetComponent(out EnemyBaseAI ai))
        {
            float multi = GetTierMultiplier(tier);
            
            // Kalkulace statů: Base * Tier Multi * Global Difficulty
            int hp = Mathf.RoundToInt(def.BaseHealth * multi * _difficultyMultiplier);
            int dmg = Mathf.RoundToInt(def.BaseDamage * multi * _difficultyMultiplier);
            float speed = def.BaseSpeed * (1 + (multi * 0.1f)); // Speed roste pomaleji
            float scale = 1.0f + (multi * 0.2f); // Boss je větší

            ai.InitializeEnemy(tier, hp, dmg, speed, scale);
        }

        _enemiesAlive++;
    }

    // --- LOGIKA VÝBĚRU ---

    private EnemyTier CalculateTier(float zoneDifficulty)
    {
        // Čím vyšší zoneDifficulty, tím větší šance na Elite
        float roll = Random.value;
        float difficultyMod = zoneDifficulty * 0.1f; // Třeba

        if (roll < _championChance * difficultyMod) return EnemyTier.Boss;
        if (roll < _eliteChance * difficultyMod) return EnemyTier.Elite;
        
        return EnemyTier.Normal;
    }

    private float GetTierMultiplier(EnemyTier tier)
    {
        switch (tier)
        {
            case EnemyTier.Elite: return 2.0f;
            case EnemyTier.Champion: return 5.0f;
            case EnemyTier.Boss: return 20.0f;
            default: return 1.0f;
        }
    }

    private EnemyDefinition PickRandomEnemy()
    {
        // Vážený výběr (Weighted Random)
        int totalWeight = 0;
        foreach (var e in _allEnemies) totalWeight += (int)e.Rarity;

        int roll = Random.Range(0, totalWeight);
        int current = 0;

        foreach (var e in _allEnemies)
        {
            current += (int)e.Rarity;
            if (roll < current) return e;
        }
        return _allEnemies[0];
    }

    private EnemySpawnPoint GetSmartSpawnPoint()
    {
        // Najdi náhodného hráče
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count == 0) return null;
        var player = clients[Random.Range(0, clients.Count)].PlayerObject;
        if (player == null) return null;

        // Najdi spawnpointy v okolí (ne moc blízko, ne moc daleko)
        // Optimalizace: V reálu použít prostorové dělení, zde Linq
        var validPoints = _spawnPoints
            .Where(sp => {
                float d = Vector3.Distance(sp.transform.position, player.transform.position);
                return d > 10f && d < 60f; // Spawnuj jen 10-60m od hráče
            })
            .ToList();

        if (validPoints.Count == 0) return null;
        return validPoints[Random.Range(0, validPoints.Count)];
    }
}