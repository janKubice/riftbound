using UnityEngine;
using TMPro;
using UnityEngine.UI;
using RogueDeckCoop.Networking;
using Steamworks;

public class LobbySlotUI : MonoBehaviour
{
    [SerializeField] private GameObject _occupiedGroup;
    [SerializeField] private GameObject _emptyGroup;

    [Header("Occupied UI")]
    [SerializeField] private TextMeshProUGUI _playerName;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private Image _readyIndicator;

    [Header("Empty UI")]
    [SerializeField] private Button _inviteButton;

    private void Start()
    {
        _inviteButton.onClick.AddListener(OnInviteClicked);
    }

    public void SetOccupied(LobbyPlayerData data)
    {
        // "Násilně" vypnout Empty, zapnout Occupied
        _emptyGroup.SetActive(false);
        _occupiedGroup.SetActive(true);

        _playerName.text = data.PlayerName.ToString();

        // Status text s barvami
        if (data.IsReady)
            _statusText.text = "<color=green>READY</color>";
        else
            _statusText.text = "<color=red>WAITING</color>";

        _readyIndicator.color = data.IsReady ? Color.green : Color.red;
    }

    public void SetEmpty()
    {
        _occupiedGroup.SetActive(false);
        _emptyGroup.SetActive(true);

        // Invite tlačítko funguje jen, pokud jsme připojeni ke Steamu
        _inviteButton.interactable = SteamManager.Instance.IsSteamInitialized;
    }

    private void OnInviteClicked()
    {
        if (SteamManager.Instance.IsSteamInitialized)
        {
            // Otevře Steam Overlay s dialogem pro pozvání přátel do aktuální lobby
            SteamFriends.ActivateGameOverlayInviteDialog(SteamManager.Instance.CurrentLobbyId);
        }
    }
}