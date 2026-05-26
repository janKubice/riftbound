using System.Collections.Generic;
using System.Threading.Tasks;

namespace Riftbound.Networking.Leaderboards
{
    public enum LeaderboardType
    {
        Kills,
        TimeSurvived // Reprezentováno v sekundách (int)
    }

    public struct LeaderboardEntry
    {
        public int Rank;
        public ulong SteamId;
        public string Name;
        public int Score;

        public LeaderboardEntry(int rank, ulong steamId, string name, int score)
        {
            Rank = rank;
            SteamId = steamId;
            Name = name;
            Score = score;
        }
    }

    public interface ILeaderboardService
    {
        Task<bool> UploadScoreAsync(LeaderboardType type, int score);
        Task<List<LeaderboardEntry>> GetTopEntriesAsync(LeaderboardType type, int count = 3);
        Task<List<LeaderboardEntry>> GetSurroundingEntriesAsync(LeaderboardType type, int range = 5);
    }
}