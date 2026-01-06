using UnityEngine;
using TMPro;
using RogueDeckCoop.Networking;
using Steamworks;
using System.Collections; // Nutné pro Coroutines
using System.Collections.Generic;

public class MainMenuUI : MonoBehaviour
{
    [Header("Panely")]
    [SerializeField] private GameObject _mainPanel;
    [SerializeField] private GameObject _hostPanel;
    [SerializeField] private GameObject _browserPanel;
    [SerializeField] private GameObject _passwordPanel;

    [Header("Host Game UI")]
    [SerializeField] private TMP_InputField _lobbyNameInput;
    [SerializeField] private TMP_InputField _hostPasswordInput;
    [SerializeField] private TMP_Dropdown _privacyDropdown;
    [SerializeField] private TextMeshProUGUI _hostErrorText;

    [Header("Browser UI")]
    [SerializeField] private Transform _lobbyListContent;
    [SerializeField] private LobbyListItem _lobbyListItemPrefab;

    [Header("Password Prompt UI")]
    [SerializeField] private TMP_InputField _clientPasswordInput;
    [SerializeField] private TextMeshProUGUI _passwordErrorText;

    [Header("General")]
    [SerializeField] private TextMeshProUGUI _errorText;
    [SerializeField] private float _errorDisplayTime = 5f; // Čas zobrazení chyby

    private CSteamID _pendingLobbyId;
    private readonly List<LobbyListItem> _spawnedLobbyItems = new List<LobbyListItem>();

    // Reference na běžící coroutiny pro resetování časovače
    private Coroutine _mainErrorRoutine;
    private Coroutine _hostErrorRoutine;
    private Coroutine _passErrorRoutine;

    private readonly List<LobbyPrivacy> _privacyOptions = new List<LobbyPrivacy>
    {
        LobbyPrivacy.Public,
        LobbyPrivacy.FriendsOnly,
        LobbyPrivacy.Private
    };

    private const string KEY_LOBBY_NAME = "LobbyName";
    private const string KEY_IS_SECURED = "IsSecured";

    private void Start()
    {
        // Kritické: Odemknout myš při návratu ze hry
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        ShowPanel(_mainPanel);
        
        SteamManager.OnLobbyListUpdated += HandleLobbyListUpdated;
        SteamManager.OnConnectionFailed += HandleConnectionFailed;

        // Reset textů
        ClearAllErrors();

        // UX: Vymazat chybu, když uživatel začne opravovat vstup
        _lobbyNameInput.onValueChanged.AddListener(_ => StopAndClearError(_hostErrorText, ref _hostErrorRoutine));
        _clientPasswordInput.onValueChanged.AddListener(_ => StopAndClearError(_passwordErrorText, ref _passErrorRoutine));
    }

    private void OnDestroy()
    {
        SteamManager.OnLobbyListUpdated -= HandleLobbyListUpdated;
        SteamManager.OnConnectionFailed -= HandleConnectionFailed;
    }

    // --- LOGIKA CHYB (AUTO-HIDE) ---

