using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("UI References")]
    [SerializeField] private PauseMenuUI _pauseMenuScript; // Odkaz na nový skript

    // --- PAUSE LOGIC ---
    public NetworkVariable<bool> IsGamePaused = new NetworkVariable<bool>(false);

    // Server sleduje, kolik hráčů má otevřené menu
    private HashSet<ulong> _playersInMenu = new HashSet<ulong>();

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
        base.OnNetworkSpawn();

        // Synchronizace TimeScale při změně proměnné
        IsGamePaused.OnValueChanged += OnPauseStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        IsGamePaused.OnValueChanged -= OnPauseStateChanged;
    }

    // --- PAUSE SYSTEM ---

    private void OnPauseStateChanged(bool previousValue, bool newValue)
    {        
        // Zde můžeš zobrazit overlay "GAME PAUSED BY ANOTHER PLAYER", pokud lokální hráč nemá menu
        Debug.Log($"[GameManager] Game Paused: {newValue}");
    }

    /// <summary>
    /// Klient nahlásí serveru, že otevřel/zavřel menu.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void SetPlayerInMenuServerRpc(bool isInMenu, ServerRpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;

        if (isInMenu)
            _playersInMenu.Add(clientId);
        else
            _playersInMenu.Remove(clientId);

        IsGamePaused.Value = _playersInMenu.Count > 0;
    }

    // --- ADMIN / KICK SYSTEM ---

    [ServerRpc(RequireOwnership = false)]
    public void KickPlayerServerRpc(ulong targetClientId)
    {
        // Pouze Host může vyhazovat (ověření na serveru)
        if (NetworkManager.Singleton.LocalClientId != NetworkManager.ServerClientId) return;

        // Nemůžeme vykopnout sami sebe
        if (targetClientId == NetworkManager.ServerClientId) return;

        Debug.Log($"[GameManager] Kicking player {targetClientId}...");

        // Nastavíme důvod odpojení (vyžaduje Unity Netcode 1.2+)
        NetworkManager.Singleton.DisconnectClient(targetClientId, "Kicked by Host");
    }

    [ServerRpc(RequireOwnership = false)]
    public void RequestEndGameServerRpc()
    {
        // Uložit hru všem hráčům
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
             if (client.PlayerObject != null && client.PlayerObject.TryGetComponent(out PlayerAttributes attrs))
             {
                 attrs.SavePlayerData(); // Volání prázdné metody
             }
        }

        // Ukončení session
        EndGameClientRpc("Host ended the game.");
        
        // Zpožděný shutdown na serveru
        StartCoroutine(ShutdownRoutine());
    }

    [ClientRpc]
    private void EndGameClientRpc(string reason)
    {
        // Klienti se odpojí a zobrazí důvod
        if (!IsServer) 
        {
            RogueDeckCoop.Networking.SteamManager.Instance.LeaveLobby();
            // Předáme důvod do MainMenu (např. přes statickou proměnnou nebo Event v AppManageru)
            // Pro teď jen log:
            Debug.Log($"Game Ended: {reason}");
            AppManager.Instance.GoToMainMenu();
        }
    }

    private IEnumerator ShutdownRoutine()
    {
        yield return new WaitForSeconds(0.5f);
        NetworkManager.Singleton.Shutdown();
        AppManager.Instance.GoToMainMenu();
    }

}