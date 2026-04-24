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
    [SerializeField] private string _layerMaskName = "Enviroment";

    private List<EnemyBaseAI> _activeEnemies = new List<EnemyBaseAI>(3000);

    private NativeArray<float3> _positions;
    private NativeArray<float3> _separationVectors;
    private NativeParallelMultiHashMap<int, int> _hashMap;
    private NativeArray<float3> _baseDirections;
    private NativeArray<float3> _combinedDirections;
    private NativeArray<RaycastCommand> _raycastCommands;
    private NativeArray<RaycastHit> _raycastHits;
    private float _flowFieldUpdateTimer = 0f;
    private const float FLOW_FIELD_INTERVAL = 0.5f;

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

    private void Update()
    {
        if (!NetworkManager.Singleton.IsServer) return;

        int count = _activeEnemies.Count;
        if (count == 0) return;

        var firstEnemy = _activeEnemies[0];
        if (firstEnemy.TargetPlayer == null)
        {
            FindTargetFor(firstEnemy);
        }

        if (firstEnemy.TargetPlayer != null)
        {
            _flowFieldUpdateTimer -= Time.deltaTime;
            if (_flowFieldUpdateTimer <= 0f)
            {
                FlowFieldManager.Instance.CalculateFlowField(firstEnemy.TargetPlayer.position);
                _flowFieldUpdateTimer = FLOW_FIELD_INTERVAL;
            }
        }

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

        // A. Výpočet základního směru z Flow Fieldu
        var flowJob = new MoveWithFlowFieldJob
        {
            FlowField = FlowFieldManager.Instance.Grid,
            Positions = _positions,
            OutputDirections = _baseDirections,
            GridOrigin = FlowFieldManager.Instance.GridOrigin,
            CellSize = FlowFieldManager.Instance.CellSize,
            GridWidth = FlowFieldManager.Instance.GridWidth,
            GridHeight = FlowFieldManager.Instance.GridHeight,
            TargetPos = firstEnemy.TargetPlayer.position
        };
        JobHandle flowHandle = flowJob.Schedule(count, 64);

        // B. Hashování pozic pro separaci
        var hashJob = new HashPositionsJob
        {
            Positions = _positions,
            HashMap = _hashMap.AsParallelWriter(),
            CellSize = _cellSize
        };
        JobHandle hashHandle = hashJob.Schedule(count, 64);

        // C. Výpočet separace
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

        // D. Spojení závislostí a synchronizace
        JobHandle mergedHandle = JobHandle.CombineDependencies(flowHandle, separationHandle);
        mergedHandle.Complete();

        // E. Aplikace finálního pohybu
        for (int i = 0; i < count; i++)
        {
            var enemy = _activeEnemies[i];
            if (enemy.TargetPlayer == null || enemy.IsMovementPaused) continue;

            Vector3 combinedDir = _baseDirections[i] + _separationVectors[i];
            combinedDir.y = 0;

            if (combinedDir.sqrMagnitude > 0.0001f)
            {
                combinedDir.Normalize();
            }

            Vector3 finalVelocity = combinedDir * enemy.CurrentSpeed;
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


    private void AllocateNativeArrays(int capacity)
    {
        _positions = new NativeArray<float3>(capacity, Allocator.Persistent);
        _separationVectors = new NativeArray<float3>(capacity, Allocator.Persistent);
        _hashMap = new NativeParallelMultiHashMap<int, int>(capacity, Allocator.Persistent);

        _baseDirections = new NativeArray<float3>(capacity, Allocator.Persistent);
        _combinedDirections = new NativeArray<float3>(capacity, Allocator.Persistent);
        _raycastCommands = new NativeArray<RaycastCommand>(capacity, Allocator.Persistent);
        _raycastHits = new NativeArray<RaycastHit>(capacity, Allocator.Persistent);
    }

    private void DisposeNativeArrays()
    {
        if (_positions.IsCreated) _positions.Dispose();
        if (_separationVectors.IsCreated) _separationVectors.Dispose();
        if (_hashMap.IsCreated) _hashMap.Dispose();

        if (_baseDirections.IsCreated) _baseDirections.Dispose();
        if (_combinedDirections.IsCreated) _combinedDirections.Dispose();
        if (_raycastCommands.IsCreated) _raycastCommands.Dispose();
        if (_raycastHits.IsCreated) _raycastHits.Dispose();
    }

    // 3. Nový Job pro přípravu RaycastCommand batchingu (umístit mimo třídu EnemyMovementManager)
    public struct SetupRaycastsJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> BaseDirections;
        [ReadOnly] public NativeArray<float3> SeparationVectors;
        [WriteOnly] public NativeArray<float3> CombinedDirections;
        [WriteOnly] public NativeArray<RaycastCommand> Commands;
        public int LayerMask;

        public void Execute(int index)
        {
            // Sloučení směru z Flow Field a separace
            float3 combined = BaseDirections[index] + SeparationVectors[index];
            combined.y = 0;
            CombinedDirections[index] = combined;

            // Parametry pro paprsek detekce terénu
            float3 origin = Positions[index] + new float3(0, 3f, 0);
            float3 direction = new float3(0, -1f, 0);
            QueryParameters queryParams = new QueryParameters(LayerMask, false, QueryTriggerInteraction.Ignore, false);

            Commands[index] = new RaycastCommand(origin, direction, queryParams, 5f);
        }
    }
}


public struct MoveWithFlowFieldJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<FlowCell> FlowField;
    [ReadOnly] public NativeArray<float3> Positions;
    public NativeArray<float3> OutputDirections;
    public float3 GridOrigin;
    public float CellSize;
    public int GridWidth;
    public int GridHeight;
    public float3 TargetPos;

    public void Execute(int index)
    {
        float3 pos = Positions[index];

        int gridX = (int)math.floor((pos.x - GridOrigin.x) / CellSize);
        int gridY = (int)math.floor((pos.z - GridOrigin.z) / CellSize);

        gridX = math.clamp(gridX, 0, GridWidth - 1);
        gridY = math.clamp(gridY, 0, GridHeight - 1);

        int flatIndex = gridX + (gridY * GridWidth);
        float2 flowDirection = FlowField[flatIndex].Direction;

        // FAILSAFE: Pokud mřížka neobsahuje směr, jdi přímo k hráči
        if (flowDirection.x == 0f && flowDirection.y == 0f)
        {
            float2 directDir = math.normalize(new float2(TargetPos.x - pos.x, TargetPos.z - pos.z));
            OutputDirections[index] = new float3(directDir.x, 0, directDir.y);
        }
        else
        {
            OutputDirections[index] = new float3(flowDirection.x, 0, flowDirection.y);
        }
    }
}