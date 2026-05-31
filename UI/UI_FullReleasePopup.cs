using UnityEngine;
using UnityEngine.UI;

public class UI_FullReleasePopup : MonoBehaviour
{
    [SerializeField] private GameObject popupPanel;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button wishlistButton;
    [SerializeField] private string steamStoreUrl = "https://store.steampowered.com/app/TVOJE_ID_HRY";

    private void Start()
    {
        popupPanel.SetActive(false); // Defaultně skryto
        closeButton.onClick.AddListener(ClosePopup);
        wishlistButton.onClick.AddListener(OpenSteamStore);
    }

    public void OpenPopup()
    {
        popupPanel.SetActive(true);
    }

    public void ClosePopup()
    {
        popupPanel.SetActive(false);
    }

    private void OpenSteamStore()
    {
        // Použijeme tvůj manažer přes Singleton a tvou vlastnost IsSteamInitialized
        if (RogueDeckCoop.Networking.SteamManager.Instance != null &&
            RogueDeckCoop.Networking.SteamManager.Instance.IsSteamInitialized)
        {
            Steamworks.SteamFriends.ActivateGameOverlayToWebPage(steamStoreUrl);
        }
        else
        {
            Application.OpenURL(steamStoreUrl);
        }
    }
}