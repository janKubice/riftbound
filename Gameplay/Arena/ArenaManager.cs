using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections; // Pro Coroutiny
using System;

public class ArenaManager : NetworkBehaviour
{
    public static ArenaManager Instance { get; private set; }

    [Header("Konfigurace")]
    [SerializeField] private float _matchCountdownDuration = 10f;
    [SerializeField] private float _endMatchDelay = 3.0f; // Čas na oslavu vítěze
    [SerializeField] private int minimumPlayerCount = 1;

    [Header("Lokace")]
    [Tooltip("Místo, kam se hráči vrátí po smrti nebo konci zápasu (Lobby).")]
    [SerializeField] private Transform _lobbySpawnPoint;

    [Tooltip("Místa uvnitř arény.")]
    [SerializeField] private Transform[] _arenaSpawnPoints;

    // --- Network Proměnné ---
    public NetworkVariable<ArenaState> CurrentState = new NetworkVariable<ArenaState>(ArenaState.Waiting);
    public NetworkVariable<float> TimeRemaining = new NetworkVariable<float>(0f);
    public NetworkVariable<int> WaitingPlayerCount = new NetworkVariable<int>(0);

    private Dictionary<int, ulong> _registeredPlayers = new Dictionary<int, ulong>();
    private List<ulong> _activeFighters = new List<ulong>();

