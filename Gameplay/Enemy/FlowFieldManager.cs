using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public struct FlowCell
{
    public byte Cost;              // 1 = walkable, 255 = blocked
    public ushort BestCost;        // integrated cost toward target
    public float2 Direction;       // normalized XZ direction
}

public class FlowFieldManager : MonoBehaviour
{
    public static FlowFieldManager Instance { get; private set; }

    public NativeArray<FlowCell> Grid;

    [Header("Grid")]
    public int GridWidth = 120;
    public int GridHeight = 120;
    public float CellSize = 4f;

    // Bottom-left world corner of the current grid window.
    public float3 GridOrigin = float3.zero;

    [Header("Moving Grid")]
    [SerializeField] private bool _useMovingGrid = true;

    [Tooltip("Grid recenters only when the target is this many cells away from the grid center.")]
    [SerializeField, Min(1)] private int _recenterThresholdCells = 20;

    [Tooltip("Keeps the moving grid inside the rough map bounds. Useful near map edges.")]
    [SerializeField] private bool _clampToMapBounds = true;

    [Tooltip("Map minimum XZ, not XY. For your map: X=-500, Z=-800.")]
    [SerializeField] private Vector2 _mapMinXZ = new Vector2(-500f, -800f);

    [Tooltip("Map maximum XZ. For your map: X=700, Z=650.")]
    [SerializeField] private Vector2 _mapMaxXZ = new Vector2(700f, 650f);

    [SerializeField] private float _mapBoundsBuffer = 20f;

    [Header("Agent Clearance")]
    [Tooltip("Přibližný radius CharacterControlleru enemy. Flow field podle toho nafoukne překážky.")]
    [SerializeField] private float _agentRadius = 0.55f;

    [Tooltip("Malá rezerva kolem překážek. Pomáhá hlavně proti zasekávání o stromy a kameny.")]
    [SerializeField] private float _obstaclePadding = 0.15f;

    [Tooltip("Výška box probe pro detekci překážek. Nemusí být přesná výška enemy, stačí aby trefila kmeny/stěny.")]
    [SerializeField] private float _obstacleProbeHalfHeight = 1.25f;

    [Header("Detection")]
    public LayerMask TerrainMask;
    public LayerMask ObstacleMask;
    [SerializeField] private float _maxWalkableSlope = 45f;
    [SerializeField] private int _nearestTargetSearchRadius = 5;

    [Header("Debug")]
    [SerializeField] private bool _drawGridBounds = true;

    private readonly int2[] _neighborOffsets =
    {
        new int2(0, 1), new int2(0, -1), new int2(1, 0), new int2(-1, 0),
        new int2(1, 1), new int2(-1, -1), new int2(1, -1), new int2(-1, 1)
    };

    private readonly Queue<int2> _cellsToCheck = new Queue<int2>(4096);
    private bool _costFieldGenerated;

    private void OnValidate()
    {
        GridWidth = Mathf.Max(4, GridWidth);
        GridHeight = Mathf.Max(4, GridHeight);
        CellSize = Mathf.Max(0.25f, CellSize);
        _recenterThresholdCells = Mathf.Max(1, _recenterThresholdCells);
        _mapBoundsBuffer = Mathf.Max(0f, _mapBoundsBuffer);
        _agentRadius = Mathf.Max(0.05f, _agentRadius);
        _obstaclePadding = Mathf.Max(0f, _obstaclePadding);
        _obstacleProbeHalfHeight = Mathf.Max(0.1f, _obstacleProbeHalfHeight);
        _maxWalkableSlope = Mathf.Clamp(_maxWalkableSlope, 0f, 89f);
        _nearestTargetSearchRadius = Mathf.Max(1, _nearestTargetSearchRadius);
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        AllocateGrid();
        GenerateCostField();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        DisposeGrid();
    }

