using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using Steamworks;
using RogueDeckCoop.Networking;

namespace Riftbound.Networking.Leaderboards
{
    public class SteamLeaderboardManager : PersistentSingleton<SteamLeaderboardManager>, ILeaderboardService
    {
        private const string LEADERBOARD_KILLS = "leaderboard_kills"; // Název ze Steamworks
        private const string LEADERBOARD_TIME = "leaderboard_time";   // Název ze Steamworks

        // --- CallResults a Tasks pro Steamworks.NET ---
        private CallResult<LeaderboardFindResult_t> _findLeaderboardCallResult;
        private CallResult<LeaderboardScoreUploaded_t> _uploadScoreCallResult;
        private CallResult<LeaderboardScoresDownloaded_t> _downloadScoresCallResult;

        private TaskCompletionSource<SteamLeaderboard_t> _findTcs;
        private TaskCompletionSource<bool> _uploadTcs;
        private TaskCompletionSource<List<LeaderboardEntry>> _downloadTcs;

        // Cache pro nalezené žebříčky, abychom je nemuseli hledat pokaždé
        private Dictionary<string, SteamLeaderboard_t> _leaderboardCache = new Dictionary<string, SteamLeaderboard_t>();

        protected override void Awake()
        {
            base.Awake();

            // Inicializace CallResult objektů
            _findLeaderboardCallResult = CallResult<LeaderboardFindResult_t>.Create(OnLeaderboardFindResult);
            _uploadScoreCallResult = CallResult<LeaderboardScoreUploaded_t>.Create(OnScoreUploaded);
            _downloadScoresCallResult = CallResult<LeaderboardScoresDownloaded_t>.Create(OnScoresDownloaded);
        }

        // --- 1. ZÍSKÁNÍ ŽEBŘÍČKU ---
        private async Task<SteamLeaderboard_t> GetLeaderboardHandleAsync(string leaderboardName)
        {
            if (_leaderboardCache.ContainsKey(leaderboardName))
                return _leaderboardCache[leaderboardName];

            Debug.Log($"[SteamLeaderboard] Hledám handle pro žebříček: {leaderboardName}");
            _findTcs = new TaskCompletionSource<SteamLeaderboard_t>();

            SteamAPICall_t handle = SteamUserStats.FindLeaderboard(leaderboardName);

            if (handle.m_SteamAPICall == 0)
            {
                Debug.LogError("[SteamLeaderboard] Steam vrátil neplatný handle (0) pro FindLeaderboard! Možná není Steam ještě plně připojen.");
                return new SteamLeaderboard_t();
            }

            _findLeaderboardCallResult.Set(handle);

            // Pojistka: Čekáme max 5 sekund. Pokud Steam neodpoví, nezasekneme hru.
            var delayTask = Task.Delay(5000);
            var completedTask = await Task.WhenAny(_findTcs.Task, delayTask);

            if (completedTask == delayTask)
            {
                Debug.LogError($"[SteamLeaderboard] TIMEOUT! Steam neodpověděl na hledání žebříčku '{leaderboardName}' do 5 sekund.");
                return new SteamLeaderboard_t();
            }

            SteamLeaderboard_t result = await _findTcs.Task;

            if (result.m_SteamLeaderboard != 0)
                _leaderboardCache[leaderboardName] = result;

            return result;
        }

        private void OnLeaderboardFindResult(LeaderboardFindResult_t pCallback, bool bIOFailure)
        {
            if (bIOFailure || pCallback.m_bLeaderboardFound == 0)
            {
                Debug.LogError("[SteamLeaderboard] Callback FindResult: Žebříček nenalezen nebo chyba IO!");
                _findTcs?.TrySetResult(new SteamLeaderboard_t());
            }
            else
            {
                Debug.Log($"[SteamLeaderboard] Callback FindResult: Žebříček úspěšně nalezen (Handle: {pCallback.m_hSteamLeaderboard.m_SteamLeaderboard})");
                _findTcs?.TrySetResult(pCallback.m_hSteamLeaderboard);
            }
        }

        // --- 2. NAHRÁVÁNÍ SKÓRE ---
        public async Task<bool> UploadScoreAsync(LeaderboardType type, int score)
        {
            if (!SteamManager.Instance.IsSteamInitialized) return false;

            string boardName = GetLeaderboardName(type);
            SteamLeaderboard_t lbHandle = await GetLeaderboardHandleAsync(boardName);

            if (lbHandle.m_SteamLeaderboard == 0) return false;

            _uploadTcs = new TaskCompletionSource<bool>();

            // k_ELeaderboardUploadScoreMethodKeepBest zajistí, že Steam nepřepíše tvůj rekord horším výsledkem
            SteamAPICall_t handle = SteamUserStats.UploadLeaderboardScore(
                lbHandle,
                ELeaderboardUploadScoreMethod.k_ELeaderboardUploadScoreMethodKeepBest,
                score,
                null,
                0);

            _uploadScoreCallResult.Set(handle);

            return await _uploadTcs.Task;
        }

