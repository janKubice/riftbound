using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Riftbound.Networking.Leaderboards;
using System.Threading.Tasks;
using TMPro;

namespace Riftbound.UI
{
    public class LeaderboardUIController : MonoBehaviour
    {
        [Header("UI References")]
        [SerializeField] private GameObject _panel; // Hlavní vizuál okna tabulky
        [SerializeField] private Transform _top3Container;
        [SerializeField] private Transform _surroundingContainer;
        [SerializeField] private LeaderboardRowUI _rowPrefab;
        [SerializeField] private TextMeshProUGUI _loadingText; // Volitelný text pro stav (např. "Načítání...")
        [SerializeField] private GameObject _loadingOverlay; // Sem přetáhneš ten nový panel

        [Header("Buttons")]
        [SerializeField] private Button _killsButton;
        [SerializeField] private Button _timeButton;

        private List<GameObject> _spawnedRows = new List<GameObject>();
        private LeaderboardType _currentType = LeaderboardType.Kills;
        private bool _isLeaderboardOpen = false;

        private void Awake()
        {
            if (_killsButton != null) _killsButton.onClick.AddListener(() => SwitchCategory(LeaderboardType.Kills));
            if (_timeButton != null) _timeButton.onClick.AddListener(() => SwitchCategory(LeaderboardType.TimeSurvived));
        }

        // Metoda, kterou zavolá MainMenuUI nebo DeathScreen
        public void OpenLeaderboard()
        {
            if (_panel != null) _panel.SetActive(true);
            _isLeaderboardOpen = true;
            
            Debug.Log("[LeaderboardUI] Otevírám žebříček a spouštím stahování dat...");
            SwitchCategory(LeaderboardType.Kills); // Výchozí zobrazení
        }

        public void CloseLeaderboard()
        {
            if (_panel != null) _panel.SetActive(false);
            _isLeaderboardOpen = false;
        }

        private async void SwitchCategory(LeaderboardType type)
        {
            try
            {
                _currentType = type;
                ClearRows();

                if (_loadingOverlay != null) _loadingOverlay.SetActive(true);

                if (_loadingText != null) _loadingText.text = "Loading Steam data...";

                // --- 1. BEZPEČNOSTNÍ PAUZA ---
                // Pokud se menu zapne hned při startu hry, dáme Steamworks 250 milisekund 
                // na probrání a synchronizaci s API, než pošleme dotaz.
                await Task.Delay(250);

                if (SteamLeaderboardManager.Instance == null)
                {
                    Debug.LogError("[LeaderboardUI] CHYBA: SteamLeaderboardManager.Instance neexistuje ve scéně!");
                    if (_loadingText != null) _loadingText.text = "Error: Leaderboard service unavailable.";
                    return;
                }

                Debug.Log($"[LeaderboardUI] Posílám požadavek na Steam pro: {type}");
                
                // --- 2. STAŽENÍ DAT ---
                var top3 = await SteamLeaderboardManager.Instance.GetTopEntriesAsync(_currentType, 3);
                var surrounding = await SteamLeaderboardManager.Instance.GetSurroundingEntriesAsync(_currentType, 2);

                // Pokud hráč mezitím z menu odešel, nebudeme UI překreslovat
                if (!_isLeaderboardOpen) return;

                // --- 3. KONTROLA PRÁZDNÝCH DAT ---
                if ((top3 == null || top3.Count == 0) && (surrounding == null || surrounding.Count == 0))
                {
                    Debug.LogWarning("[LeaderboardUI] Steam vrátil prázdné listy. Buď žebříčky na Steamu nemají žádná data (jsi tam sám), nebo jsou špatně pojmenované.");
                    if (_loadingText != null) _loadingText.text = "There are no entries in the leaderboard yet.";
                    return;
                }

                if (_loadingText != null) _loadingText.text = "";

                
                // --- 4. NAPLNĚNÍ UI ---
                PopulateUI(top3, surrounding);
                if (_loadingOverlay != null) _loadingOverlay.SetActive(false);
            }
            catch (System.Exception ex)
            {
                // Pokud cokoliv spadne, toto to zachytí a vypíše červeně
                Debug.LogError($"[LeaderboardUI] FATÁLNÍ CHYBA v SwitchCategory: {ex.Message}\n{ex.StackTrace}");
                if (_loadingText != null) _loadingText.text = "Error: Failed to load leaderboard data.";
            }
        }

        private void PopulateUI(List<LeaderboardEntry> top3, List<LeaderboardEntry> surrounding)
        {
            ClearRows();
            string suffix = _currentType == LeaderboardType.TimeSurvived ? "s" : "";

            if (_rowPrefab == null)
            {
                Debug.LogError("[LeaderboardUI] V Inspectoru ti chybí přiřazený '_rowPrefab' (řádek tabulky)!");
                return;
            }

            // Vykreslení Top 3
            if (top3 != null)
            {
                foreach (var entry in top3)
                {
                    var row = Instantiate(_rowPrefab, _top3Container);
                    row.Setup(entry.Rank, entry.SteamId, entry.Name, entry.Score, suffix);
                    _spawnedRows.Add(row.gameObject);
                }
            }

            // Vykreslení okolí hráče
            if (surrounding != null)
            {
                foreach (var entry in surrounding)
                {
                    var row = Instantiate(_rowPrefab, _surroundingContainer);
                    row.Setup(entry.Rank, entry.SteamId, entry.Name, entry.Score, suffix);
                    _spawnedRows.Add(row.gameObject);
                }
            }
            
            Debug.Log($"[LeaderboardUI] Tabulka úspěšně vykreslena. Vykresleno řádků: {_spawnedRows.Count}");
        }

        private void ClearRows()
        {
            foreach (var row in _spawnedRows)
            {
                if (row != null) Destroy(row);
            }
            _spawnedRows.Clear();
        }
    }
}