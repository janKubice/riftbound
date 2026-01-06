using System.Collections;
using Unity.Netcode;
using UnityEngine;

// GameManager řídí stav hry, spawnování hráčů a nepřátel
public class GameManager : NetworkBehaviour
{
    [Header("Spawnování Hráče")]
    [SerializeField] private GameObject _playerPrefab;
    [SerializeField] private Transform _playerSpawnPoint;

    [Header("Spawnování Nepřátel")]
    [SerializeField] private GameObject _enemyPrefab;
    [SerializeField] private float _spawnInterval = 2.0f;
    [SerializeField] private float _spawnRadius = 15f;
    [SerializeField] private Transform _spawnCenter; // Centrum spawnování

    [Header("UI")]
    [SerializeField] private GameObject _pauseMenuPanel;

    private void Start()
    {
        // Spawnování hráče už neřešíme, dělá to NGO

        if (_spawnCenter == null)
            _spawnCenter = this.transform;

        if (_pauseMenuPanel)
            _pauseMenuPanel.SetActive(false);
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Spawnování nepřátel spouští POUZE server
        // OnNetworkSpawn() se volá až poté, co je síť aktivní a IsServer je platné.
        if (IsServer)
        {
            StartCoroutine(SpawnEnemyRoutine());
        }
    }

    private IEnumerator SpawnEnemyRoutine()
    {
        while (true)
        {
            // Počkáme daný interval
            yield return new WaitForSeconds(_spawnInterval);

            if (_enemyPrefab == null) continue;

            // Najde náhodnou pozici v kruhu
            Vector2 randomCircle = Random.insideUnitCircle.normalized * _spawnRadius;
            Vector3 spawnPos = _spawnCenter.position + new Vector3(randomCircle.x, 0.5f, randomCircle.y);

            GameObject enemyGO = Instantiate(_enemyPrefab, spawnPos, Quaternion.identity);

            // Spawnování na síti (vytvoří objekt i u klientů)
            enemyGO.GetComponent<NetworkObject>().Spawn(true);
        }
    }


}