        private void OnScoreUploaded(LeaderboardScoreUploaded_t pCallback, bool bIOFailure)
        {
            bool success = !bIOFailure && pCallback.m_bSuccess == 1;
            if (success && pCallback.m_bScoreChanged == 1)
            {
                Debug.Log($"[SteamLeaderboard] Nový osobní rekord uložen! Pozice: {pCallback.m_nGlobalRankNew}");
            }
            _uploadTcs?.TrySetResult(success);
        }

        // --- 3. STAHOVÁNÍ TOP 3 ---
        public async Task<List<LeaderboardEntry>> GetTopEntriesAsync(LeaderboardType type, int count = 3)
        {
            return await DownloadEntriesAsync(type, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobal, 1, count);
        }

        // --- 4. STAHOVÁNÍ OKOLÍ HRÁČE ---
        public async Task<List<LeaderboardEntry>> GetSurroundingEntriesAsync(LeaderboardType type, int range = 5)
        {
            return await DownloadEntriesAsync(type, ELeaderboardDataRequest.k_ELeaderboardDataRequestGlobalAroundUser, -range, range);
        }

        // --- SPOLEČNÁ METODA PRO STAHOVÁNÍ ---
        private async Task<List<LeaderboardEntry>> DownloadEntriesAsync(LeaderboardType type, ELeaderboardDataRequest requestType, int rangeStart, int rangeEnd)
        {
            if (!SteamManager.Instance.IsSteamInitialized) return new List<LeaderboardEntry>();

            string boardName = GetLeaderboardName(type);
            SteamLeaderboard_t lbHandle = await GetLeaderboardHandleAsync(boardName);

            if (lbHandle.m_SteamLeaderboard == 0)
            {
                Debug.LogWarning($"[SteamLeaderboard] Přerušuji stahování, neplatný handle pro {boardName}.");
                return new List<LeaderboardEntry>();
            }

            Debug.Log($"[SteamLeaderboard] Požaduji stažení dat pro {boardName} (Typ: {requestType})...");
            _downloadTcs = new TaskCompletionSource<List<LeaderboardEntry>>();
            SteamAPICall_t handle = SteamUserStats.DownloadLeaderboardEntries(lbHandle, requestType, rangeStart, rangeEnd);

            if (handle.m_SteamAPICall == 0)
            {
                Debug.LogError("[SteamLeaderboard] Steam vrátil neplatný handle (0) pro DownloadLeaderboardEntries!");
                return new List<LeaderboardEntry>();
            }

            _downloadScoresCallResult.Set(handle);

            // Timeout pojistka pro stahování
            var delayTask = Task.Delay(1500);
            var completedTask = await Task.WhenAny(_downloadTcs.Task, delayTask);

            if (completedTask == delayTask)
            {
                Debug.LogError("[SteamLeaderboard] TIMEOUT! Steam neodpověděl na stažení dat do 5 sekund.");
                return new List<LeaderboardEntry>();
            }

            return await _downloadTcs.Task;
        }

        private void OnScoresDownloaded(LeaderboardScoresDownloaded_t pCallback, bool bIOFailure)
        {
            List<LeaderboardEntry> results = new List<LeaderboardEntry>();

            if (!bIOFailure)
            {
                Debug.Log($"[SteamLeaderboard] Data stažena! Počet záznamů: {pCallback.m_cEntryCount}");
                for (int i = 0; i < pCallback.m_cEntryCount; i++)
                {
                    LeaderboardEntry_t entry;
                    SteamUserStats.GetDownloadedLeaderboardEntry(pCallback.m_hSteamLeaderboardEntries, i, out entry, null, 0);

                    string name = SteamFriends.GetFriendPersonaName(entry.m_steamIDUser);

                    results.Add(new LeaderboardEntry(
                        entry.m_nGlobalRank,
                        entry.m_steamIDUser.m_SteamID,
                        name,
                        entry.m_nScore
                    ));
                }
            }
            else
            {
                Debug.LogError("[SteamLeaderboard] Callback Downloaded: IO Chyba při stahování dat ze žebříčku.");
            }

            _downloadTcs?.TrySetResult(results);
        }

        private string GetLeaderboardName(LeaderboardType type)
        {
            return type switch
            {
                LeaderboardType.Kills => LEADERBOARD_KILLS,
                LeaderboardType.TimeSurvived => LEADERBOARD_TIME,
                _ => LEADERBOARD_KILLS
            };
        }
    }
}