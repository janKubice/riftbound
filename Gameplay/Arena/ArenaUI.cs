using UnityEngine;
using TMPro;

public class ArenaUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _timerText;

    private ArenaState _currentState = ArenaState.Waiting;
    private int _currentPlayerCount = 0;

    private void Start()
    {
        // Počkáme na inicializaci Managera
        if (ArenaManager.Instance != null)
        {
            SubscribeEvents();
            // Načtení výchozích hodnot (pokud jsme se připojili později)
            _currentState = ArenaManager.Instance.CurrentState.Value;
            _currentPlayerCount = ArenaManager.Instance.WaitingPlayerCount.Value;
            RefreshUI();
        }
        else
        {
            // Fallback: zkusíme najít event později nebo v OnEnable
            _statusText.text = "";
            _timerText.text = "";
        }
    }

    private void OnEnable()
    {
        if (ArenaManager.Instance != null) SubscribeEvents();
    }

    private void OnDisable()
    {
        if (ArenaManager.Instance != null) UnsubscribeEvents();
    }

    private void SubscribeEvents()
    {
        ArenaManager.Instance.OnStateChanged += HandleStateChanged;
        ArenaManager.Instance.OnTimerChanged += HandleTimerChanged;
        ArenaManager.Instance.OnPlayerCountChanged += HandlePlayerCountChanged;
    }

    private void UnsubscribeEvents()
    {
        ArenaManager.Instance.OnStateChanged -= HandleStateChanged;
        ArenaManager.Instance.OnTimerChanged -= HandleTimerChanged;
        ArenaManager.Instance.OnPlayerCountChanged -= HandlePlayerCountChanged;
    }

    // --- Event Handlers ---

    private void HandleStateChanged(ArenaState newState)
    {
        _currentState = newState;
        RefreshUI();
    }

    private void HandlePlayerCountChanged(int newCount)
    {
        _currentPlayerCount = newCount;
        RefreshUI();
    }

    private void HandleTimerChanged(float time)
    {
        if (time > 0) _timerText.text = time.ToString("F1");
        else _timerText.text = "";
    }

    // --- Hlavní logika zobrazení ---

    private void RefreshUI()
    {
        switch (_currentState)
        {
            case ArenaState.Waiting:
                // Zobrazíme text pouze pokud alespoň jeden hráč čeká
                if (_currentPlayerCount > 0)
                {
                    _statusText.text = $"WAITING FOR PLAYERS ({_currentPlayerCount}/4)";
                }
                else
                {
                    _statusText.text = ""; // Prázdná aréna = prázdné UI
                }
                _timerText.text = "";
                break;

            case ArenaState.Countdown:
                _statusText.text = "MATCH STARTING IN:";
                break;

            case ArenaState.Fighting:
                _statusText.text = "FIGHT!";
                _timerText.text = "";
                break;
                
            case ArenaState.Ending:
                 _statusText.text = "MATCH ENDED";
                 break;
        }
    }
}