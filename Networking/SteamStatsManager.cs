using UnityEngine;
using Unity.Netcode;
using System.Collections;
using Steamworks;
using RogueDeckCoop.Networking;

public class SteamStatsManager : NetworkBehaviour
{
    public static SteamStatsManager Instance { get; private set; }

    [Header("Settings")]
    [SerializeField] private float _autoSaveInterval = 30f;

    private bool _statsValid = false;
    private bool _isDirty = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (SteamManager.Instance != null && SteamManager.Instance.IsSteamInitialized)
        {
            // SDK >= 1.61: Data jsou automaticky synchronizována Steam klientem před spuštěním
            _statsValid = true;
            StartCoroutine(AutoSaveRoutine());
        }
        else
        {
            Debug.LogError("[SteamStatsManager] Steam API není inicializováno.");
        }
    }

    private IEnumerator AutoSaveRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(_autoSaveInterval);
            SaveStatsIfDirty();
        }
    }

    private void SaveStatsIfDirty()
    {
        if (_statsValid && _isDirty)
        {
            SteamUserStats.StoreStats();
            _isDirty = false;
        }
    }

    public void IncrementStat(string statApiName, int amount = 1)
    {
        if (!_statsValid) return;

        int currentValue;
        bool success = SteamUserStats.GetStat(statApiName, out currentValue);

        if (success)
        {
            currentValue += amount;
            SteamUserStats.SetStat(statApiName, currentValue);
            _isDirty = true;
        }
    }

    [ClientRpc]
    public void IncrementStatClientRpc(string statApiName, int amount, ClientRpcParams rpcParams = default)
    {
        IncrementStat(statApiName, amount);
    }

    public void UnlockAchievement(string achievementApiName)
    {
        if (!_statsValid) return;

        bool isAchieved;
        SteamUserStats.GetAchievement(achievementApiName, out isAchieved);

        if (!isAchieved)
        {
            SteamUserStats.SetAchievement(achievementApiName);
            _isDirty = true;
        }
    }

    public void ResetAllStats(bool achievementsToo)
    {
        if (!_statsValid) return;
        SteamUserStats.ResetAllStats(achievementsToo);
        SteamUserStats.StoreStats(); 
        _isDirty = false;
    }

    public void IncrementStatForClient(ulong clientId, string statApiName, int amount = 1)
    {
        if (!IsServer) return;

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        IncrementStatClientRpc(statApiName, amount, rpcParams);
    }

    public void UnlockAchievementForClient(ulong clientId, string achievementApiName)
    {
        if (!IsServer) return;

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
        };

        UnlockAchievementClientRpc(achievementApiName, rpcParams);
    }

    [ClientRpc]
    public void UnlockAchievementClientRpc(string achievementApiName, ClientRpcParams rpcParams = default)
    {
        UnlockAchievement(achievementApiName);
    }

    private void OnDestroy()
    {
        // Pojištění uložení dat při vypnutí aplikace
        SaveStatsIfDirty();
    }
}