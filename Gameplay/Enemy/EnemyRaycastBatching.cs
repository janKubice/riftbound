using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

public class EnemyRaycastBatching : MonoBehaviour
{
    private NativeArray<RaycastCommand> _commands;
    private NativeArray<RaycastHit> _results;
    private int _layerMask;

    private void Start()
    {
        _layerMask = LayerMask.GetMask("Enviroment");
    }

    public JobHandle ScheduleRaycasts(NativeArray<float3> positions, int count, JobHandle dependency)
    {
        _commands = new NativeArray<RaycastCommand>(count, Allocator.TempJob);
        _results = new NativeArray<RaycastHit>(count, Allocator.TempJob);

        var setupJob = new SetupRaycastsJob
        {
            Positions = positions,
            Commands = _commands,
            LayerMask = _layerMask
        };
        JobHandle setupHandle = setupJob.Schedule(count, 64, dependency);

        // Batch scheduling Unity fyziky
        JobHandle raycastHandle = RaycastCommand.ScheduleBatch(_commands, _results, 64, setupHandle);
        return raycastHandle;
    }

    public void ProcessResultsAndDispose(JobHandle raycastHandle, NativeArray<float3> directions, int count)
    {
        var processJob = new ProcessHitsJob
        {
            Hits = _results,
            Directions = directions
        };
        JobHandle processHandle = processJob.Schedule(count, 64, raycastHandle);
        processHandle.Complete();

        _commands.Dispose();
        _results.Dispose();
    }
}

public struct SetupRaycastsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<float3> Positions;
    [WriteOnly] public NativeArray<RaycastCommand> Commands;
    public int LayerMask;

    public void Execute(int index)
    {
        float3 origin = Positions[index] + new float3(0, 3f, 0);
        float3 direction = new float3(0, -1f, 0);
        QueryParameters queryParameters = new QueryParameters(LayerMask, false, QueryTriggerInteraction.Ignore, false);
        Commands[index] = new RaycastCommand(origin, direction, queryParameters, 5f);
    }
}

public struct ProcessHitsJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<RaycastHit> Hits;
    public NativeArray<float3> Directions; // Zde jsou předpočítané horizontální směry (Separace + Cíl)

    public void Execute(int index)
    {
        if (Hits[index].colliderInstanceID != 0)
        {
            // Projekce vektoru na normálu svahu
            float3 normal = Hits[index].normal;
            float3 dir = Directions[index];
            Directions[index] = math.normalize(dir - math.project(dir, normal));
        }
    }
}