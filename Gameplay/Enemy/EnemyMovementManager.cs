using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Netcode;
using UnityEngine;

public class EnemyMovementManager : MonoBehaviour
{
    public static EnemyMovementManager Instance { get; private set; }

    [Header("Separation - Manual Values")]
    [SerializeField] private float _separationRadius = 1.15f;
    [SerializeField] private float _separationForce = 0.55f;
    [SerializeField] private float _maxSeparationInfluence = 0.55f;
    [SerializeField] private float _spatialHashCellSize = 1.25f;
    [SerializeField] private int _maxSeparationNeighbours = 12;

    [Header("Flow Field - Manual Values")]
    [SerializeField] private float _flowFieldUpdateInterval = 0.25f;

    [Header("Fallback Movement")]
    [Tooltip("Použije se pouze když flow field nemá validní směr. Nejde o attack range nepřítele.")]
    [SerializeField] private float _fallbackStopDistance = 0.7f;

    [Header("Line Breaking")]
    [Tooltip("Malý stabilní boční bias pro každého enemy. Rozbíjí dlouhé jednosměrné hady bez náhodného jitteru.")]
    [SerializeField] private float _personalSideBias = 0.10f;

    [Tooltip("Vzdálenost, ve které enemy kontroluje, zda má přímo před sebou jiného enemy ve stejné linii.")]
    [SerializeField] private float _lineBreakRadius = 2.8f;

    [Tooltip("Síla bočního úhybu, když enemy vidí jiného enemy přímo před sebou.")]
    [SerializeField] private float _lineBreakForce = 0.45f;

    [Tooltip("Jak moc musí být soused před enemy, aby se spustil line break. Vyšší = méně často.")]
    [SerializeField, Range(0f, 1f)] private float _lineBreakForwardDot = 0.25f;

    [Tooltip("Jak blízko ose pohybu musí soused být. Nižší = jen velmi přesné linie, vyšší = širší záběr.")]
    [SerializeField, Range(0.05f, 1f)] private float _lineBreakSideDot = 0.45f;

    [Tooltip("Blízko targetu se line breaking vypíná, aby enemy neobíhali hráče místo útoku.")]
    [SerializeField] private float _lineBreakDisableDistance = 3.0f;

    [Header("Local Obstacle Avoidance")]
    [SerializeField] private bool _useLocalObstacleAvoidance = true;

    [Tooltip("Obvykle stejné vrstvy jako FlowField ObstacleMask. Pokud je None, zkusí se použít FlowFieldManager.ObstacleMask.")]
    [SerializeField] private LayerMask _localObstacleMask;

    [SerializeField] private float _avoidanceSphereRadius = 0.45f;
    [SerializeField] private float _avoidanceDistance = 1.8f;
    [SerializeField] private float _avoidanceHeight = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _avoidanceBlend = 0.75f;

    [Header("Adaptive Performance")]
    [SerializeField] private bool _adaptivePerformance = true;
    [SerializeField] private float _targetFps = 60f;
    [SerializeField] private float _criticalFps = 42f;
    [SerializeField] private float _fpsSmoothing = 0.08f;
    [SerializeField] private float _adaptiveCheckInterval = 0.5f;

    [Tooltip("Od tohoto počtu pohyblivých enemies začne manager preventivně snižovat kvalitu separation.")]
    [SerializeField] private int _enemyCountSoftLimit = 120;

    [Tooltip("Při tomto počtu pohyblivých enemies se použije nejlevnější nastavení bez ohledu na FPS.")]
    [SerializeField] private int _enemyCountHardLimit = 350;

    [Header("Adaptive Ranges")]
    [SerializeField] private float _minAdaptiveSeparationRadius = 0.85f;
    [SerializeField] private float _maxAdaptiveSeparationRadius = 1.15f;
    [SerializeField] private float _minAdaptiveSeparationForce = 0.30f;
    [SerializeField] private float _maxAdaptiveSeparationForce = 0.55f;
    [SerializeField] private float _minAdaptiveSeparationInfluence = 0.30f;
    [SerializeField] private float _maxAdaptiveSeparationInfluence = 0.55f;
    [SerializeField] private float _minAdaptiveFlowFieldUpdateInterval = 0.20f;
    [SerializeField] private float _maxAdaptiveFlowFieldUpdateInterval = 0.55f;
    [SerializeField] private int _minAdaptiveSeparationNeighbours = 4;
    [SerializeField] private int _maxAdaptiveSeparationNeighbours = 12;

