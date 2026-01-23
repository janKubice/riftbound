using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public class EnemyWaveManager : NetworkBehaviour
{
    public static EnemyWaveManager Instance { get; private set; }

    [System.Serializable]
    public struct WaveDefinition
    {
        public string WaveName;
        public List<EnemySpawnConfig> Enemies;
        public float TimeBetweenSpawns;
    }

    [System.Serializable]
    public struct EnemySpawnConfig
    {
        public GameObject Prefab;
        public int Count;
    }

    [Header("Waves")]
    [SerializeField] private List<WaveDefinition> _waves;
    [SerializeField] private float _timeBetweenWaves = 10f;

    [Header("Spawn Area")]
    [SerializeField] private Transform[] _spawnPoints; // Body po mapě
    [SerializeField] private float _spawnRadius = 20f; // Nebo náhodně kolem hráčů

    private int _currentWaveIndex = 0;
    private bool _isWaveActive = false;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            StartCoroutine(GameLoop());
        }
    }

    private IEnumerator GameLoop()
    {
        Debug.Log("[WaveManager] Čekám 5 sekund na start...");
        yield return new WaitForSeconds(5f);

        if (_waves.Count == 0)
        {
            Debug.LogError("[WaveManager] CHYBA: Seznam vln (_waves) je prázdný!");
            yield break;
        }

        while (_currentWaveIndex < _waves.Count)
        {
            Debug.Log($"[WaveManager] Startuji vlnu {_currentWaveIndex + 1}: {_waves[_currentWaveIndex].WaveName}");
            yield return StartCoroutine(SpawnWave(_waves[_currentWaveIndex]));
            
            Debug.Log($"[WaveManager] Vlna dokončena. Pauza.");
            yield return new WaitForSeconds(_timeBetweenWaves);
            
            _currentWaveIndex++;
        }
    }

    private IEnumerator SpawnWave(WaveDefinition wave)
    {
        foreach (var config in wave.Enemies)
        {
            for (int i = 0; i < config.Count; i++)
            {
                SpawnEnemy(config.Prefab);
                yield return new WaitForSeconds(wave.TimeBetweenSpawns);
            }
        }
        
        // Zde bys mohl čekat, dokud nejsou všichni mrtví (volitelné)
        // yield return new WaitUntil(() => GameObject.FindGameObjectsWithTag("Enemy").Length == 0);
    }

    private void SpawnEnemy(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("[WaveManager] CHYBA: Prefab nepřítele v nastavení vlny je NULL!");
            return;
        }

        Vector3 spawnPos = GetSpawnPositionAroundPlayer();
        Debug.Log($"[WaveManager] Pokus o spawn {prefab.name} na pozici {spawnPos}");
        
        GameObject enemy = Instantiate(prefab, spawnPos, Quaternion.identity);
        
        var netObj = enemy.GetComponent<NetworkObject>();
        if (netObj != null)
        {
            try 
            {
                netObj.Spawn(true);
                Debug.Log("[WaveManager] Úspěšně spawnuto na síti.");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WaveManager] Chyba při NetworkObject.Spawn(): {e.Message}. Máš prefab v NetworkManager listu?");
            }
        }
        else
        {
            Debug.LogError("[WaveManager] CHYBA: Prefab nepřítele nemá NetworkObject!");
        }
    }

    private Vector3 GetSpawnPositionAroundPlayer()
    {
        var clients = NetworkManager.Singleton.ConnectedClientsList;
        if (clients.Count == 0) return transform.position;

        var randomClient = clients[Random.Range(0, clients.Count)];
        if (randomClient.PlayerObject == null) return transform.position;

        // Náhodný bod v kruhu
        Vector2 circle = Random.insideUnitCircle.normalized * _spawnRadius;
        Vector3 targetPos = randomClient.PlayerObject.transform.position + new Vector3(circle.x, 0, circle.y);
        
        // Zvedneme bod vysoko do vzduchu a střelíme paprsek dolů
        targetPos.y += 50f; // ZMĚNA: Bylo 10, dáváme 50 pro jistotu
        
        // Raycast hledá vrstvy Default a Terrain
        if (Physics.Raycast(targetPos, Vector3.down, out RaycastHit hit, 100f, LayerMask.GetMask("Default", "Terrain")))
        {
            return hit.point;
        }

        Debug.LogWarning("[WaveManager] Raycast nenašel zem! Spawnuje se ve vzduchu/na hráči.");
        return randomClient.PlayerObject.transform.position;
    }
}