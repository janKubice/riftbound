using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

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
            return;
        }
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        foreach (var config in PooledPrefabsList)
        {
            RegisterPrefab(config.Prefab, config.PrewarmCount);
        }
    }

    public override void OnNetworkDespawn()
    {
        foreach (var prefab in _prefabs)
        {
            NetworkManager.Singleton.PrefabHandler.RemoveHandler(prefab);
        }
        _pooledObjects.Clear();
    }

    private void RegisterPrefab(GameObject prefab, int prewarmCount)
    {
        _prefabs.Add(prefab);
        _pooledObjects[prefab] = new Queue<NetworkObject>(prewarmCount);

        for (int i = 0; i < prewarmCount; i++)
        {
            var go = Instantiate(prefab);
            var no = go.GetComponent<NetworkObject>();
            go.SetActive(false);
            _pooledObjects[prefab].Enqueue(no);
        }

        NetworkManager.Singleton.PrefabHandler.AddHandler(prefab, new PooledPrefabInstanceHandler(prefab, this));
    }

    public NetworkObject GetNetworkObject(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!_pooledObjects.TryGetValue(prefab, out Queue<NetworkObject> queue))
        {
            Debug.LogError($"Prefab {prefab.name} není registrován v Poolu.");
            return null;
        }

        NetworkObject networkObject = null;

        // VYČIŠTĚNÍ POOLU: Projdeme frontu. Pokud je nějaký objekt zničený (null), zahodíme ho.
        while (queue.Count > 0)
        {
            NetworkObject obj = queue.Dequeue();
            if (obj != null && obj.gameObject != null)
            {
                networkObject = obj;
                break; // Našli jsme zdravý, existující objekt
            }
        }

        // Pokud byla fronta prázdná nebo plná smazaných objektů, instancujeme nový
        if (networkObject == null)
        {
            networkObject = Instantiate(prefab).GetComponent<NetworkObject>();
        }

        networkObject.transform.SetPositionAndRotation(position, rotation);
        networkObject.gameObject.SetActive(true);

        return networkObject;
    }

    public void ReturnNetworkObject(NetworkObject networkObject, GameObject prefab)
    {
        networkObject.gameObject.SetActive(false);
        _pooledObjects[prefab].Enqueue(networkObject);
    }

    [System.Serializable]
    private struct PoolConfigObject
    {
        public GameObject Prefab;
        public int PrewarmCount;
    }
}

public class PooledPrefabInstanceHandler : INetworkPrefabInstanceHandler
{
    private readonly GameObject _prefab;
    private readonly NetworkObjectPool _pool;

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