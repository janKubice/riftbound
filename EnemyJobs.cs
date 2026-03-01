using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct HashPositionsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    public NativeParallelMultiHashMap<int, int>.ParallelWriter HashMap;
    public float CellSize;

    public void Execute(int index)
    {
        int hash = Hash(Positions[index], CellSize);
        HashMap.Add(hash, index);
    }

    public static int Hash(float3 pos, float cellSize)
    {
        int x = (int)math.floor(pos.x / cellSize);
        int z = (int)math.floor(pos.z / cellSize);
        return (x * 73856093) ^ (z * 83492791);
    }
}

[BurstCompile]
public struct EnemySeparationJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    [ReadOnly] public NativeParallelMultiHashMap<int, int> HashMap;
    public float SeparationRadius;
    public float SeparationForce;
    public float CellSize;
    
    public NativeArray<float3> SeparationVectors;

    public void Execute(int index)
    {
        float3 myPos = Positions[index];
        float3 separation = float3.zero;
        int count = 0;
        float radiusSq = SeparationRadius * SeparationRadius;

        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                float3 offset = new float3(x * CellSize, 0, z * CellSize);
                int hash = HashPositionsJob.Hash(myPos + offset, CellSize);

                if (HashMap.TryGetFirstValue(hash, out int otherIndex, out var it))
                {
                    do
                    {
                        if (index == otherIndex) continue;

                        float3 dir = myPos - Positions[otherIndex];
                        float distSq = math.lengthsq(dir);

                        if (distSq < radiusSq && distSq > 0.0001f)
                        {
                            float dist = math.sqrt(distSq);
                            separation += (dir / dist) * (1.0f - (dist / SeparationRadius));
                            count++;
                        }
                    } while (HashMap.TryGetNextValue(out otherIndex, ref it));
                }
            }
        }

        SeparationVectors[index] = count > 0 ? (separation / count) * SeparationForce : float3.zero;
    }
}