    public void CalculateFlowField(Vector3 targetWorldPosition)
    {
        if (!Grid.IsCreated || Grid.Length == 0)
            return;

        UpdateMovingGridIfNeeded(targetWorldPosition);

        if (!_costFieldGenerated)
            GenerateCostField();

        int2 requestedTarget = WorldToCellClamped(targetWorldPosition);

        if (!TryFindNearestWalkableCell(requestedTarget, out int2 targetCell))
            return;

        ResetIntegratedField();

        _cellsToCheck.Clear();

        int targetIndex = ToIndex(targetCell);
        FlowCell startCell = Grid[targetIndex];
        startCell.BestCost = 0;
        startCell.Direction = float2.zero;
        Grid[targetIndex] = startCell;

        _cellsToCheck.Enqueue(targetCell);

        while (_cellsToCheck.Count > 0)
        {
            int2 current = _cellsToCheck.Dequeue();
            int currentIndex = ToIndex(current);
            ushort currentCost = Grid[currentIndex].BestCost;

            foreach (int2 offset in _neighborOffsets)
            {
                int2 neighbor = current + offset;

                if (!CanStep(current, neighbor))
                    continue;

                int neighborIndex = ToIndex(neighbor);
                FlowCell neighborCell = Grid[neighborIndex];

                int moveCost = IsDiagonal(offset) ? 14 : 10;
                int candidate = currentCost + (moveCost * neighborCell.Cost);

                if (candidate < neighborCell.BestCost && candidate < ushort.MaxValue)
                {
                    neighborCell.BestCost = (ushort)candidate;
                    Grid[neighborIndex] = neighborCell;
                    _cellsToCheck.Enqueue(neighbor);
                }
            }
        }

        BuildDirectionField();
    }

    public void ForceRecenterAround(Vector3 targetWorldPosition)
    {
        if (!_useMovingGrid)
            return;

        MoveGridToCenter(targetWorldPosition);
    }

    public bool IsWorldPositionInsideGrid(Vector3 worldPosition)
    {
        int2 cell = WorldToCellUnclamped(worldPosition);
        return IsInside(cell);
    }

    public int2 WorldToCellClamped(Vector3 worldPosition)
    {
        int2 unclamped = WorldToCellUnclamped(worldPosition);

        return new int2(
            math.clamp(unclamped.x, 0, GridWidth - 1),
            math.clamp(unclamped.y, 0, GridHeight - 1)
        );
    }

    public int2 WorldToCellUnclamped(Vector3 worldPosition)
    {
        int x = (int)math.floor((worldPosition.x - GridOrigin.x) / CellSize);
        int y = (int)math.floor((worldPosition.z - GridOrigin.z) / CellSize);
        return new int2(x, y);
    }

    public Vector3 GetCellCenterWorld(int x, int y)
    {
        return new Vector3(
            GridOrigin.x + (x + 0.5f) * CellSize,
            0f,
            GridOrigin.z + (y + 0.5f) * CellSize
        );
    }

    public Vector3 GetGridWorldCenter()
    {
        return new Vector3(
            GridOrigin.x + GridWidth * CellSize * 0.5f,
            0f,
            GridOrigin.z + GridHeight * CellSize * 0.5f
        );
    }