    [Header("Adaptive Runtime Debug")]
    [SerializeField] private float _smoothedFps;
    [SerializeField, Range(0f, 1f)] private float _runtimeQuality01 = 1f;
    [SerializeField] private float _runtimeSeparationRadius;
    [SerializeField] private float _runtimeSeparationForce;
    [SerializeField] private float _runtimeMaxSeparationInfluence;
    [SerializeField] private float _runtimeSpatialHashCellSize;
    [SerializeField] private float _runtimeFlowFieldUpdateInterval;
    [SerializeField] private int _runtimeMaxSeparationNeighbours;

    private readonly List<EnemyBaseAI> _registeredEnemies = new List<EnemyBaseAI>(3000);
    private readonly List<EnemyBaseAI> _movingEnemies = new List<EnemyBaseAI>(3000);

    private NativeArray<float3> _positions;
    private NativeArray<float3> _targetPositions;
    private NativeArray<float3> _baseDirections;
    private NativeArray<float3> _separationDirections;
    private NativeArray<float3> _finalDirections;
    private NativeArray<float> _preferredSides;
    private NativeParallelMultiHashMap<int, int> _spatialHash;

    private NativeArray<SpherecastCommand> _avoidanceCommands;
    private NativeArray<RaycastHit> _avoidanceHits;
    private JobHandle _crowdMovementDependency;

    private float _flowFieldUpdateTimer;
    private float _adaptiveTimer;

    private void OnValidate()
    {
        _separationRadius = Mathf.Max(0.05f, _separationRadius);
        _separationForce = Mathf.Max(0f, _separationForce);
        _maxSeparationInfluence = Mathf.Max(0f, _maxSeparationInfluence);
        _spatialHashCellSize = Mathf.Max(0.05f, _spatialHashCellSize);
        _maxSeparationNeighbours = Mathf.Max(0, _maxSeparationNeighbours);

        _flowFieldUpdateInterval = Mathf.Max(0.05f, _flowFieldUpdateInterval);
        _fallbackStopDistance = Mathf.Max(0f, _fallbackStopDistance);

        _personalSideBias = Mathf.Max(0f, _personalSideBias);
        _lineBreakRadius = Mathf.Max(0.05f, _lineBreakRadius);
        _lineBreakForce = Mathf.Max(0f, _lineBreakForce);
        _lineBreakDisableDistance = Mathf.Max(0f, _lineBreakDisableDistance);

        _avoidanceSphereRadius = Mathf.Max(0.05f, _avoidanceSphereRadius);
        _avoidanceDistance = Mathf.Max(0.05f, _avoidanceDistance);
        _avoidanceHeight = Mathf.Max(0.05f, _avoidanceHeight);

        _targetFps = Mathf.Max(15f, _targetFps);
        _criticalFps = Mathf.Clamp(_criticalFps, 10f, _targetFps - 1f);
        _fpsSmoothing = Mathf.Clamp01(_fpsSmoothing);
        _adaptiveCheckInterval = Mathf.Max(0.1f, _adaptiveCheckInterval);

        _enemyCountSoftLimit = Mathf.Max(1, _enemyCountSoftLimit);
        _enemyCountHardLimit = Mathf.Max(_enemyCountSoftLimit + 1, _enemyCountHardLimit);

        _minAdaptiveSeparationRadius = Mathf.Max(0.05f, _minAdaptiveSeparationRadius);
        _maxAdaptiveSeparationRadius = Mathf.Max(_minAdaptiveSeparationRadius, _maxAdaptiveSeparationRadius);

        _minAdaptiveSeparationForce = Mathf.Max(0f, _minAdaptiveSeparationForce);
        _maxAdaptiveSeparationForce = Mathf.Max(_minAdaptiveSeparationForce, _maxAdaptiveSeparationForce);

        _minAdaptiveSeparationInfluence = Mathf.Max(0f, _minAdaptiveSeparationInfluence);
        _maxAdaptiveSeparationInfluence = Mathf.Max(_minAdaptiveSeparationInfluence, _maxAdaptiveSeparationInfluence);

        _minAdaptiveFlowFieldUpdateInterval = Mathf.Max(0.05f, _minAdaptiveFlowFieldUpdateInterval);
        _maxAdaptiveFlowFieldUpdateInterval = Mathf.Max(_minAdaptiveFlowFieldUpdateInterval, _maxAdaptiveFlowFieldUpdateInterval);

        _minAdaptiveSeparationNeighbours = Mathf.Max(0, _minAdaptiveSeparationNeighbours);
        _maxAdaptiveSeparationNeighbours = Mathf.Max(_minAdaptiveSeparationNeighbours, _maxAdaptiveSeparationNeighbours);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        ApplyManualRuntimeTuning();
        AllocateNativeData(3000);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        DisposeNativeData();
    }

