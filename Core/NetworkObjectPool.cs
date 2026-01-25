using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Assertions;

public class NetworkObjectPool : NetworkBehaviour
{
    public static NetworkObjectPool Instance { get; private set; }

    [SerializeField]
    private List<PoolConfigObject> PooledPrefabsList;

    private HashSet<GameObject> _prefabs = new HashSet<GameObject>();
    private Dictionary<GameObject, Queue<NetworkObject>> _pooledObjects = new Dictionary<GameObject, Queue<NetworkObject>>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
        }
        else
        {
            Instance = this;
        }
    }

    public override void OnNetworkSpawn()
    {
        // Registrace všech prefabů do pooling systému Netcode
        foreach (var config in PooledPrefabsList)
        {
            RegisterPrefab(config.Prefab, config.PrewarmCount);
        }
    }

    public override void OnNetworkDespawn()
    {
        // Úklid při vypnutí
        foreach (var prefab in _prefabs)
        {
            NetworkManager.Singleton.PrefabHandler.RemoveHandler(prefab);
        }
        _pooledObjects.Clear();
    }

    private void RegisterPrefab(GameObject prefab, int prewarmCount)
    {
        _prefabs.Add(prefab);
        _pooledObjects[prefab] = new Queue<NetworkObject>();

        // Vytvoření počáteční zásoby (Prewarm)
        for (int i = 0; i < prewarmCount; i++)
        {
            var go = Instantiate(prefab);
            var no = go.GetComponent<NetworkObject>();
            go.SetActive(false);
            _pooledObjects[prefab].Enqueue(no);
        }

        // Řekneme Netcode, ať pro tento prefab používá náš pool
        NetworkManager.Singleton.PrefabHandler.AddHandler(prefab, new PooledPrefabInstanceHandler(prefab, this));
    }

    public NetworkObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!_pooledObjects.ContainsKey(prefab))
        {
            Debug.LogError($"Prefab {prefab.name} není registrován v Poolu! Přidej ho do listu v Inspektoru.");
            return null;
        }

        Queue<NetworkObject> queue = _pooledObjects[prefab];
        NetworkObject networkObject;

        // Pokud je fronta prázdná, vyrobíme nový (jinak bereme ze zásoby)
        if (queue.Count > 0)
        {
            networkObject = queue.Dequeue();
        }
        else
        {
            networkObject = Instantiate(prefab).GetComponent<NetworkObject>();
        }

        // Aktivace a nastavení pozice
        networkObject.transform.position = position;
        networkObject.transform.rotation = rotation;
        networkObject.gameObject.SetActive(true);

        return networkObject;
    }

    public void ReturnNetworkObject(NetworkObject networkObject, GameObject prefab)
    {
        networkObject.gameObject.SetActive(false);
        _pooledObjects[prefab].Enqueue(networkObject);
    }

    // Helper třída pro Inspector
    [System.Serializable]
    struct PoolConfigObject
    {
        public GameObject Prefab;
        public int PrewarmCount;
    }
}

// Handler, který propojuje Unity Netcode s naším poolem
public class PooledPrefabInstanceHandler : INetworkPrefabInstanceHandler
{
    private GameObject _prefab;
    private NetworkObjectPool _pool;

    public PooledPrefabInstanceHandler(GameObject prefab, NetworkObjectPool pool)
    {
        _prefab = prefab;
        _pool = pool;
    }

    public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
    {
        return _pool.GetNetworkObject(_prefab, position, rotation);
    }

    public void Destroy(NetworkObject networkObject)
    {
        _pool.ReturnNetworkObject(networkObject, _prefab);
    }
}