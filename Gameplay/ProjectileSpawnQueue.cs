using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public struct SpawnRequest
{
    public GameObject Prefab;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Direction;
    public NetworkObject Attacker;
    public WeaponStats Stats;
    public List<HitEffect> Payload;
    public GameObject IgnoredTarget;
}

public class ProjectileSpawnQueue : MonoBehaviour
{
    public static ProjectileSpawnQueue Instance { get; private set; }
    private Queue<SpawnRequest> _spawnQueue = new Queue<SpawnRequest>();

    [Header("Performance")]
    [Tooltip("Maximální počet Instantiate a Network.Spawn volání za jeden frame")]
    public int MaxSpawnsPerFrame = 5;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void EnqueueSpawn(SpawnRequest request)
    {
        _spawnQueue.Enqueue(request);
    }

    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer || _spawnQueue.Count == 0) return;

        int spawnsThisFrame = 0;
        
        // Zpracuj pouze MaxSpawnsPerFrame požadavků za frame
        while (_spawnQueue.Count > 0 && spawnsThisFrame < MaxSpawnsPerFrame)
        {
            SpawnRequest req = _spawnQueue.Dequeue();
            ProcessSpawn(req);
            spawnsThisFrame++;
        }
    }

    private void ProcessSpawn(SpawnRequest req)
    {
        // Kontrola platnosti - útočník se mohl odpojit během čekání ve frontě
        if (req.Attacker == null) return; 

        GameObject newProjGO = Instantiate(req.Prefab, req.Position, req.Rotation);

        if (newProjGO.TryGetComponent(out NetworkObject netObj))
        {
            netObj.Spawn(true);
        }

        if (newProjGO.TryGetComponent(out SmartProjectile smartProj))
        {
            smartProj.Initialize(req.Attacker, req.Direction, req.Stats, req.Payload);
            
            if (req.IgnoredTarget != null)
            {
                smartProj.AddIgnoredTarget(req.IgnoredTarget);
            }
        }
    }
}