    public void GenerateCostField()
    {
        if (!Grid.IsCreated)
            return;

        float horizontalProbeHalfExtent = CellSize * 0.45f + _agentRadius + _obstaclePadding;
        Vector3 halfExtents = new Vector3(horizontalProbeHalfExtent, _obstacleProbeHalfHeight, horizontalProbeHalfExtent);

        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                int index = ToIndex(new int2(x, y));
                FlowCell cell = Grid[index];

                Vector3 cellCenterXZ = GetCellCenterWorld(x, y);
                Vector3 rayStart = new Vector3(cellCenterXZ.x, 1000f, cellCenterXZ.z);

                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2000f, TerrainMask, QueryTriggerInteraction.Ignore))
                {
                    float slopeAngle = Vector3.Angle(Vector3.up, hit.normal);

                    if (slopeAngle > _maxWalkableSlope)
                    {
                        cell.Cost = 255;
                    }
                    else
                    {
                        Vector3 boxCenter = hit.point + Vector3.up * (halfExtents.y + 0.1f);
                        bool blocked = Physics.CheckBox(boxCenter, halfExtents, Quaternion.identity, ObstacleMask, QueryTriggerInteraction.Ignore);
                        cell.Cost = blocked ? (byte)255 : (byte)1;
                    }
                }
                else
                {
                    cell.Cost = 255;
                }

                cell.BestCost = ushort.MaxValue;
                cell.Direction = float2.zero;
                Grid[index] = cell;
            }
        }

        _costFieldGenerated = true;
    }

    private void UpdateMovingGridIfNeeded(Vector3 targetWorldPosition)
    {
        if (!_useMovingGrid)
            return;

        int2 targetCell = WorldToCellUnclamped(targetWorldPosition);
        int centerX = GridWidth / 2;
        int centerY = GridHeight / 2;

        bool targetOutsideGrid =
            targetCell.x < 0 ||
            targetCell.x >= GridWidth ||
            targetCell.y < 0 ||
            targetCell.y >= GridHeight;

        bool targetTooFarFromCenter =
            math.abs(targetCell.x - centerX) > _recenterThresholdCells ||
            math.abs(targetCell.y - centerY) > _recenterThresholdCells;

        if (!targetOutsideGrid && !targetTooFarFromCenter)
            return;

        MoveGridToCenter(targetWorldPosition);
    }

    private void MoveGridToCenter(Vector3 targetWorldPosition)
    {
        float3 newOrigin = CalculateSnappedCenteredOrigin(targetWorldPosition);

        if (_clampToMapBounds)
            newOrigin = ClampOriginToMapBounds(newOrigin);

        if (math.distancesq(new float2(GridOrigin.x, GridOrigin.z), new float2(newOrigin.x, newOrigin.z)) < 0.0001f)
            return;

        GridOrigin = newOrigin;
        GenerateCostField();
    }

    private float3 CalculateSnappedCenteredOrigin(Vector3 center)
    {
        float worldWidth = GridWidth * CellSize;
        float worldHeight = GridHeight * CellSize;

        float originX = center.x - worldWidth * 0.5f;
        float originZ = center.z - worldHeight * 0.5f;

        // Snap to whole cells. Without this, tiny target movement shifts the grid and causes jitter.
        originX = Mathf.Floor(originX / CellSize) * CellSize;
        originZ = Mathf.Floor(originZ / CellSize) * CellSize;

        return new float3(originX, 0f, originZ);
    }

    private float3 ClampOriginToMapBounds(float3 origin)
    {
        float worldWidth = GridWidth * CellSize;
        float worldHeight = GridHeight * CellSize;

        float minOriginX = _mapMinXZ.x - _mapBoundsBuffer;
        float maxOriginX = _mapMaxXZ.x + _mapBoundsBuffer - worldWidth;

        float minOriginZ = _mapMinXZ.y - _mapBoundsBuffer;
        float maxOriginZ = _mapMaxXZ.y + _mapBoundsBuffer - worldHeight;

        if (minOriginX <= maxOriginX)
            origin.x = Mathf.Clamp(origin.x, minOriginX, maxOriginX);
        else
            origin.x = (minOriginX + maxOriginX) * 0.5f;

        if (minOriginZ <= maxOriginZ)
            origin.z = Mathf.Clamp(origin.z, minOriginZ, maxOriginZ);
        else
            origin.z = (minOriginZ + maxOriginZ) * 0.5f;

        return origin;
    }

    private void ResetIntegratedField()
    {
        for (int i = 0; i < Grid.Length; i++)
        {
            FlowCell cell = Grid[i];
            cell.BestCost = ushort.MaxValue;
            cell.Direction = float2.zero;
            Grid[i] = cell;
        }
    }

    private void BuildDirectionField()
    {
        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                int2 current = new int2(x, y);
                int index = ToIndex(current);
                FlowCell cell = Grid[index];

                if (cell.Cost == 255 || cell.BestCost == ushort.MaxValue)
                {
                    cell.Direction = float2.zero;
                    Grid[index] = cell;
                    continue;
                }

                ushort bestCost = cell.BestCost;
                int2 bestDirection = int2.zero;

                foreach (int2 offset in _neighborOffsets)
                {
                    int2 neighbor = current + offset;

                    if (!CanStep(current, neighbor))
                        continue;

                    ushort neighborCost = Grid[ToIndex(neighbor)].BestCost;

                    if (neighborCost < bestCost)
                    {
                        bestCost = neighborCost;
                        bestDirection = offset;
                    }
                }

                cell.Direction = bestDirection.x != 0 || bestDirection.y != 0
                    ? math.normalize(new float2(bestDirection.x, bestDirection.y))
                    : float2.zero;

                Grid[index] = cell;
            }
        }
    }

    private bool TryFindNearestWalkableCell(int2 requested, out int2 result)
    {
        requested = new int2(
            math.clamp(requested.x, 0, GridWidth - 1),
            math.clamp(requested.y, 0, GridHeight - 1)
        );

        if (IsWalkable(requested))
        {
            result = requested;
            return true;
        }

        for (int r = 1; r <= _nearestTargetSearchRadius; r++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dy = -r; dy <= r; dy++)
                {
                    if (math.abs(dx) != r && math.abs(dy) != r)
                        continue;

                    int2 candidate = requested + new int2(dx, dy);

                    if (IsWalkable(candidate))
                    {
                        result = candidate;
                        return true;
                    }
                }
            }
        }

        result = requested;
        return false;
    }

    private int ToIndex(int2 cell)
    {
        return cell.x + cell.y * GridWidth;
    }

    private bool IsInside(int2 cell)
    {
        return cell.x >= 0 && cell.x < GridWidth && cell.y >= 0 && cell.y < GridHeight;
    }

    private bool IsWalkable(int2 cell)
    {
        return IsInside(cell) && Grid[ToIndex(cell)].Cost != 255;
    }

    private bool IsDiagonal(int2 offset)
    {
        return offset.x != 0 && offset.y != 0;
    }

    private bool CanStep(int2 from, int2 to)
    {
        if (!IsWalkable(to))
            return false;

        int dx = to.x - from.x;
        int dy = to.y - from.y;

        if (dx != 0 && dy != 0)
        {
            if (!IsWalkable(new int2(from.x + dx, from.y)))
                return false;

            if (!IsWalkable(new int2(from.x, from.y + dy)))
                return false;
        }

        return true;
    }

    private void AllocateGrid()
    {
        DisposeGrid();
        Grid = new NativeArray<FlowCell>(GridWidth * GridHeight, Allocator.Persistent);
        _costFieldGenerated = false;
    }

    private void DisposeGrid()
    {
        if (Grid.IsCreated)
            Grid.Dispose();
    }

    private void OnDrawGizmosSelected()
    {
        if (!_drawGridBounds)
            return;

        Vector3 center = new Vector3(
            GridOrigin.x + GridWidth * CellSize * 0.5f,
            2f,
            GridOrigin.z + GridHeight * CellSize * 0.5f
        );

        Vector3 size = new Vector3(
            GridWidth * CellSize,
            4f,
            GridHeight * CellSize
        );

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(center, size);

        if (_clampToMapBounds)
        {
            Vector3 mapCenter = new Vector3(
                (_mapMinXZ.x + _mapMaxXZ.x) * 0.5f,
                1f,
                (_mapMinXZ.y + _mapMaxXZ.y) * 0.5f
            );

            Vector3 mapSize = new Vector3(
                (_mapMaxXZ.x - _mapMinXZ.x) + _mapBoundsBuffer * 2f,
                2f,
                (_mapMaxXZ.y - _mapMinXZ.y) + _mapBoundsBuffer * 2f
            );

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(mapCenter, mapSize);
        }
    }
}
