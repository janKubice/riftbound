using UnityEngine;
using RogueDeckCoop.Networking;
using Unity.Netcode;

// Děláme z toho NetworkBehaviour, aby server mohl posílat příkazy klientům (ClientRpc)
public class SteamStatsManager : NetworkBehaviour
{
    public static SteamStatsManager Instance { get; private set; }

    private bool _statsValid = false;

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

    // ------------------------------------------------------------------------
    // HLAVNÍ METODA (Lokální) - Voláš, když se něco stane u tebe (otevření truhly, smrt)
    // ------------------------------------------------------------------------
    public void IncrementStat(string statApiName, int amount = 1)
    {
        if (!_statsValid) return;

        // 1. Získat aktuální hodnotu
        int currentValue;
        bool success = global::Steamworks.SteamUserStats.GetStat(statApiName, out currentValue);

        if (success)
        {
            // 2. Zvýšit
            currentValue += amount;

            // 3. Nastavit novou hodnotu
            global::Steamworks.SteamUserStats.SetStat(statApiName, currentValue);

            // 4. Uložit (Odeslat na Steam)
            // Tip: Pokud bys posílal staty extrémně často (např. damage každou sekundu),
            // je lepší StoreStats volat jen občas. Pro RPG/Survival je to ale OK volat hned.
            global::Steamworks.SteamUserStats.StoreStats();
            
            Debug.Log($"[SteamStats] Stat '{statApiName}' zvýšen na {currentValue}");
        }
        else
        {
            Debug.LogError($"[SteamStats] Nepodařilo se najít stat s názvem: {statApiName}. Zkontroluj Steamworks portál.");
        }
    }

    // ------------------------------------------------------------------------
    // SÍŤOVÁ METODA (Server -> Klient)
    // Server může říct konkrétnímu hráči: "Započítej si stat"
    // ------------------------------------------------------------------------
    [ClientRpc]
    public void IncrementStatClientRpc(string statApiName, int amount, ClientRpcParams rpcParams = default)
    {
        // Toto se provede na klientovi, kterému to server poslal
        if (IsOwner) // Pojistka: Staty si zapisuje jen ten, komu patří tento objekt (lokální hráč)
        {
            IncrementStat(statApiName, amount);
        }
    }

    // ------------------------------------------------------------------------
    // POMOCNÁ METODA PRO PŘÍMÉ ODEMČENÍ (Pro achievementy bez statů)
    // Např. "Najdi tajnou místnost" - tam se nic nepočítá, prostě ji najdeš.
    // ------------------------------------------------------------------------
    public void UnlockAchievement(string achievementApiName)
    {
        if (!_statsValid) return;

        bool isAchieved;
        global::Steamworks.SteamUserStats.GetAchievement(achievementApiName, out isAchieved);

        if (!isAchieved)
        {
            global::Steamworks.SteamUserStats.SetAchievement(achievementApiName);
            global::Steamworks.SteamUserStats.StoreStats();
            Debug.Log($"[SteamStats] Achievement odemčen: {achievementApiName}");
        }
    }
    
    // Pro debugování - resetuje vše (vypni v ostré verzi)
    public void ResetAllStats(bool achievementsToo)
    {
        global::Steamworks.SteamUserStats.ResetAllStats(achievementsToo);
        global::Steamworks.SteamUserStats.StoreStats();
        Debug.Log("[SteamStats] Všechny statistiky resetovány.");
    }
}