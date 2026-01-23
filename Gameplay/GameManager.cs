using System.Collections;
using Unity.Netcode;
using UnityEngine;

// GameManager řídí stav hry, spawnování hráčů a nepřátel
public class GameManager : NetworkBehaviour
{
    [Header("Spawnování Hráče")]
    [SerializeField] private GameObject _playerPrefab;
    [SerializeField] private Transform _playerSpawnPoint;


    [Header("UI")]
    [SerializeField] private GameObject _pauseMenuPanel;

    private void Start()
    {
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



}