    private void ShowTemporaryError(TextMeshProUGUI textComp, string message, ref Coroutine routine)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ErrorRoutine(textComp, message));
    }

    private IEnumerator ErrorRoutine(TextMeshProUGUI textComp, string message)
    {
        textComp.text = message;
        textComp.gameObject.SetActive(true);
        
        yield return new WaitForSeconds(_errorDisplayTime);
        
        textComp.text = "";
        textComp.gameObject.SetActive(false);
    }

    private void StopAndClearError(TextMeshProUGUI textComp, ref Coroutine routine)
    {
        if (routine != null) StopCoroutine(routine);
        textComp.text = "";
        textComp.gameObject.SetActive(false);
    }

    private void ClearAllErrors()
    {
        StopAndClearError(_errorText, ref _mainErrorRoutine);
        StopAndClearError(_hostErrorText, ref _hostErrorRoutine);
        StopAndClearError(_passwordErrorText, ref _passErrorRoutine);
    }

    // --- NAVIGACE ---

    public void GoToHostPanel()
    {
        ClearAllErrors();
        ShowPanel(_hostPanel);
    }

    public void GoToBrowser()
    {
        ClearAllErrors();
        ShowPanel(_browserPanel);
        SteamManager.Instance.RequestLobbyList();
    }

    public void GoBack() => ShowPanel(_mainPanel);
    public void OnExitGameClicked() => AppManager.Instance.ExitGame();

    private void ShowPanel(GameObject panelToShow)
    {
        _mainPanel.SetActive(panelToShow == _mainPanel);
        _hostPanel.SetActive(panelToShow == _hostPanel);
        _browserPanel.SetActive(panelToShow == _browserPanel);
        _passwordPanel.SetActive(false);
    }

    // --- ZAKLÁDÁNÍ LOBBY ---

    public void OnCreateLobbyClicked()
    {
        string lobbyName = _lobbyNameInput.text.Trim();

        if (string.IsNullOrEmpty(lobbyName))
        {
            ShowTemporaryError(_hostErrorText, "Lobby name cannot be empty.", ref _hostErrorRoutine);
            return;
        }

        string password = _hostPasswordInput.text;

        if (_privacyDropdown.value >= _privacyOptions.Count)
        {
            Debug.LogError("Dropdown index out of range.");
            return;
        }
        LobbyPrivacy privacy = _privacyOptions[_privacyDropdown.value];

        SteamManager.Instance.HostLobby(lobbyName, privacy, password);
    }

    // --- PROHLÍŽEČ LOBBY ---

    public void OnRefreshClicked() => SteamManager.Instance.RequestLobbyList();

    private void HandleLobbyListUpdated(List<CSteamID> lobbyIds)
    {
        foreach (var item in _spawnedLobbyItems) item.gameObject.SetActive(false);

        for (int i = 0; i < lobbyIds.Count; i++)
        {
            CSteamID lobbyId = lobbyIds[i];

            string name = SteamMatchmaking.GetLobbyData(lobbyId, KEY_LOBBY_NAME);
            if (string.IsNullOrEmpty(name)) name = "Unknown Lobby";

            int numPlayers = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            int maxPlayers = SteamMatchmaking.GetLobbyMemberLimit(lobbyId);

            string isSecuredStr = SteamMatchmaking.GetLobbyData(lobbyId, KEY_IS_SECURED);
            bool isSecured = isSecuredStr == "1";

            LobbyListItem item;
            if (i < _spawnedLobbyItems.Count)
            {
                item = _spawnedLobbyItems[i];
                item.gameObject.SetActive(true);
            }
            else
            {
                item = Instantiate(_lobbyListItemPrefab, _lobbyListContent);
                _spawnedLobbyItems.Add(item);
            }

            item.Setup(lobbyId, name, numPlayers, maxPlayers, isSecured, OnLobbyItemClicked);
        }
    }

    private void OnLobbyItemClicked(CSteamID lobbyId, bool isSecured)
    {
        if (isSecured)
        {
            _pendingLobbyId = lobbyId;
            _clientPasswordInput.text = "";
            StopAndClearError(_passwordErrorText, ref _passErrorRoutine); // Reset chyby při novém otevření
            _passwordPanel.SetActive(true);
        }
        else
        {
            SteamManager.Instance.JoinLobby(lobbyId, "");
        }
    }

    // --- PŘIPOJOVÁNÍ S HESLEM ---

    public void OnConfirmPasswordClicked()
    {
        if (_pendingLobbyId.m_SteamID != 0)
        {
            SteamManager.Instance.JoinLobby(_pendingLobbyId, _clientPasswordInput.text);
        }
    }

    public void OnCancelPasswordClicked()
    {
        _passwordPanel.SetActive(false);
        _pendingLobbyId = CSteamID.Nil;
        StopAndClearError(_passwordErrorText, ref _passErrorRoutine);
    }

    private void HandleConnectionFailed(string reason)
    {
        // 1. Zobrazit globální chybu
        if (_errorText != null)
        {
            ShowTemporaryError(_errorText, reason, ref _mainErrorRoutine);
        }

        // 2. Specifická chyba pro heslo
        if (_passwordPanel.activeSelf)
        {
            ShowTemporaryError(_passwordErrorText, reason, ref _passErrorRoutine);
        }
    }
}