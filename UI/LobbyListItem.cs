using UnityEngine;
using TMPro;
using UnityEngine.UI;
using Steamworks;
using System;

public class LobbyListItem : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TextMeshProUGUI _lobbyNameText;
    [SerializeField] private TextMeshProUGUI _playerCountText;
    [SerializeField] private Image _lockIcon;
    [SerializeField] private Image _backgroundImage;

    [Header("Settings")]
    [SerializeField] private Color _fullLobbyColor = Color.red;
    [SerializeField] private Color _availableLobbyColor = Color.white;
    [SerializeField] private Color _securedLobbyColor = new Color(1f, 0.8f, 0.4f); // Oranžový nádech pro heslo

    private CSteamID _lobbyId;
    private Action<CSteamID, bool> _onJoinClicked;
    private bool _isSecured;

    public void Setup(CSteamID lobbyId, string lobbyName, int currentPlayers, int maxPlayers, bool isSecured, Action<CSteamID, bool> onJoinClicked)
    {
        _lobbyId = lobbyId;
        _onJoinClicked = onJoinClicked;
        _isSecured = isSecured;

        // Základní texty
        _lobbyNameText.text = string.IsNullOrEmpty(lobbyName) ? "Unknown Lobby" : lobbyName;
        _playerCountText.text = $"{currentPlayers} / {maxPlayers}";

        // Vizuální stavy
        bool isFull = currentPlayers >= maxPlayers;
        _playerCountText.color = isFull ? _fullLobbyColor : _availableLobbyColor;

        if (_lockIcon != null)
        {
            _lockIcon.enabled = isSecured;
            _lockIcon.color = _securedLobbyColor;
        }

        // Zvýraznění názvu, pokud je vyžadováno heslo
        if (isSecured)
        {
            _lobbyNameText.text = $"<color=#{ColorUtility.ToHtmlStringRGB(_securedLobbyColor)}>[SECURED]</color> {_lobbyNameText.text}";
        }

        // Zakázání kliknutí, pokud je plno
        if (TryGetComponent<Button>(out var btn))
        {
            btn.interactable = !isFull;
        }
    }

    public void OnClick()
    {
        _onJoinClicked?.Invoke(_lobbyId, _isSecured);
    }
}