    public void RegisterEnemy(EnemyBaseAI enemy)
    {
        if (enemy == null)
            return;

        if (!_registeredEnemies.Contains(enemy))
            _registeredEnemies.Add(enemy);

        EnsureCapacity(_registeredEnemies.Count);
    }

    public void UnregisterEnemy(EnemyBaseAI enemy)
    {
        if (enemy == null)
            return;

        _registeredEnemies.Remove(enemy);
        _movingEnemies.Remove(enemy);
    }

    private void Update()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            return;

        UpdateSmoothedFps();

        if (_registeredEnemies.Count == 0)
            return;

        RemoveDestroyedEnemies();
        BuildMovingEnemyList(out Transform flowTarget);

        int count = _movingEnemies.Count;
        if (count == 0)
            return;

        UpdateAdaptiveTuningIfNeeded(count);

        EnsureCapacity(count);
        RefreshFlowFieldIfNeeded(flowTarget);
        CopyPositions(count);

        _spatialHash.Clear();

        JobHandle hashHandle = new EnemySpatialHashBuildJob
        {
            Positions = _positions,
            CellSize = _runtimeSpatialHashCellSize,
            HashMap = _spatialHash.AsParallelWriter()
        }.Schedule(count, 64);

        JobHandle flowHandle = ScheduleFlowDirectionJob(count, flowTarget);

        JobHandle crowdDependency = JobHandle.CombineDependencies(hashHandle, flowHandle);

        JobHandle crowdHandle = new EnemyCrowdResolveJob
        {
            Positions = _positions,
            BaseDirections = _baseDirections,
            PreferredSides = _preferredSides,
            HashMap = _spatialHash,
            CellSize = _runtimeSpatialHashCellSize,
            SeparationRadius = _runtimeSeparationRadius,
            SeparationForce = _runtimeSeparationForce,
            LineBreakRadius = _lineBreakRadius,
            LineBreakForce = _lineBreakForce,
            LineBreakForwardDot = _lineBreakForwardDot,
            LineBreakSideDot = _lineBreakSideDot,
            MaxNeighbours = _runtimeMaxSeparationNeighbours,
            Output = _separationDirections
        }.Schedule(count, 64, crowdDependency);

        JobHandle finalDirectionHandle = new EnemyFinalDirectionJob
        {
            Positions = _positions,
            TargetPositions = _targetPositions,
            BaseDirections = _baseDirections,
            SeparationDirections = _separationDirections,
            PreferredSides = _preferredSides,
            Output = _finalDirections,
            MaxSeparationInfluence = _runtimeMaxSeparationInfluence,
            FallbackStopDistance = _fallbackStopDistance,
            LineBreakDisableDistance = _lineBreakDisableDistance,
            PersonalSideBias = _personalSideBias
        }.Schedule(count, 64, crowdHandle);

