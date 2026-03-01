using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

public class EnemyMovementManager : MonoBehaviour
{
    public static EnemyMovementManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float _separationRadius = 1.5f;
    [SerializeField] private float _separationForce = 2.0f;
    [SerializeField] private float _cellSize = 2.0f;

    private List<EnemyBaseAI> _activeEnemies = new List<EnemyBaseAI>(3000);
    
    private NativeArray<float3> _positions;
    private NativeArray<float3> _separationVectors;
    private NativeParallelMultiHashMap<int, int> _hashMap;

    private void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        Instance = this;
        AllocateNativeArrays(3000);
    }

    private void OnDestroy()
    {
        DisposeNativeArrays();
    }

    public void RegisterEnemy(EnemyBaseAI enemy)
    {
        if (!_activeEnemies.Contains(enemy)) _activeEnemies.Add(enemy);
        EnsureCapacity(_activeEnemies.Count);
    }

    public void UnregisterEnemy(EnemyBaseAI enemy)
    {
        _activeEnemies.Remove(enemy);
    }

    private void EnsureCapacity(int count)
    {
        if (count > _positions.Length)
        {
            DisposeNativeArrays();
            AllocateNativeArrays(count * 2);
        }
    }

    private void AllocateNativeArrays(int capacity)
    {
        _positions = new NativeArray<float3>(capacity, Allocator.Persistent);
        _separationVectors = new NativeArray<float3>(capacity, Allocator.Persistent);
        _hashMap = new NativeParallelMultiHashMap<int, int>(capacity, Allocator.Persistent);
    }

    private void DisposeNativeArrays()
    {
        if (_positions.IsCreated) _positions.Dispose();
        if (_separationVectors.IsCreated) _separationVectors.Dispose();
        if (_hashMap.IsCreated) _hashMap.Dispose();
    }

    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        int count = _activeEnemies.Count;
        if (count == 0) return;

        _hashMap.Clear();

        for (int i = 0; i < count; i++)
        {
            var enemy = _activeEnemies[i];
            
            if (enemy.TargetPlayer == null)
            {
                FindTargetFor(enemy);
            }

            if (enemy.TargetPlayer != null && !enemy.IsMovementPaused)
            {
                enemy.BehaviorLogic();
            }

            _positions[i] = enemy.MyTransform.position;
        }

        var hashJob = new HashPositionsJob
        {
            Positions = _positions,
            HashMap = _hashMap.AsParallelWriter(),
            CellSize = _cellSize
        };
        JobHandle hashHandle = hashJob.Schedule(count, 64);

        var separationJob = new EnemySeparationJob
        {
            Positions = _positions,
            HashMap = _hashMap,
            SeparationRadius = _separationRadius,
            SeparationForce = _separationForce,
            CellSize = _cellSize,
            SeparationVectors = _separationVectors
        };
        JobHandle separationHandle = separationJob.Schedule(count, 64, hashHandle);

        separationHandle.Complete();

        for (int i = 0; i < count; i++)
        {
            var enemy = _activeEnemies[i];
            if (enemy.TargetPlayer == null || enemy.IsMovementPaused) continue;

            Vector3 currentPos = enemy.MyTransform.position;
            Vector3 targetPos = enemy.TargetPlayer.position + enemy._targetOffset;
            Vector3 direction = (targetPos - currentPos).normalized;

            Vector3 separation = _separationVectors[i];
            Vector3 finalVelocity = (direction + separation).normalized * 3.5f; 
            
            enemy.ManualMove(finalVelocity);
        }
    }

    private void FindTargetFor(EnemyBaseAI enemy)
    {
        if (NetworkManager.Singleton.ConnectedClients.Count > 0)
        {
            var client = NetworkManager.Singleton.ConnectedClientsList[0];
            if (client.PlayerObject != null)
            {
                enemy.TargetPlayer = client.PlayerObject.transform;
            }
        }
    }
}