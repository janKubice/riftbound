using UnityEngine;
using Unity.Netcode;
using System.Collections; // Nutné pro IEnumerator

public class GameLifecycleManager : NetworkBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private GameObject _guyPrefab;
    [SerializeField] private Transform[] _spawnPoints;
    [SerializeField] private float _startDelay = 1f;//5.0f; // Doba čekání v sekundách

    private bool _spawningAllowed = false;

    public override void OnNetworkSpawn()
    {
        LoadingScreenManager.Instance.UpdateMessage("Synchronizing Game State...");
        if (IsServer)
        {
            // Místo okamžitého spawnu spustíme odpočet
            StartCoroutine(DelayedSpawnRoutine());

            // Callbacky pro hráče, kteří se připojí PO uplynutí 5 sekund
            NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer && NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        }
    }

    private IEnumerator DelayedSpawnRoutine()
    {
        Debug.Log($"[Server] Synchronizace klientů ({_startDelay}s)...");

        // Informujeme všechny klienty, že se "připravuje hra"
        UpdateLoadingMessageClientRpc("Preparing Battlefield...");

        yield return new WaitForSeconds(_startDelay);

        _spawningAllowed = true;
        Debug.Log("[Server] Spawn spuštěn.");

        // Hromadný spawn
        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            SpawnPlayerForClient(clientId);
        }

        // Kličová změna: Všem klientům shodíme loading screen AŽ TEĎ
        HideLoadingScreenClientRpc();
    }

    private void OnClientConnected(ulong clientId)
    {
        // Pokud se někdo připojí během těch 5 sekund, ignorujeme ho (bude spawnut hromadně ve smyčce výše).
        // Pokud se připojí až po hře (late join), spawneme ho hned.
        if (_spawningAllowed)
        {
            SpawnPlayerForClient(clientId);
        }
    }

    private void SpawnPlayerForClient(ulong clientId)
    {
        // Bezpečnostní kontrola duplicity
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out NetworkClient client))
        {
            if (client.PlayerObject != null) return;
        }

        int index = (int)clientId % _spawnPoints.Length;
        Vector3 spawnPos = (_spawnPoints != null && _spawnPoints.Length > 0)
            ? _spawnPoints[index].position
            : Vector3.zero;

        Quaternion spawnRot = (_spawnPoints != null && _spawnPoints.Length > 0)
            ? _spawnPoints[index].rotation
            : Quaternion.identity;

        GameObject newPlayer = Instantiate(_guyPrefab, spawnPos, spawnRot);
        newPlayer.name = $"Player_Client_{clientId}";

        newPlayer.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    [ClientRpc]
    private void UpdateLoadingMessageClientRpc(string msg)
    {
        LoadingScreenManager.Instance.UpdateMessage(msg);
    }

    [ClientRpc]
    private void HideLoadingScreenClientRpc()
    {
        LoadingScreenManager.Instance.Hide();
    }
}