    public event Action<ArenaState> OnStateChanged;
    public event Action<float> OnTimerChanged;
    public event Action<int> OnPlayerCountChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicitní ArenaManager detekován.");
            gameObject.NetDestroy();
            return;
        }
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        CurrentState.OnValueChanged += HandleStateChanged;
        TimeRemaining.OnValueChanged += HandleTimerChanged;
        WaitingPlayerCount.OnValueChanged += HandlePlayerCountChanged;
    }

    public override void OnNetworkDespawn()
    {
        CurrentState.OnValueChanged -= HandleStateChanged;
        TimeRemaining.OnValueChanged -= HandleTimerChanged;
        WaitingPlayerCount.OnValueChanged -= HandlePlayerCountChanged;
    }

    private void Update()
    {
        if (!IsServer) return;

        if (CurrentState.Value == ArenaState.Countdown)
        {
            TimeRemaining.Value -= Time.deltaTime;
            if (TimeRemaining.Value <= 0f) StartMatch();
        }
    }

    // --- Server Logic: Vstupy ---
    public void OnPlayerEnteredEntrance(int entranceID, ulong clientId)
    {
        if (!IsServer || CurrentState.Value == ArenaState.Fighting) return;

        if (!_registeredPlayers.ContainsKey(entranceID) && !_registeredPlayers.ContainsValue(clientId))
        {
            _registeredPlayers.Add(entranceID, clientId);
            UpdatePlayerCount();
            CheckMatchConditions();
        }
    }

    public void OnEntranceOvercrowded(int entranceID) => RemovePlayerFromEntrance(entranceID);
    public void OnEntranceEmpty(int entranceID) => RemovePlayerFromEntrance(entranceID);

    private void RemovePlayerFromEntrance(int entranceID)
    {
        if (!IsServer) return;
        if (_registeredPlayers.ContainsKey(entranceID))
        {
            _registeredPlayers.Remove(entranceID);
            UpdatePlayerCount();
            CheckMatchConditions();
        }
    }

    private void UpdatePlayerCount() => WaitingPlayerCount.Value = _registeredPlayers.Count;

    private void CheckMatchConditions()
    {
        int count = _registeredPlayers.Count;
        if (CurrentState.Value == ArenaState.Waiting && count >= minimumPlayerCount)
        {
            TimeRemaining.Value = _matchCountdownDuration;
            CurrentState.Value = ArenaState.Countdown;
        }
        else if (CurrentState.Value == ArenaState.Countdown && count < minimumPlayerCount)
        {
            CurrentState.Value = ArenaState.Waiting;
            TimeRemaining.Value = 0f;
        }
    }

    // --- Server Logic: Boj ---

    private void StartMatch()
    {
        Debug.Log("[Arena] BOJ ZAČÍNÁ!");
        CurrentState.Value = ArenaState.Fighting;

        _activeFighters.Clear();
        int spawnIndex = 0;

        foreach (var kvp in _registeredPlayers)
        {
            ulong clientId = kvp.Value;
            _activeFighters.Add(clientId);
            Transform targetSpawn = _arenaSpawnPoints[spawnIndex % _arenaSpawnPoints.Length];
            TeleportPlayerClientRpc(clientId, targetSpawn.position, targetSpawn.rotation);
            spawnIndex++;
        }

        _registeredPlayers.Clear();
        UpdatePlayerCount();
    }

    /// <summary>
    /// Voláno z PlayerAttributes, když hráč zemře.
    /// </summary>
    public void OnPlayerDiedInArena(ulong clientId)
    {
        if (!IsServer || CurrentState.Value != ArenaState.Fighting) return;

        if (_activeFighters.Contains(clientId))
        {
            Debug.Log($"[Arena] Hráč {clientId} byl eliminován.");
            _activeFighters.Remove(clientId);

            // Vrátíme poraženého do lobby
            if (_lobbySpawnPoint != null)
            {
                TeleportPlayerClientRpc(clientId, _lobbySpawnPoint.position, _lobbySpawnPoint.rotation);
            }

            CheckWinCondition();
        }
    }

    private void CheckWinCondition()
    {
        if (_activeFighters.Count <= 1)
        {
            // Máme vítěze (nebo remízu, pokud umřeli oba naráz)
            ulong winnerId = _activeFighters.Count == 1 ? _activeFighters[0] : ulong.MaxValue;
            StartCoroutine(EndMatchRoutine(winnerId));
        }
    }

    private IEnumerator EndMatchRoutine(ulong winnerId)
    {
        CurrentState.Value = ArenaState.Ending;

        // 1. Oznámíme všem klientům výsledek, aby si zapsali staty
        AnnounceMatchResultClientRpc(winnerId);

        if (winnerId != ulong.MaxValue)
        {
            Debug.Log($"[Arena] VÍTĚZ: {winnerId}");
        }

        yield return new WaitForSeconds(_endMatchDelay);

        // Teleport vítěze
        if (winnerId != ulong.MaxValue && _lobbySpawnPoint != null)
        {
            TeleportPlayerClientRpc(winnerId, _lobbySpawnPoint.position, _lobbySpawnPoint.rotation);
        }

        _activeFighters.Clear();
        CurrentState.Value = ArenaState.Waiting;
    }

    [ClientRpc]
    private void AnnounceMatchResultClientRpc(ulong winnerClientId)
    {
        // Tento kód běží na KAŽDÉM klientovi (včetně Hosta)

        // 1. Všem započítáme účast v zápase
        // Kontrola: Započítat jen těm, co byli v aréně? 
        // Pro zjednodušení započítáme všem, co jsou připojeni, nebo si zde můžete
        // přidat logiku "If I was in active fighters".

        if (SteamStatsManager.Instance != null)
        {
            SteamStatsManager.Instance.AddPvpMatch();

            // 2. Pokud jsem já vítěz, započítám výhru
            if (NetworkManager.Singleton.LocalClientId == winnerClientId)
            {
                SteamStatsManager.Instance.AddPvpWin();
                Debug.Log("[Client] Jsem vítěz! Zapisuji staty.");
            }
        }
    }

    public bool IsPlayerInArena(ulong clientId)
    {
        return _activeFighters.Contains(clientId);
    }

    // --- Helpers ---

    [ClientRpc]
    private void TeleportPlayerClientRpc(ulong targetClientId, Vector3 position, Quaternion rotation)
    {
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjectsList.Count == 0) return;
        NetworkObject playerObj = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(targetClientId);

        if (playerObj != null)
        {
            var controller = playerObj.GetComponent<CharacterController>();
            if (controller != null) controller.enabled = false;
            playerObj.transform.position = position;
            playerObj.transform.rotation = rotation;
            if (controller != null) controller.enabled = true;
        }
    }

    // --- UI Events ---
    private void HandleStateChanged(ArenaState old, ArenaState @new) => OnStateChanged?.Invoke(@new);
    private void HandleTimerChanged(float old, float @new) => OnTimerChanged?.Invoke(@new);
    private void HandlePlayerCountChanged(int old, int @new) => OnPlayerCountChanged?.Invoke(@new);
}