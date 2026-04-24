using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Unity.Netcode;
using RogueDeckCoop.Networking;

public class EndScreenUI : MonoBehaviour
{
    public static EndScreenUI Instance { get; private set; }

    [Header("UI Reference")]
    [SerializeField] private GameObject _panel;
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _messageText;
    [SerializeField] private Button _mainMenuButton;
    [SerializeField] private Button _restartButton; // Viditelné ideálně jen pro Hosta

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else { Destroy(gameObject); return; }

        _panel.SetActive(false);

        _mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        _restartButton.onClick.AddListener(OnRestartClicked);
    }

    /// <summary>
    /// Zobrazí End Screen. Voláno lokálně u klienta, který zemřel nebo vyhrál.
    /// </summary>
    public void Show(string title, string message)
    {
        _titleText.text = title;
        _messageText.text = message;
        _panel.SetActive(true);

        // Odemkneme kurzor, aby hráč mohl kliknout na tlačítka
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Tlačítko Restart dává v MP smysl typicky jen pro Hosta (aby restartoval mapu pro všechny)
        // Pro klienty ho můžeme skrýt, nebo ho nechat fungovat jako "Leave & Rejoin"
        _restartButton.gameObject.SetActive(NetworkManager.Singleton.IsHost);
    }

    private void OnMainMenuClicked()
    {
        DisconnectAndCleanup();
        AppManager.Instance.GoToMainMenu();
    }

    private void OnRestartClicked()
    {
        if (NetworkManager.Singleton.IsHost)
        {
            // Host přenačte síťovou scénu pro všechny
            NetworkManager.Singleton.SceneManager.LoadScene("GameScene", UnityEngine.SceneManagement.LoadSceneMode.Single);
        }
    }

    private void DisconnectAndCleanup()
    {
        Time.timeScale = 1f;

        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        if (SteamManager.Instance != null)
        {
            SteamManager.Instance.LeaveLobby();
        }
    }
}