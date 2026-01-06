using UnityEngine;
using UnityEngine.UI;
using RogueDeckCoop.Networking;
using Unity.Netcode;

public class PauseMenuUI : MonoBehaviour
{
    [SerializeField] private Button _returnToMenuButton;

    // Panel, který tento skript ovládá (sám sebe)
    private GameObject _pausePanel;

    private bool _isPaused = false;

    private void Awake()
    {
        _pausePanel = this.gameObject;
        _returnToMenuButton.onClick.AddListener(OnReturnToMenuClicked);

        // Začít skrytý
        _pausePanel.SetActive(false);
    }

    private void Update()
    {
        // Otevření menu
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            TogglePauseMenu();
        }
    }

    public void TogglePauseMenu()
    {
        _isPaused = !_isPaused;
        _pausePanel.SetActive(_isPaused);

        // Pozastavení hry (Funguje POUZE v single-playeru!)
        // V Fázi 3 (síť) se toto musí řešit zprávou Hostiteli.
        Time.timeScale = _isPaused ? 0f : 1f;
    }

    // V PauseMenuUI.cs -> OnReturnToMenuClicked()
    private void OnReturnToMenuClicked()
    {
        Time.timeScale = 1f;

        // Vypneme NGO session
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }

        // Odpojíme se z lobby
        if (SteamManager.Instance != null)
        {
            SteamManager.Instance.LeaveLobby();
        }

        AppManager.Instance.GoToMainMenu();
    }
}