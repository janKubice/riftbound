using RogueDeckCoop.Networking;
using UnityEngine;
// NEPOUŽÍVEJTE "using Steamworks;" - to způsobuje ten zmatek.

public class SteamStatsManager : PersistentSingleton<SteamStatsManager>
{
    // API Názvy (musí přesně odpovídat Steamworks portálu)
    private const string STAT_WINS = "stat_pvp_wins";
    private const string STAT_MATCHES = "stat_pvp_matches";
    
    private const string ACH_WIN_1 = "ACH_WIN_1";
    private const string ACH_WIN_10 = "ACH_WIN_10";

    private bool _statsValid = false;

    private void Start()
    {
        //
        if (SteamManager.Instance.IsSteamInitialized)
        {
            // TOTO JE KLÍČOVÉ: global::Steamworks.SteamUserStats
            // Příkaz "global::" donutí Unity ignorovat vaše lokální skripty se stejným názvem.
            bool success = true; //global::Steamworks.SteamUserStats.RequestCurrentStats();
            _statsValid = success;
            
            if (!success)
            {
                Debug.LogWarning("[SteamStats] RequestCurrentStats selhalo.");
            }
        }
    }

    public void AddPvpMatch()
    {
        if (!_statsValid) return;

        int currentMatches;
        // Použití global::
        global::Steamworks.SteamUserStats.GetStat(STAT_MATCHES, out currentMatches);
        
        currentMatches++;
        global::Steamworks.SteamUserStats.SetStat(STAT_MATCHES, currentMatches);
        
        // Uložení
        global::Steamworks.SteamUserStats.StoreStats();
        Debug.Log($"[SteamStats] Zápas započten. Celkem: {currentMatches}");
    }

    public void AddPvpWin()
    {
        if (!_statsValid) return;

        int currentWins;
        // Použití global::
        global::Steamworks.SteamUserStats.GetStat(STAT_WINS, out currentWins);
        
        currentWins++;
        global::Steamworks.SteamUserStats.SetStat(STAT_WINS, currentWins);

        CheckWinAchievements(currentWins);

        global::Steamworks.SteamUserStats.StoreStats();
        Debug.Log($"[SteamStats] Výhra započtena. Celkem: {currentWins}");
    }

    private void CheckWinAchievements(int wins)
    {
        if (wins >= 1) UnlockAchievement(ACH_WIN_1);
        if (wins >= 10) UnlockAchievement(ACH_WIN_10);
    }

    private void UnlockAchievement(string achId)
    {
        bool isAchieved;
        // Použití global::
        global::Steamworks.SteamUserStats.GetAchievement(achId, out isAchieved);

        if (!isAchieved)
        {
            global::Steamworks.SteamUserStats.SetAchievement(achId);
            Debug.Log($"[SteamStats] Achievement odemčen: {achId}");
            // StoreStats se volá v nadřazené metodě
        }
    }
}