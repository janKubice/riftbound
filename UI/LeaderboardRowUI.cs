using UnityEngine;
using TMPro;
using RogueDeckCoop.Networking; // Pro přístup k SteamManager

namespace Riftbound.UI
{
    public class LeaderboardRowUI : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI _rankText;
        [SerializeField] private TextMeshProUGUI _nameText;
        [SerializeField] private TextMeshProUGUI _scoreText;
        
        [Header("Colors")]
        [SerializeField] private Color _normalColor = Color.white;
        [SerializeField] private Color _highlightColor = Color.yellow; // Zvýrazňovací barva

        public void Setup(int rank, ulong steamId, string playerName, int score, string suffix = "")
        {
            _rankText.text = $"#{rank}";
            _nameText.text = playerName;
            _scoreText.text = $"{score} {suffix}";

            // Zjistíme, zda toto SteamID patří lokálnímu hráči
            bool isMe = SteamManager.Instance != null && SteamManager.Instance.PlayerSteamId.m_SteamID == steamId;

            // Aplikujeme barvy
            Color targetColor = isMe ? _highlightColor : _normalColor;
            _rankText.color = targetColor;
            _nameText.color = targetColor;
            _scoreText.color = targetColor;
        }
    }
}