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
    [SerializeField] private Button _button; // Cache reference v Inspectoru

    [Header("Visual Config")]
    [SerializeField] private Color _normalColor = new Color(0.1f, 0.1f, 0.1f, 0.8f);
    [SerializeField] private Color _securedColor = new Color(0.3f, 0.2f, 0.0f, 0.8f); // Gold/Orange tint
    [SerializeField] private Color _fullColor = new Color(0.3f, 0.0f, 0.0f, 0.8f);    // Red tint
    
    [Space]
    [SerializeField] private Color _textNormalColor = Color.white;
    [SerializeField] private Color _textDimmedColor = Color.gray;

    private CSteamID _lobbyId;
    private Action<CSteamID, bool> _onJoinClicked;
    private bool _isSecured;

    // Cache pro Button, pokud není přiřazen v Inspectoru
    private void Awake()
    {
        if (_button == null) _button = GetComponent<Button>();
    }

    public void Setup(CSteamID lobbyId, string lobbyName, int currentPlayers, int maxPlayers, bool isSecured, Action<CSteamID, bool> onJoinClicked)
    {
        _lobbyId = lobbyId;
        _onJoinClicked = onJoinClicked;
        _isSecured = isSecured;

        // 1. Data (Čistý text bez formátování)
        _lobbyNameText.text = string.IsNullOrEmpty(lobbyName) ? "Unknown Lobby" : lobbyName;
        _playerCountText.text = $"{currentPlayers} / {maxPlayers}";

        // 2. Logika stavu
        bool isFull = currentPlayers >= maxPlayers;

        // 3. Aplikace vizuálu
        UpdateVisuals(isFull, isSecured);
    }

    private void UpdateVisuals(bool isFull, bool isSecured)
    {
        // Reset interaktivity
        if (_button != null) _button.interactable = !isFull;

        // Ikona zámku
        if (_lockIcon != null)
        {
            _lockIcon.enabled = isSecured;
            // Volitelně: Můžeš měnit i sprite zámku (otevřený/zavřený)
        }

        // Barvy pozadí a textu podle priority: Full > Secured > Normal
        if (isFull)
        {
            SetColors(_fullColor, _textDimmedColor);
            _playerCountText.text = "FULL"; // UX vylepšení: Místo "4/4" napsat "FULL"
        }
        else if (isSecured)
        {
            SetColors(_securedColor, _textNormalColor);
        }
        else
        {
            SetColors(_normalColor, _textNormalColor);
        }
    }

    private void SetColors(Color bg, Color text)
    {
        if (_backgroundImage != null) _backgroundImage.color = bg;
        if (_lobbyNameText != null) _lobbyNameText.color = text;
        if (_playerCountText != null) _playerCountText.color = text;
        
        // Zámek má obvykle specifickou barvu, nebo dědí barvu textu
        if (_lockIcon != null) _lockIcon.color = text; 
    }

    // Voláno Unity Eventem na Buttonu
    public void OnClick()
    {
        _onJoinClicked?.Invoke(_lobbyId, _isSecured);
    }
}