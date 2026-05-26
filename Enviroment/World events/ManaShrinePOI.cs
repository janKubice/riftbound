using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class ManaShrinePOI : WorldPOIBase
{
    public static event Action<NetworkObject, ManaShrinePOI> OnManaShrineCompletedServer;

    [Header("Mana Shrine")]
    [SerializeField] private float _activationRadius = 6.0f;
    [SerializeField] private float _requiredChannelSeconds = 22.0f;

    [Tooltip("Pokud je true, progress roste jen když je alespoň jeden hráč v radiusu.")]
    [SerializeField] private bool _requiresPlayerInsideRadius = true;

    [Tooltip("Pokud je true, progress pomalu klesá, když hráč odejde z radiusu.")]
    [SerializeField] private bool _decayProgressWhenEmpty = true;

    [SerializeField] private float _progressDecaySpeed = 0.45f;

    [Header("Pressure Spawns")]
    [SerializeField] private bool _spawnPressureEnemies = true;

    [Tooltip("První spawn po spuštění shrine eventu.")]
    [SerializeField] private float _firstPressureSpawnDelay = 2.0f;

    [SerializeField] private float _pressureSpawnInterval = 4.0f;

    [SerializeField] private int _enemiesPerPressurePulse = 2;

    [Tooltip("Maximum enemy spawnutých tímto shrine eventem, kteří můžou být živí najednou. Jednoduchý bezpečnostní limit.")]
    [SerializeField] private int _maxAliveShrineEnemies = 12;

    [SerializeField] private List<EnemySpawnWeight> _pressureEnemyPool = new List<EnemySpawnWeight>();

    [Header("Spawn Placement")]
    [SerializeField] private float _minEnemySpawnDistance = 8.0f;
    [SerializeField] private float _maxEnemySpawnDistance = 15.0f;

    [SerializeField] private LayerMask _groundMask = ~0;
    [SerializeField] private float _groundRayHeight = 20f;
    [SerializeField] private float _groundRayDistance = 50f;
    [SerializeField] private float _groundOffset = 0.1f;

    [Header("Enemy Scaling")]
    [SerializeField] private float _enemyPowerMultiplier = 0.85f;
    [SerializeField] private bool _useDirectorDifficultyScaling = true;

    [Header("Reward")]
    [SerializeField] private int _debugRewardXP = 40;
    [SerializeField] private int _debugRewardMana = 20;

    [Header("Visuals")]
    [SerializeField] private GameObject[] _channelingObjects;
    [SerializeField] private Light _shrineLight;
    [SerializeField] private float _dormantLightIntensity = 0.15f;
    [SerializeField] private float _activeLightIntensity = 1.2f;
    [SerializeField] private float _channelLightIntensity = 2.2f;
    [SerializeField] private float _completedLightIntensity = 0.6f;

    [Header("Visual Controller")]
    [SerializeField] private ShrineVisualController _visualController;

    [Header("Debug")]
    [SerializeField] private bool _debugLogs = true;
    [SerializeField] private bool _drawDebug = true;

    private readonly NetworkVariable<bool> _isChanneling = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<float> _channelProgress01 = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine _channelRoutine;
    private Coroutine _pressureRoutine;

    private NetworkObject _activatingPlayer;
    private readonly List<NetworkObject> _spawnedShrineEnemies = new List<NetworkObject>();

    public float ChannelProgress01 => _channelProgress01.Value;
    public bool IsChanneling => _isChanneling.Value;

    public override string InteractionPrompt
    {
        get
        {
            if (IsCompleted)
                return _completedPrompt;

            if (IsChanneling)
                return $"Mana Shrine: {Mathf.RoundToInt(ChannelProgress01 * 100f)}%";

            if (IsActive)
                return _activePrompt;

            return _dormantPrompt;
        }
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isChanneling.OnValueChanged += OnChannelingChanged;
        _channelProgress01.OnValueChanged += OnProgressChanged;

        ApplyChannelVisuals(_isChanneling.Value);
        ApplyLightForState(State, _isChanneling.Value);

        ApplyShrineVisualPhase(true);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        _isChanneling.OnValueChanged -= OnChannelingChanged;
        _channelProgress01.OnValueChanged -= OnProgressChanged;
    }

    protected override void OnInteractedServer(NetworkObject interactor)
    {
        if (!IsServer)
            return;

        if (IsChanneling)
            return;

        if (interactor == null)
            return;

        StartChannelServer(interactor);
    }

    protected override void OnActivatedServer()
    {
        if (_debugLogs)
            Debug.Log($"[ManaShrinePOI] Shrine activated: {name}", this);
    }

    protected override void OnExpiredServer()
    {
        if (IsChanneling)
            return;

        base.OnExpiredServer();
    }

    protected override void OnCompletedServer()
    {
        if (_debugLogs)
        {
            Debug.Log(
                $"[ManaShrinePOI] Completed. Reward XP: {_debugRewardXP}, Mana: {_debugRewardMana}",
                this
            );
        }

        OnManaShrineCompletedServer?.Invoke(_activatingPlayer, this);
    }

    protected override void OnVisualStateApplied(WorldPOIState state)
    {
        ApplyLightForState(state, _isChanneling.Value);
        ApplyShrineVisualPhase();
    }

    private void StartChannelServer(NetworkObject interactor)
    {
        _activatingPlayer = interactor;

        _isChanneling.Value = true;
        _channelProgress01.Value = 0f;

        if (_channelRoutine != null)
            StopCoroutine(_channelRoutine);

        if (_pressureRoutine != null)
            StopCoroutine(_pressureRoutine);

        _channelRoutine = StartCoroutine(ChannelRoutine());

        if (_spawnPressureEnemies)
            _pressureRoutine = StartCoroutine(PressureSpawnRoutine());

        PlayChannelStartedClientRpc();

        if (_debugLogs)
            Debug.Log($"[ManaShrinePOI] Channel started by {interactor.name}.", this);
    }

    private IEnumerator ChannelRoutine()
    {
        float progressSeconds = 0f;

        while (progressSeconds < _requiredChannelSeconds)
        {
            bool hasPlayerInside = HasAnyPlayerInsideRadius();

            if (!_requiresPlayerInsideRadius || hasPlayerInside)
            {
                progressSeconds += Time.deltaTime;
            }
            else if (_decayProgressWhenEmpty)
            {
                progressSeconds -= Time.deltaTime * _progressDecaySpeed;
                progressSeconds = Mathf.Max(0f, progressSeconds);
            }

            _channelProgress01.Value = Mathf.Clamp01(
                progressSeconds / Mathf.Max(0.01f, _requiredChannelSeconds)
            );

            yield return null;
        }

        FinishChannelServer();
    }

    private IEnumerator PressureSpawnRoutine()
    {
        yield return new WaitForSeconds(_firstPressureSpawnDelay);

        while (IsServer && IsChanneling && !IsCompleted)
        {
            CleanupDeadShrineEnemies();

            if (_spawnedShrineEnemies.Count < _maxAliveShrineEnemies)
            {
                int spawnCount = Mathf.Max(0, _enemiesPerPressurePulse);

                for (int i = 0; i < spawnCount; i++)
                {
                    if (_spawnedShrineEnemies.Count >= _maxAliveShrineEnemies)
                        break;

                    if (TrySpawnPressureEnemy(out NetworkObject spawnedEnemy))
                        _spawnedShrineEnemies.Add(spawnedEnemy);
                }
            }

            yield return new WaitForSeconds(Mathf.Max(0.5f, _pressureSpawnInterval));
        }
    }

    private void FinishChannelServer()
    {
        if (!IsServer)
            return;

        _isChanneling.Value = false;
        _channelProgress01.Value = 1f;

        if (_channelRoutine != null)
        {
            StopCoroutine(_channelRoutine);
            _channelRoutine = null;
        }

        if (_pressureRoutine != null)
        {
            StopCoroutine(_pressureRoutine);
            _pressureRoutine = null;
        }

        ApplyRewardServer(_activatingPlayer);

        CompleteServer();

        PlayChannelCompletedClientRpc();
    }

    private void ApplyRewardServer(NetworkObject player)
    {
        if (player == null)
            return;

        RewardChoiceManager rewardChoiceManager = player.GetComponent<RewardChoiceManager>();

        if (rewardChoiceManager != null)
        {
            rewardChoiceManager.OfferRewardChoicesServer("Mana Shrine Reward");
            return;
        }

        if (_debugLogs)
            Debug.LogWarning("[ManaShrinePOI] Player has no RewardChoiceManager.", player);
    }

    private bool HasAnyPlayerInsideRadius()
    {
        if (NetworkManager.Singleton == null)
            return false;

        float sqrRadius = _activationRadius * _activationRadius;
        IReadOnlyList<NetworkClient> clients = NetworkManager.Singleton.ConnectedClientsList;

        for (int i = 0; i < clients.Count; i++)
        {
            NetworkObject playerObj = clients[i].PlayerObject;

            if (playerObj == null)
                continue;

            float sqrDistance = (playerObj.transform.position - transform.position).sqrMagnitude;

            if (sqrDistance <= sqrRadius)
                return true;
        }

        return false;
    }

    private bool TrySpawnPressureEnemy(out NetworkObject spawnedEnemy)
    {
        spawnedEnemy = null;

        EnemyDefinition def = PickEnemyDefinition();

        if (def == null || def.Prefab == null)
            return false;

        if (!TryGetEnemySpawnPosition(out Vector3 spawnPosition))
            return false;

        Quaternion rotation = Quaternion.LookRotation(
            (transform.position - spawnPosition).normalized,
            Vector3.up
        );

        NetworkObject netObj = null;

        if (NetworkObjectPool.Instance != null)
        {
            netObj = NetworkObjectPool.Instance.GetNetworkObject(def.Prefab, spawnPosition, rotation);
        }
        else
        {
            GameObject instance = Instantiate(def.Prefab, spawnPosition, rotation);
            netObj = instance.GetComponent<NetworkObject>();
        }

        if (netObj == null)
            return false;

        if (!netObj.IsSpawned)
            netObj.Spawn(true);

        InitializeSpawnedEnemy(netObj, def, spawnPosition);

        spawnedEnemy = netObj;
        return true;
    }

    private EnemyDefinition PickEnemyDefinition()
    {
        if (_pressureEnemyPool == null || _pressureEnemyPool.Count == 0)
            return null;

        float runMinute = DirectorSpawner.Instance != null
            ? DirectorSpawner.Instance.GetRunTimeMinutes()
            : 0f;

        float totalWeight = 0f;

        for (int i = 0; i < _pressureEnemyPool.Count; i++)
        {
            EnemySpawnWeight entry = _pressureEnemyPool[i];

            if (entry == null)
                continue;

            if (!entry.IsAvailable(runMinute))
                continue;

            totalWeight += entry.Weight;
        }

        if (totalWeight <= 0f)
            return null;

        float roll = UnityEngine.Random.Range(0f, totalWeight);
        float current = 0f;

        for (int i = 0; i < _pressureEnemyPool.Count; i++)
        {
            EnemySpawnWeight entry = _pressureEnemyPool[i];

            if (entry == null)
                continue;

            if (!entry.IsAvailable(runMinute))
                continue;

            current += entry.Weight;

            if (roll <= current)
                return entry.EnemyDef;
        }

        return null;
    }

    private bool TryGetEnemySpawnPosition(out Vector3 spawnPosition)
    {
        spawnPosition = transform.position;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;

            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.right;

            float distance = UnityEngine.Random.Range(_minEnemySpawnDistance, _maxEnemySpawnDistance);

            Vector3 raw = transform.position + new Vector3(direction.x, 0f, direction.y) * distance;
            Vector3 rayOrigin = raw + Vector3.up * _groundRayHeight;

            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    _groundRayDistance,
                    _groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                spawnPosition = hit.point + Vector3.up * _groundOffset;
                return true;
            }
        }

        return false;
    }

    private void InitializeSpawnedEnemy(NetworkObject netObj, EnemyDefinition def, Vector3 spawnPosition)
    {
        if (!netObj.TryGetComponent(out EnemyBaseAI ai))
            return;

        float difficultyMultiplier = 1f;

        if (_useDirectorDifficultyScaling && DirectorSpawner.Instance != null)
            difficultyMultiplier = DirectorSpawner.Instance.CurrentDifficultyPercent.Value / 100f;

        float power = Mathf.Max(0.05f, _enemyPowerMultiplier * difficultyMultiplier);



        ai.InitializeEnemy(
            EnemyTier.Normal,
            def,
            power,
            spawnPosition
        );
    }

    private void CleanupDeadShrineEnemies()
    {
        for (int i = _spawnedShrineEnemies.Count - 1; i >= 0; i--)
        {
            NetworkObject enemy = _spawnedShrineEnemies[i];

            if (enemy == null || !enemy.IsSpawned)
            {
                _spawnedShrineEnemies.RemoveAt(i);
                continue;
            }

            if (enemy.TryGetComponent(out EnemyBaseAI ai))
            {
                if (!ai.IsAlive)
                    _spawnedShrineEnemies.RemoveAt(i);
            }
        }
    }

    private void OnChannelingChanged(bool oldValue, bool newValue)
    {
        ApplyChannelVisuals(newValue);
        ApplyLightForState(State, newValue);
        ApplyShrineVisualPhase();
    }

    private void OnProgressChanged(float oldValue, float newValue)
    {
        if (_visualController != null)
            _visualController.SetProgress(newValue);
    }

    private void ApplyChannelVisuals(bool isChanneling)
    {
        SetObjectsActive(_channelingObjects, isChanneling);
    }

    private void ApplyLightForState(WorldPOIState state, bool isChanneling)
    {
        if (_shrineLight == null)
            return;

        if (isChanneling)
        {
            _shrineLight.enabled = true;
            _shrineLight.intensity = _channelLightIntensity;
            return;
        }

        switch (state)
        {
            case WorldPOIState.Dormant:
                _shrineLight.enabled = _dormantLightIntensity > 0f;
                _shrineLight.intensity = _dormantLightIntensity;
                break;

            case WorldPOIState.Active:
                _shrineLight.enabled = true;
                _shrineLight.intensity = _activeLightIntensity;
                break;

            case WorldPOIState.Completed:
                _shrineLight.enabled = _completedLightIntensity > 0f;
                _shrineLight.intensity = _completedLightIntensity;
                break;

            default:
                _shrineLight.enabled = false;
                break;
        }
    }

    private static void SetObjectsActive(GameObject[] objects, bool active)
    {
        if (objects == null)
            return;

        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i] != null)
                objects[i].SetActive(active);
        }
    }

    [ClientRpc]
    private void PlayChannelStartedClientRpc()
    {
        if (_visualController != null)
            _visualController.PlayChannelStarted();

        if (_debugLogs)
            Debug.Log("[ManaShrinePOI] Channel started.");
    }

    [ClientRpc]
    private void PlayChannelCompletedClientRpc()
    {
        if (_visualController != null)
            _visualController.PlayCompleted();

        if (_debugLogs)
            Debug.Log("[ManaShrinePOI] Channel completed.");
    }

    private void ApplyShrineVisualPhase(bool force = false)
    {
        if (_visualController == null)
            return;

        ManaShrineVisualPhase phase = GetVisualPhase();

        _visualController.ApplyPhase(phase, force);
        _visualController.SetProgress(_channelProgress01.Value);
    }

    private ManaShrineVisualPhase GetVisualPhase()
    {
        if (IsCompleted)
            return ManaShrineVisualPhase.Completed;

        if (IsChanneling)
            return ManaShrineVisualPhase.Channeling;

        if (IsActive)
            return ManaShrineVisualPhase.Active;

        return ManaShrineVisualPhase.Dormant;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!_drawDebug)
            return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, _activationRadius);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, _minEnemySpawnDistance);

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, _maxEnemySpawnDistance);
    }
#endif
}