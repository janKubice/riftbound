using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class WorldEventDirector : NetworkBehaviour
{
    public static WorldEventDirector Instance { get; private set; }

    [Header("Definitions")]
    [SerializeField] private List<WorldEventDefinition> _eventDefinitions = new List<WorldEventDefinition>();

    [Header("Initial World Population")]
    [SerializeField] private bool _spawnDormantEventsAtRunStart = true;

    [SerializeField] private int _initialDormantPOICount = 10;

    [SerializeField] private float _initialSpawnDelay = 0.25f;

    [Header("Activation")]
    [SerializeField] private bool _activateEventsOverTime = true;

    [Tooltip("Od jaké minuty runu může WorldEventDirector začít aktivovat PoI.")]
    [SerializeField] private float _firstActivationMinute = 1.0f;

    [SerializeField] private Vector2 _activationIntervalSeconds = new Vector2(45f, 75f);

    [SerializeField] private int _maxActivePOIs = 2;

    [Header("Dynamic Spawn Fallback")]
    [SerializeField] private bool _allowDynamicSpawnIfNoDormantFound = true;

    [Header("Placement")]
    [SerializeField] private LayerMask _groundMask = ~0;
    [SerializeField] private float _groundRayHeight = 20f;
    [SerializeField] private float _groundRayDistance = 50f;
    [SerializeField] private float _groundOffset = 0.05f;

    [Header("Player Distance")]
    [SerializeField] private bool _avoidActivatingTooCloseToPlayers = true;
    [SerializeField] private float _minActivationDistanceFromPlayer = 18f;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs = true;

    private readonly List<SpawnedPOIRecord> _spawnedRecords = new List<SpawnedPOIRecord>(64);

    private float _nextActivationTime;
    private bool _initialSpawnDone;
    private float _localRunTimeMinutes;
    private float _fallbackRunStartTime;

    private class SpawnedPOIRecord
    {
        public WorldPOIBase Poi;
        public WorldEventDefinition Definition;
        public WorldEventSpawnPoint SpawnPoint;
    }

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
        if (!IsServer)
            return;

        ScheduleNextActivation();

        if (_spawnDormantEventsAtRunStart)
            StartCoroutine(InitialSpawnRoutine());
        else
            _initialSpawnDone = true;

        StartCoroutine(DirectorLoopRoutine());
    }

    private IEnumerator DirectorLoopRoutine()
    {
        // Čekání na dokončení počátečního spawnu
        while (!_initialSpawnDone)
            yield return null;

        while (IsServer)
        {
            if (!_activateEventsOverTime)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            UpdateRunTime();

            if (_localRunTimeMinutes < _firstActivationMinute)
            {
                yield return new WaitForSeconds(1f);
                continue;
            }

            // Pokud je plno, odložíme další kontrolu o 8-14 sekund
            if (CountActivePOIs() >= _maxActivePOIs)
            {
                yield return new WaitForSeconds(Random.Range(8f, 14f));
                continue;
            }

            bool activated = TryActivateDormantPOI();

            if (!activated && _allowDynamicSpawnIfNoDormantFound)
                activated = TryDynamicSpawnAndActivate();

            if (_debugLogs && !activated)
                Debug.Log("[WorldEventDirector] No suitable POI was activated.", this);

            // Uspání korutiny do další regulérní aktivace (45-75 sekund)
            float delay = Random.Range(_activationIntervalSeconds.x, _activationIntervalSeconds.y);
            yield return new WaitForSeconds(delay);
        }
    }

    private void UpdateRunTime()
    {
        if (DirectorSpawner.Instance != null)
        {
            _localRunTimeMinutes = DirectorSpawner.Instance.GetRunTimeMinutes();
        }
        else
        {
            // Výpočet z absolutního času je nezávislý na frekvenci volání
            _localRunTimeMinutes = (Time.time - _fallbackRunStartTime) / 60f;
        }
    }

    private IEnumerator InitialSpawnRoutine()
    {
        yield return new WaitForSeconds(_initialSpawnDelay);

        int spawned = 0;

        for (int i = 0; i < _initialDormantPOICount; i++)
        {
            WorldEventDefinition definition = PickDefinitionForPreSpawn();

            if (definition == null)
                break;

            WorldEventSpawnPoint spawnPoint = PickSpawnPoint(definition.Category);

            if (spawnPoint == null)
                break;

            if (TrySpawnPOI(definition, spawnPoint, false, out _))
                spawned++;
        }

        _initialSpawnDone = true;

        if (_debugLogs)
            Debug.Log($"[WorldEventDirector] Initial dormant POIs spawned: {spawned}", this);
    }

    private bool TryActivateDormantPOI()
    {
        List<SpawnedPOIRecord> candidates = new List<SpawnedPOIRecord>();

        for (int i = 0; i < _spawnedRecords.Count; i++)
        {
            SpawnedPOIRecord record = _spawnedRecords[i];

            if (record == null || record.Poi == null || record.Definition == null)
                continue;

            if (!record.Poi.IsDormant)
                continue;

            if (!record.Definition.IsAvailableAtMinute(_localRunTimeMinutes))
                continue;

            if (CountActiveOfDefinition(record.Definition) >= record.Definition.MaxActiveInstances)
                continue;

            if (_avoidActivatingTooCloseToPlayers &&
                IsTooCloseToAnyPlayer(record.Poi.transform.position, _minActivationDistanceFromPlayer))
            {
                continue;
            }

            candidates.Add(record);
        }

        if (candidates.Count == 0)
            return false;

        SpawnedPOIRecord selected = candidates[Random.Range(0, candidates.Count)];

        selected.Poi.ActivateServer(selected.Definition.ActiveDurationSeconds);

        AnnounceActivatedClientRpc(
            selected.Definition.EventName,
            selected.Poi.transform.position
        );

        if (_debugLogs)
            Debug.Log($"[WorldEventDirector] Activated POI: {selected.Definition.EventName}", selected.Poi);

        return true;
    }

    private bool TryDynamicSpawnAndActivate()
    {
        WorldEventDefinition definition = PickDefinitionForDynamicSpawn();

        if (definition == null)
            return false;

        WorldEventSpawnPoint spawnPoint = PickSpawnPoint(definition.Category);

        if (spawnPoint == null)
            return false;

        if (!TrySpawnPOI(definition, spawnPoint, true, out WorldPOIBase poi))
            return false;

        poi.ActivateServer(definition.ActiveDurationSeconds);

        AnnounceActivatedClientRpc(definition.EventName, poi.transform.position);

        if (_debugLogs)
            Debug.Log($"[WorldEventDirector] Dynamically spawned and activated: {definition.EventName}", poi);

        return true;
    }

    private bool TrySpawnPOI(
        WorldEventDefinition definition,
        WorldEventSpawnPoint spawnPoint,
        bool activateImmediately,
        out WorldPOIBase instance)
    {
        instance = null;

        if (definition == null || definition.Prefab == null || spawnPoint == null)
            return false;

        Vector3 rawPosition = spawnPoint.GetRandomPosition();

        if (!TryResolveGroundPosition(rawPosition, definition.VerticalSpawnOffset, out Vector3 spawnPosition))
            return false;

        Quaternion rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        instance = Instantiate(definition.Prefab, spawnPosition, rotation);

        NetworkObject netObj = instance.GetComponent<NetworkObject>();

        if (netObj == null)
        {
            Debug.LogError($"POI prefab '{definition.Prefab.name}' has no NetworkObject.", definition.Prefab);
            Destroy(instance.gameObject);
            return false;
        }

        netObj.Spawn(true);

        SpawnedPOIRecord record = new SpawnedPOIRecord
        {
            Poi = instance,
            Definition = definition,
            SpawnPoint = spawnPoint
        };

        _spawnedRecords.Add(record);

        spawnPoint.IsOccupied = true;

        instance.ServerCompleted += HandlePOICompleted;

        if (activateImmediately)
            instance.ActivateServer(definition.ActiveDurationSeconds);
        else
            instance.SetDormantServer();

        return true;
    }

    private void HandlePOICompleted(WorldPOIBase poi)
    {
        if (_debugLogs && poi != null)
            Debug.Log($"[WorldEventDirector] Completed POI: {poi.DisplayName}", poi);
    }

    private WorldEventDefinition PickDefinitionForPreSpawn()
    {
        float totalWeight = 0f;

        for (int i = 0; i < _eventDefinitions.Count; i++)
        {
            WorldEventDefinition def = _eventDefinitions[i];

            if (def == null || !def.CanPreSpawn || def.Prefab == null)
                continue;

            if (def.PreSpawnWeight <= 0f)
                continue;

            if (CountInstancesOfDefinition(def) >= def.MaxInstances)
                continue;

            totalWeight += def.PreSpawnWeight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        float current = 0f;

        for (int i = 0; i < _eventDefinitions.Count; i++)
        {
            WorldEventDefinition def = _eventDefinitions[i];

            if (def == null || !def.CanPreSpawn || def.Prefab == null)
                continue;

            if (def.PreSpawnWeight <= 0f)
                continue;

            if (CountInstancesOfDefinition(def) >= def.MaxInstances)
                continue;

            current += def.PreSpawnWeight;

            if (roll <= current)
                return def;
        }

        return null;
    }

    private WorldEventDefinition PickDefinitionForDynamicSpawn()
    {
        float totalWeight = 0f;

        for (int i = 0; i < _eventDefinitions.Count; i++)
        {
            WorldEventDefinition def = _eventDefinitions[i];

            if (def == null || !def.CanDynamicSpawn || def.Prefab == null)
                continue;

            if (!def.IsAvailableAtMinute(_localRunTimeMinutes))
                continue;

            if (def.DynamicSpawnWeight <= 0f)
                continue;

            if (CountInstancesOfDefinition(def) >= def.MaxInstances)
                continue;

            totalWeight += def.DynamicSpawnWeight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        float current = 0f;

        for (int i = 0; i < _eventDefinitions.Count; i++)
        {
            WorldEventDefinition def = _eventDefinitions[i];

            if (def == null || !def.CanDynamicSpawn || def.Prefab == null)
                continue;

            if (!def.IsAvailableAtMinute(_localRunTimeMinutes))
                continue;

            if (def.DynamicSpawnWeight <= 0f)
                continue;

            if (CountInstancesOfDefinition(def) >= def.MaxInstances)
                continue;

            current += def.DynamicSpawnWeight;

            if (roll <= current)
                return def;
        }

        return null;
    }

    private WorldEventSpawnPoint PickSpawnPoint(WorldPOICategory category)
    {
        float totalWeight = 0f;

        for (int i = 0; i < WorldEventSpawnPoint.All.Count; i++)
        {
            WorldEventSpawnPoint point = WorldEventSpawnPoint.All[i];

            if (point == null || point.IsOccupied)
                continue;

            if (!point.Allows(category))
                continue;

            if (point.Weight <= 0f)
                continue;

            totalWeight += point.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = Random.Range(0f, totalWeight);
        float current = 0f;

        for (int i = 0; i < WorldEventSpawnPoint.All.Count; i++)
        {
            WorldEventSpawnPoint point = WorldEventSpawnPoint.All[i];

            if (point == null || point.IsOccupied)
                continue;

            if (!point.Allows(category))
                continue;

            if (point.Weight <= 0f)
                continue;

            current += point.Weight;

            if (roll <= current)
                return point;
        }

        return null;
    }

    private bool TryResolveGroundPosition(Vector3 rawPosition, float verticalOffset, out Vector3 groundPosition)
    {
        Vector3 rayOrigin = rawPosition + Vector3.up * _groundRayHeight;

        if (Physics.Raycast(
                rayOrigin,
                Vector3.down,
                out RaycastHit hit,
                _groundRayDistance,
                _groundMask,
                QueryTriggerInteraction.Ignore))
        {
            // Aplikace specifického offsetu pro konkrétní prefab
            groundPosition = hit.point + Vector3.up * verticalOffset;
            return true;
        }

        groundPosition = rawPosition;
        return false;
    }

    private int CountInstancesOfDefinition(WorldEventDefinition definition)
    {
        int count = 0;

        for (int i = 0; i < _spawnedRecords.Count; i++)
        {
            SpawnedPOIRecord record = _spawnedRecords[i];

            if (record != null && record.Definition == definition && record.Poi != null)
                count++;
        }

        return count;
    }

    private int CountActiveOfDefinition(WorldEventDefinition definition)
    {
        int count = 0;

        for (int i = 0; i < _spawnedRecords.Count; i++)
        {
            SpawnedPOIRecord record = _spawnedRecords[i];

            if (record == null || record.Definition != definition || record.Poi == null)
                continue;

            if (record.Poi.IsActive)
                count++;
        }

        return count;
    }

    private int CountActivePOIs()
    {
        int count = 0;

        for (int i = 0; i < _spawnedRecords.Count; i++)
        {
            SpawnedPOIRecord record = _spawnedRecords[i];

            if (record != null && record.Poi != null && record.Poi.IsActive)
                count++;
        }

        return count;
    }

    private bool IsTooCloseToAnyPlayer(Vector3 position, float minDistance)
    {
        if (NetworkManager.Singleton == null)
            return false;

        float sqrMin = minDistance * minDistance;

        IReadOnlyList<NetworkClient> clients = NetworkManager.Singleton.ConnectedClientsList;

        for (int i = 0; i < clients.Count; i++)
        {
            NetworkObject playerObj = clients[i].PlayerObject;

            if (playerObj == null)
                continue;

            float sqrDistance = (playerObj.transform.position - position).sqrMagnitude;

            if (sqrDistance < sqrMin)
                return true;
        }

        return false;
    }

    private void ScheduleNextActivation()
    {
        ScheduleNextActivation(_activationIntervalSeconds.x, _activationIntervalSeconds.y);
    }

    private void ScheduleNextActivation(float min, float max)
    {
        float delay = Random.Range(
            Mathf.Max(1f, min),
            Mathf.Max(min + 0.1f, max)
        );

        _nextActivationTime = Time.time + delay;
    }

    [ClientRpc]
    private void AnnounceActivatedClientRpc(string eventName, Vector3 worldPosition)
    {
        Debug.Log($"World Event Activated: {eventName} at {worldPosition}");

        // Později sem napojíme UI marker:
        // WorldEventUI.Instance.ShowEvent(eventName, worldPosition);
        // CompassMarkerSystem.Instance.AddTemporaryMarker(eventName, worldPosition);
    }
}