        _crowdMovementDependency = finalDirectionHandle;
    }

    private void LateUpdate()
    {
        if (_movingEnemies.Count == 0) return;

        // Teprve zde (na úplném konci snímku) vynutíme dokončení,
        // pokud se ještě job nedokončil.
        _crowdMovementDependency.Complete();

        ApplyMovement(_movingEnemies.Count);
    }

    private void UpdateSmoothedFps()
    {
        float delta = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
        float instantFps = 1f / delta;

        if (_smoothedFps <= 0f)
            _smoothedFps = instantFps;
        else
            _smoothedFps = Mathf.Lerp(_smoothedFps, instantFps, _fpsSmoothing);
    }

    private void UpdateAdaptiveTuningIfNeeded(int movingEnemyCount)
    {
        _adaptiveTimer -= Time.unscaledDeltaTime;
        if (_adaptiveTimer > 0f)
            return;

        _adaptiveTimer = _adaptiveCheckInterval;

        if (!_adaptivePerformance)
        {
            ApplyManualRuntimeTuning();
            return;
        }

        float fpsQuality = Mathf.InverseLerp(_criticalFps, _targetFps, _smoothedFps);
        float countQuality = 1f - Mathf.InverseLerp(_enemyCountSoftLimit, _enemyCountHardLimit, movingEnemyCount);
        float targetQuality = Mathf.Clamp01(Mathf.Min(fpsQuality, countQuality));

        // Omezuje skákání mezi kvalitami. Nízké FPS sníží kvalitu rychleji, návrat nahoru bude pozvolný.
        float maxStep = targetQuality < _runtimeQuality01 ? 0.35f : 0.15f;
        _runtimeQuality01 = Mathf.MoveTowards(_runtimeQuality01, targetQuality, maxStep);

        ApplyAdaptiveRuntimeTuning(_runtimeQuality01);
    }

    private void ApplyManualRuntimeTuning()
    {
        _runtimeQuality01 = 1f;
        _runtimeSeparationRadius = _separationRadius;
        _runtimeSeparationForce = _separationForce;
        _runtimeMaxSeparationInfluence = _maxSeparationInfluence;
        _runtimeSpatialHashCellSize = Mathf.Max(_spatialHashCellSize, _runtimeSeparationRadius);
        _runtimeFlowFieldUpdateInterval = _flowFieldUpdateInterval;
        _runtimeMaxSeparationNeighbours = _maxSeparationNeighbours;
    }

    private void ApplyAdaptiveRuntimeTuning(float quality01)
    {
        // quality01 = 1 => nejlepší kvalita, dražší výpočet.
        // quality01 = 0 => nejlevnější výpočet, horší davové chování.
        _runtimeSeparationRadius = Mathf.Lerp(_minAdaptiveSeparationRadius, _maxAdaptiveSeparationRadius, quality01);
        _runtimeSeparationForce = Mathf.Lerp(_minAdaptiveSeparationForce, _maxAdaptiveSeparationForce, quality01);
        _runtimeMaxSeparationInfluence = Mathf.Lerp(_minAdaptiveSeparationInfluence, _maxAdaptiveSeparationInfluence, quality01);

        // Pro 3x3 lookup musí být cell size minimálně separation radius, jinak může job minout sousedy.
        _runtimeSpatialHashCellSize = Mathf.Max(0.05f, _runtimeSeparationRadius);

        // U flow fieldu je to obráceně: větší interval = méně častý přepočet = lepší výkon.
        _runtimeFlowFieldUpdateInterval = Mathf.Lerp(
            _maxAdaptiveFlowFieldUpdateInterval,
            _minAdaptiveFlowFieldUpdateInterval,
            quality01
        );

        _runtimeMaxSeparationNeighbours = Mathf.RoundToInt(
            Mathf.Lerp(_minAdaptiveSeparationNeighbours, _maxAdaptiveSeparationNeighbours, quality01)
        );
    }

    private void BuildMovingEnemyList(out Transform flowTarget)
    {
        flowTarget = null;
        _movingEnemies.Clear();

        for (int i = 0; i < _registeredEnemies.Count; i++)
        {
            EnemyBaseAI enemy = _registeredEnemies[i];

            if (!IsEnemyUsable(enemy))
                continue;

            if (enemy.TargetPlayer == null)
                enemy.TargetPlayer = FindClosestPlayer(enemy.MyTransform.position);

            if (enemy.TargetPlayer == null)
                continue;

            enemy.BehaviorLogic();

            if (flowTarget == null)
                flowTarget = enemy.TargetPlayer;

            if (!enemy.IsMovementPaused && enemy.UsesFlowFieldMovement)
                _movingEnemies.Add(enemy);
        }
    }

    private bool IsEnemyUsable(EnemyBaseAI enemy)
    {
        return enemy != null
               && enemy.isActiveAndEnabled
               && enemy.gameObject != null
               && enemy.gameObject.activeInHierarchy
               && enemy.IsSpawned
               && !enemy.IsSpawning
               && enemy.IsAlive
               && enemy.MyTransform != null;
    }

    private void RemoveDestroyedEnemies()
    {
        for (int i = _registeredEnemies.Count - 1; i >= 0; i--)
        {
            EnemyBaseAI enemy = _registeredEnemies[i];

            if (enemy == null || !enemy.IsSpawned)
                _registeredEnemies.RemoveAt(i);
        }
    }

    private Transform FindClosestPlayer(Vector3 fromPosition)
    {
        if (NetworkManager.Singleton == null)
            return null;

        Transform bestTarget = null;
        float bestSqrDistance = float.MaxValue;

        foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null)
                continue;

            Transform candidate = client.PlayerObject.transform;
            float sqrDistance = (candidate.position - fromPosition).sqrMagnitude;

            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    private void RefreshFlowFieldIfNeeded(Transform flowTarget)
    {
        FlowFieldManager flow = FlowFieldManager.Instance;

        if (flow == null || !flow.Grid.IsCreated || flowTarget == null)
            return;

        _flowFieldUpdateTimer -= Time.deltaTime;
        if (_flowFieldUpdateTimer > 0f)
            return;

        flow.CalculateFlowField(flowTarget.position);
        _flowFieldUpdateTimer = _runtimeFlowFieldUpdateInterval;
    }

    private void CopyPositions(int count)
    {
        for (int i = 0; i < count; i++)
        {
            EnemyBaseAI enemy = _movingEnemies[i];
            _positions[i] = enemy.MyTransform.position;
            _targetPositions[i] = enemy.TargetPlayer.position;
            
            _preferredSides[i] = enemy.StablePreferredSide; 
        }
    }

    private JobHandle ScheduleFlowDirectionJob(int count, Transform flowTarget)
    {
        FlowFieldManager flow = FlowFieldManager.Instance;

        if (flow == null || !flow.Grid.IsCreated || flowTarget == null)
        {
            for (int i = 0; i < count; i++)
                _baseDirections[i] = float3.zero;

            return default;
        }

        return new EnemyFlowDirectionJob
        {
            FlowField = flow.Grid,
            Positions = _positions,
            Output = _baseDirections,
            GridOrigin = flow.GridOrigin,
            CellSize = flow.CellSize,
            GridWidth = flow.GridWidth,
            GridHeight = flow.GridHeight,
            TargetPosition = flowTarget.position
        }.Schedule(count, 64);
    }

    private void ApplyMovement(int count)
    {
        for (int i = 0; i < count; i++)
        {
            EnemyBaseAI enemy = _movingEnemies[i];

            if (!IsEnemyUsable(enemy) || enemy.TargetPlayer == null)
                continue;

            Vector3 desiredDirection = ToVector3(_finalDirections[i]);
            desiredDirection = ApplyLocalObstacleAvoidance(enemy, desiredDirection);

            enemy.ManualMove(desiredDirection * enemy.CurrentSpeed);
        }
    }

    private Vector3 ApplyLocalObstacleAvoidance(EnemyBaseAI enemy, Vector3 desiredDirection)
    {
        if (!_useLocalObstacleAvoidance || desiredDirection.sqrMagnitude < 0.0001f)
            return desiredDirection;

        int obstacleMask = _localObstacleMask.value;
        if (obstacleMask == 0 && FlowFieldManager.Instance != null)
            obstacleMask = FlowFieldManager.Instance.ObstacleMask.value;

        if (obstacleMask == 0)
            return desiredDirection;

        Vector3 normalizedDirection = desiredDirection.normalized;
        Vector3 origin = enemy.MyTransform.position + Vector3.up * _avoidanceHeight;

        if (!Physics.SphereCast(
                origin,
                _avoidanceSphereRadius,
                normalizedDirection,
                out RaycastHit hit,
                _avoidanceDistance,
                obstacleMask,
                QueryTriggerInteraction.Ignore))
        {
            return normalizedDirection;
        }

        Vector3 flatNormal = Vector3.ProjectOnPlane(hit.normal, Vector3.up);
        if (flatNormal.sqrMagnitude < 0.0001f)
            return normalizedDirection;

        flatNormal.Normalize();

        Vector3 tangentA = Vector3.Cross(Vector3.up, flatNormal).normalized;
        Vector3 tangentB = -tangentA;
        Vector3 tangent = Vector3.Dot(tangentA, normalizedDirection) >= Vector3.Dot(tangentB, normalizedDirection)
            ? tangentA
            : tangentB;

        Vector3 steerDirection = (tangent * 0.85f + flatNormal * 0.15f).normalized;
        float closeness = 1f - Mathf.Clamp01(hit.distance / _avoidanceDistance);
        float blend = _avoidanceBlend * Mathf.Lerp(0.35f, 1f, closeness);

        return Vector3.Slerp(normalizedDirection, steerDirection, blend).normalized;
    }

    private static float GetStablePreferredSide(EnemyBaseAI enemy)
    {
        unchecked
        {
            ulong id = enemy.NetworkObjectId != 0 ? enemy.NetworkObjectId : (ulong)(uint)enemy.GetInstanceID();
            uint hash = (uint)id;

            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;

            float side = (hash & 1u) == 0u ? -1f : 1f;
            float strength = 0.65f + (((hash >> 8) & 255u) / 255f) * 0.35f;
            return side * strength;
        }
    }

    private void EnsureCapacity(int requestedCount)
    {
        if (_positions.IsCreated && requestedCount <= _positions.Length)
            return;

        int newCapacity = Mathf.Max(64, requestedCount * 2);
        DisposeNativeData();
        AllocateNativeData(newCapacity);
    }

    private void AllocateNativeData(int capacity)
    {
        _positions = new NativeArray<float3>(capacity, Allocator.Persistent);
        _targetPositions = new NativeArray<float3>(capacity, Allocator.Persistent);
        _baseDirections = new NativeArray<float3>(capacity, Allocator.Persistent);
        _separationDirections = new NativeArray<float3>(capacity, Allocator.Persistent);
        _finalDirections = new NativeArray<float3>(capacity, Allocator.Persistent);
        _preferredSides = new NativeArray<float>(capacity, Allocator.Persistent);
        _spatialHash = new NativeParallelMultiHashMap<int, int>(capacity, Allocator.Persistent);
    }

    private void DisposeNativeData()
    {
        if (_positions.IsCreated) _positions.Dispose();
        if (_targetPositions.IsCreated) _targetPositions.Dispose();
        if (_baseDirections.IsCreated) _baseDirections.Dispose();
        if (_separationDirections.IsCreated) _separationDirections.Dispose();
        if (_finalDirections.IsCreated) _finalDirections.Dispose();
        if (_preferredSides.IsCreated) _preferredSides.Dispose();
        if (_spatialHash.IsCreated) _spatialHash.Dispose();
    }

    private static Vector3 ToVector3(float3 value)
    {
        return new Vector3(value.x, value.y, value.z);
    }

    [BurstCompile]
    private struct EnemySpatialHashBuildJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        public NativeParallelMultiHashMap<int, int>.ParallelWriter HashMap;
        public float CellSize;

        public void Execute(int index)
        {
            float3 position = Positions[index];
            int2 cell = WorldToCell(position, CellSize);
            HashMap.Add(HashCell(cell), index);
        }
    }

    [BurstCompile]
    private struct EnemyCrowdResolveJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> BaseDirections;
        [ReadOnly] public NativeArray<float> PreferredSides;
        [ReadOnly] public NativeParallelMultiHashMap<int, int> HashMap;
        [WriteOnly] public NativeArray<float3> Output;

        public float CellSize;
        public float SeparationRadius;
        public float SeparationForce;
        public float LineBreakRadius;
        public float LineBreakForce;
        public float LineBreakForwardDot;
        public float LineBreakSideDot;
        public int MaxNeighbours;

        public void Execute(int index)
        {
            bool useSeparation = SeparationRadius > 0f && SeparationForce > 0f;
            bool useLineBreak = LineBreakRadius > 0f && LineBreakForce > 0f;

            if ((!useSeparation && !useLineBreak) || MaxNeighbours == 0)
            {
                Output[index] = float3.zero;
                return;
            }

            float3 selfPosition = Positions[index];
            int2 centerCell = WorldToCell(selfPosition, CellSize);

            float radius = math.max(SeparationRadius, LineBreakRadius);
            float radiusSqr = radius * radius;
            float separationRadiusSqr = SeparationRadius * SeparationRadius;
            float lineBreakRadiusSqr = LineBreakRadius * LineBreakRadius;

            float3 forward = BaseDirections[index];
            forward.y = 0f;
            bool hasForward = math.lengthsq(forward) > 0.0001f;
            if (hasForward)
                forward = math.normalize(forward);

            float3 right = hasForward ? new float3(-forward.z, 0f, forward.x) : float3.zero;
            float preferredSide = PreferredSides[index];

            float3 push = float3.zero;
            int neighbourCount = 0;
            bool reachedLimit = false;

            for (int x = -1; x <= 1; x++)
            {
                for (int y = -1; y <= 1; y++)
                {
                    int2 neighbourCell = centerCell + new int2(x, y);
                    int hash = HashCell(neighbourCell);

                    NativeParallelMultiHashMapIterator<int> iterator;
                    int otherIndex;

                    if (!HashMap.TryGetFirstValue(hash, out otherIndex, out iterator))
                        continue;

                    do
                    {
                        if (otherIndex == index)
                            continue;

                        float3 otherPosition = Positions[otherIndex];
                        float3 deltaFromOther = selfPosition - otherPosition;
                        deltaFromOther.y = 0f;

                        float distanceSqr = math.lengthsq(deltaFromOther);
                        if (distanceSqr <= 0.0001f || distanceSqr > radiusSqr)
                            continue;

                        float distance = math.sqrt(distanceSqr);

                        if (useSeparation && distanceSqr <= separationRadiusSqr)
                        {
                            float strength = 1f - math.saturate(distance / SeparationRadius);
                            push += (deltaFromOther / distance) * strength * SeparationForce;
                        }

                        if (useLineBreak && hasForward && distanceSqr <= lineBreakRadiusSqr)
                        {
                            float3 toOther = -deltaFromOther / distance;
                            float forwardDot = math.dot(toOther, forward);
                            float sideDot = math.abs(math.dot(toOther, right));

                            if (forwardDot > LineBreakForwardDot && sideDot < LineBreakSideDot)
                            {
                                float distanceStrength = 1f - math.saturate(distance / LineBreakRadius);
                                float sideStrength = 1f - math.saturate(sideDot / math.max(0.0001f, LineBreakSideDot));
                                push += right * preferredSide * distanceStrength * sideStrength * forwardDot * LineBreakForce;
                            }
                        }

                        neighbourCount++;

                        if (MaxNeighbours > 0 && neighbourCount >= MaxNeighbours)
                        {
                            reachedLimit = true;
                            break;
                        }
                    }
                    while (HashMap.TryGetNextValue(out otherIndex, ref iterator));

                    if (reachedLimit)
                        break;
                }

                if (reachedLimit)
                    break;
            }

            if (neighbourCount > 0)
                push /= neighbourCount;

            Output[index] = push;
        }
    }

    [BurstCompile]
    private struct EnemyFinalDirectionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> Positions;
        [ReadOnly] public NativeArray<float3> TargetPositions;
        [ReadOnly] public NativeArray<float3> BaseDirections;
        [ReadOnly] public NativeArray<float3> SeparationDirections;
        [ReadOnly] public NativeArray<float> PreferredSides;
        [WriteOnly] public NativeArray<float3> Output;

        public float MaxSeparationInfluence;
        public float FallbackStopDistance;
        public float LineBreakDisableDistance;
        public float PersonalSideBias;

        public void Execute(int index)
        {
            float3 baseDirection = BaseDirections[index];
            float3 separation = ClampMagnitude(SeparationDirections[index], MaxSeparationInfluence);

            float3 toTarget = TargetPositions[index] - Positions[index];
            toTarget.y = 0f;

            float fallbackStopDistanceSqr = FallbackStopDistance * FallbackStopDistance;
            if (math.lengthsq(baseDirection) < 0.0001f && math.lengthsq(toTarget) > fallbackStopDistanceSqr)
                baseDirection = math.normalize(toTarget);

            float3 desiredDirection = baseDirection + separation;
            desiredDirection.y = 0f;

            float lineBreakDisableDistanceSqr = LineBreakDisableDistance * LineBreakDisableDistance;
            if (math.lengthsq(baseDirection) > 0.0001f && math.lengthsq(toTarget) > lineBreakDisableDistanceSqr)
            {
                float3 right = new float3(-baseDirection.z, 0f, baseDirection.x);
                desiredDirection += right * (PreferredSides[index] * PersonalSideBias);
            }

            if (math.lengthsq(desiredDirection) > 1f)
                desiredDirection = math.normalize(desiredDirection);

            Output[index] = desiredDirection;
        }

        private static float3 ClampMagnitude(float3 value, float maxLength)
        {
            maxLength = math.max(0f, maxLength);
            float lengthSqr = math.lengthsq(value);
            float maxLengthSqr = maxLength * maxLength;

            if (lengthSqr <= maxLengthSqr)
                return value;

            if (lengthSqr <= 0.000001f || maxLength <= 0f)
                return float3.zero;

            return value * (maxLength * math.rsqrt(lengthSqr));
        }
    }

    [BurstCompile]
    private struct EnemyFlowDirectionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<FlowCell> FlowField;
        [ReadOnly] public NativeArray<float3> Positions;
        [WriteOnly] public NativeArray<float3> Output;

        public float3 GridOrigin;
        public float CellSize;
        public int GridWidth;
        public int GridHeight;
        public float3 TargetPosition;

        public void Execute(int index)
        {
            float3 position = Positions[index];

            float2 direct = new float2(TargetPosition.x - position.x, TargetPosition.z - position.z);
            if (math.lengthsq(direct) > 0.0001f)
                direct = math.normalize(direct);
            else
                direct = float2.zero;

            int gridX = (int)math.floor((position.x - GridOrigin.x) / CellSize);
            int gridY = (int)math.floor((position.z - GridOrigin.z) / CellSize);

            // Important for moving flow fields:
            // an enemy can be outside the temporary player-centered grid.
            // In that case, do not clamp to an edge cell, because that can produce wrong directions.
            // Use direct fallback movement until the enemy enters the grid window.
            if (gridX < 0 || gridX >= GridWidth || gridY < 0 || gridY >= GridHeight)
            {
                Output[index] = new float3(direct.x, 0f, direct.y);
                return;
            }

            FlowCell cell = FlowField[gridX + gridY * GridWidth];
            float2 direction = cell.Direction;

            if (math.lengthsq(direction) > 0.0001f)
            {
                Output[index] = new float3(direction.x, 0f, direction.y);
                return;
            }

            Output[index] = new float3(direct.x, 0f, direct.y);
        }
    }

    private static int2 WorldToCell(float3 position, float cellSize)
    {
        return new int2(
            (int)math.floor(position.x / cellSize),
            (int)math.floor(position.z / cellSize)
        );
    }

    private static int HashCell(int2 cell)
    {
        unchecked
        {
            return (cell.x * 73856093) ^ (cell.y * 19349663);
        }
    }
}
