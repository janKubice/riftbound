using UnityEngine;
using TMPro;

public class ResourceUI : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private TextMeshProUGUI _xpText;
    [SerializeField] private TextMeshProUGUI _goldText;

    private PlayerProgression _cachedProgression;

    private int _lastXp = -1;
    private int _lastGold = -1;
    private int _lastEssence = -1;

    private void OnEnable()
    {
        // Pokud instance už existuje, inicializujeme ihned
        if (PlayerAttributes.LocalInstance != null)
        {
            Initialize(PlayerAttributes.LocalInstance);
        }
        else
        {
            // Fallback: Pokud hráč ještě není, musíme se přihlásit k eventu jeho vytvoření
            // Předpokládám existenci statického eventu v PlayerAttributes, což je best practice
            // Pokud ho nemáte, použijte Update check (viz níže)
        }
    }

    private void Update()
    {
        // Pokud nemáme referenci, zkusíme ji získat (náhrada za korutinu bez overheadu IEnumeratoru)
        if (_cachedProgression == null && PlayerAttributes.LocalInstance != null)
        {
            Initialize(PlayerAttributes.LocalInstance);
        }
    }

    private void Initialize(PlayerAttributes player)
    {
        _cachedProgression = player.GetComponent<PlayerProgression>();

        if (_cachedProgression != null)
        {
            // Prevence duplicitního odběru
            _cachedProgression.OnResourcesChanged -= UpdateUI;
            _cachedProgression.OnResourcesChanged += UpdateUI;
            ForceUpdateUI();
        }
    }

    private void OnDisable()
    {
        if (_cachedProgression != null)
        {
            _cachedProgression.OnResourcesChanged -= UpdateUI;
        }
    }

    public void UpdateUI()
    {
        if (_cachedProgression == null) return;

        // XP
        int currentXp = _cachedProgression.CurrentXP.Value;
        if (_lastXp != currentXp)
        {
            _lastXp = currentXp;
            _xpText.SetText($"{_lastXp:N0} <color=#AAAAAA>XP</color>");
        }

        // Gold
        int currentGold = _cachedProgression.Gold.Value;
        if (_lastGold != currentGold)
        {
            _lastGold = currentGold;
            _goldText.SetText($"{_lastGold:N0} <color=#FFD700>G</color>");
        }


    }

    [ContextMenu("Force Update")]
    public void ForceUpdateUI()
    {
        _lastXp = -1;
        _lastGold = -1;
        _lastEssence = -1;
        UpdateUI();
    }
}