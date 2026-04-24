using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using System.Collections.Generic;

public struct FlowCell
{
    public byte Cost;
    public ushort BestCost;
    public float2 Direction;
}

public class FlowFieldManager : MonoBehaviour
{
    public static FlowFieldManager Instance { get; private set; }

    public NativeArray<FlowCell> Grid;
    public int GridWidth = 100;
    public int GridHeight = 100;
    public float CellSize = 2f;
    public float3 GridOrigin = float3.zero;
    public LayerMask TerrainMask;
    public LayerMask ObstacleMask;

    private readonly int2[] _neighborOffsets = {
        new int2(0, 1), new int2(0, -1), new int2(1, 0), new int2(-1, 0),
        new int2(1, 1), new int2(-1, -1), new int2(1, -1), new int2(-1, 1)
    };

    private void Awake()
    {
        Instance = this;
        Grid = new NativeArray<FlowCell>(GridWidth * GridHeight, Allocator.Persistent);
        GenerateCostField();
    }

    private void OnDestroy()
    {
        if (Grid.IsCreated) Grid.Dispose();
    }

    public void GenerateCostField()
    {
        // Extents jsou polovina velikosti hrany. 0.45f místo 0.5f zabraňuje falešným detekcím na hranách buněk.
        Vector3 halfExtents = new Vector3(CellSize * 0.45f, 2f, CellSize * 0.45f);

        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                int index = x + (y * GridWidth);
                FlowCell cell = Grid[index];

                // XZ střed buňky, Y na nule (předpoklad základní výšky, raycast určí přesnou)
                Vector3 cellCenterXZ = new Vector3(GridOrigin.x + x * CellSize, 0f, GridOrigin.z + y * CellSize);
                Vector3 rayStart = new Vector3(cellCenterXZ.x, 1000f, cellCenterXZ.z);

                // Zjištění výšky terénu v dané buňce
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 2000f, TerrainMask))
                {
                    float slopeAngle = Vector3.Angle(Vector3.up, hit.normal);

                    if (slopeAngle > 45f)
                    {
                        cell.Cost = 255; // Příliš strmý svah
                    }
                    else
                    {
                        // Bod dotyku s terénem posunutý nahoru pro střed BoxCastu
                        Vector3 boxCenter = hit.point + (Vector3.up * (halfExtents.y + 0.1f));

                        // Detekce statických překážek (domy, stromy)
                        if (Physics.CheckBox(boxCenter, halfExtents, Quaternion.identity, ObstacleMask))
                        {
                            cell.Cost = 255; // Překážka nalezena
                        }
                        else
                        {
                            cell.Cost = 1; // Volná cesta
                        }
                    }
                }
                else
                {
                    cell.Cost = 255; // Mimo terén / propast
                }

                Grid[index] = cell;
            }
        }
    }

    public void CalculateFlowField(Vector3 playerPosition)
    {
        int targetX = math.clamp((int)math.floor((playerPosition.x - GridOrigin.x) / CellSize), 0, GridWidth - 1);
        int targetY = math.clamp((int)math.floor((playerPosition.z - GridOrigin.z) / CellSize), 0, GridHeight - 1);
        int2 targetPos = new int2(targetX, targetY);

        for (int i = 0; i < Grid.Length; i++)
        {
            FlowCell cell = Grid[i];
            cell.BestCost = ushort.MaxValue;
            Grid[i] = cell;
        }

        Queue<int2> cellsToCheck = new Queue<int2>();

        int targetIndex = targetPos.x + (targetPos.y * GridWidth);
        FlowCell startCell = Grid[targetIndex];
        startCell.BestCost = 0;
        startCell.Cost = 1;
        Grid[targetIndex] = startCell;

        cellsToCheck.Enqueue(targetPos);

        while (cellsToCheck.Count > 0)
        {
            int2 current = cellsToCheck.Dequeue();
            int currentIndex = current.x + (current.y * GridWidth);
            ushort currentCost = Grid[currentIndex].BestCost;

            foreach (int2 offset in _neighborOffsets)
            {
                int2 neighbor = current + offset;
                if (neighbor.x < 0 || neighbor.x >= GridWidth || neighbor.y < 0 || neighbor.y >= GridHeight) continue;

                int neighborIndex = neighbor.x + (neighbor.y * GridWidth);
                FlowCell neighborCell = Grid[neighborIndex];

                if (neighborCell.Cost == 255) continue;

                ushort newCost = (ushort)(currentCost + neighborCell.Cost);
                if (newCost < neighborCell.BestCost)
                {
                    neighborCell.BestCost = newCost;
                    Grid[neighborIndex] = neighborCell;
                    cellsToCheck.Enqueue(neighbor);
                }
            }
        }

        for (int x = 0; x < GridWidth; x++)
        {
            for (int y = 0; y < GridHeight; y++)
            {
                int index = x + (y * GridWidth);
                FlowCell cell = Grid[index];

                ushort bestCost = cell.BestCost;
                int2 bestDirection = int2.zero;

                foreach (int2 offset in _neighborOffsets)
                {
                    int2 neighbor = new int2(x, y) + offset;
                    if (neighbor.x < 0 || neighbor.x >= GridWidth || neighbor.y < 0 || neighbor.y >= GridHeight) continue;

                    int neighborIndex = neighbor.x + (neighbor.y * GridWidth);
                    if (Grid[neighborIndex].BestCost < bestCost)
                    {
                        bestCost = Grid[neighborIndex].BestCost;
                        bestDirection = offset;
                    }
                }

                if (bestDirection.x != 0 || bestDirection.y != 0)
                {
                    cell.Direction = math.normalize(new float2(bestDirection.x, bestDirection.y));
                }
                else
                {
                    cell.Direction = float2.zero;
                }

                Grid[index] = cell;
            }
        }
    }
}