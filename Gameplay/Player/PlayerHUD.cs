using UnityEngine;
using UnityEngine.UI; // Nutné pro Slider
using TMPro;
using System.Collections;
using Unity.Netcode;
using Unity.VisualScripting; // Pokud používáte TextMeshPro

public class PlayerHUD : MonoBehaviour
{
    public static PlayerHUD LocalInstance { get; private set; }

    [Header("UI Panel")]
    [SerializeField] private GameObject _playerHudPanel; // Panel by měl být na začátku skrytý

    [Header("Health")]
    [SerializeField] private Slider _healthSlider;
    [SerializeField] private TextMeshProUGUI _healthText; // Volitelné

    [Header("Stamina")]
    [SerializeField] private Slider _staminaSlider;
    [SerializeField] private TextMeshProUGUI _staminaText; // Volitelné

    [Header("Mana")]
    [SerializeField] private Slider _manaSlider;
    [SerializeField] private TextMeshProUGUI _manaText; // Volitelné

    [Header("Location Display")]
    [SerializeField] private TextMeshProUGUI _locationNameText;
    [SerializeField] private float _locationFadeTime = 1.0f;
    [SerializeField] private float _locationHoldTime = 3.0f;
    private Coroutine _locationCoroutine;

    [Header("Time")]
    [SerializeField] private TMPro.TextMeshProUGUI _clockText;
    [SerializeField] private DayNightCycle dayNightCycle;

    private void Awake()
    {
        if (LocalInstance != null && LocalInstance != this)
        {
            // Kontrola, zda je objekt pod správou sítě
            if (TryGetComponent<NetworkObject>(out var netObj) && netObj.IsSpawned)
            {
                // Na klientovi nesmíte volat Destroy. 
                // Vypněte pouze vizuální/funkční složku, nikoliv celý objekt.
                enabled = false;
                Debug.LogWarning($"[PlayerHUD] Duplicitní HUD na síťovém objektu {gameObject.name}. Skript deaktivován.");
                return;
            }

            // Pokud to není spawnovaný NetworkObject, lze smazat
            gameObject.NetDestroy();
        }
        else
        {
            LocalInstance = this;
        }
    }

    private void OnEnable()
    {
        // Přihlásíme se ke statickým událostem
        PlayerAttributes.OnLocalPlayerHealthChanged += UpdateHealthUI;
        PlayerAttributes.OnLocalPlayerStaminaChanged += UpdateStaminaUI;
        PlayerAttributes.OnLocalPlayerManaChanged += UpdateManaUI;
    }

    private void OnDisable()
    {
        // Odhlásíme se
        PlayerAttributes.OnLocalPlayerHealthChanged -= UpdateHealthUI;
        PlayerAttributes.OnLocalPlayerStaminaChanged -= UpdateStaminaUI;
        PlayerAttributes.OnLocalPlayerManaChanged -= UpdateManaUI;
    }

    private void Start()
    {
        _playerHudPanel.SetActive(true);
        if (_locationNameText != null)
        {
            _locationNameText.gameObject.SetActive(false);
            var c = _locationNameText.color;
            _locationNameText.color = new Color(c.r, c.g, c.b, 0);
        }

        // Zkusíme najít hráče hned
        if (PlayerAttributes.LocalInstance != null)
        {
            InitializeHUD();
        }
        else
        {
            // ... a spustíme "hlídacího psa", který čeká na spawn
            StartCoroutine(WaitForLocalPlayer());
        }
    }

    void Update()
    {
        if (_clockText != null) // Udělej si singleton z DayNightCycle nebo najdi referenci
        {
            _clockText.text = dayNightCycle.GetFormattedTime();
        }
    }

    private IEnumerator WaitForLocalPlayer()
    {
        // Čekáme, dokud LocalInstance nebude null
        // (to se stane ve chvíli, kdy se hráč spawne a nastaví si svůj Singleton)
        yield return new WaitUntil(() => PlayerAttributes.LocalInstance != null);

        // Hráč je na světě! Inicializujeme HUD.
        InitializeHUD();
    }
    private void InitializeHUD()
    {
        if (_locationNameText != null)
        {
            _locationNameText.gameObject.SetActive(false);
            _locationNameText.color = new Color(_locationNameText.color.r, _locationNameText.color.g, _locationNameText.color.b, 0);
        }

        if (PlayerAttributes.LocalInstance != null)
        {
            _playerHudPanel.SetActive(true);
            // ANO, existuje. Propásli jsme událost OnNetworkSpawn.
            // Musíme si data "vytáhnout" ručně z Network Proměnných.
            UpdateHealthUI(
                PlayerAttributes.LocalInstance.CurrentHealth.Value,
                PlayerAttributes.LocalInstance.MaxHealth.Value
            );
            UpdateStaminaUI(
                PlayerAttributes.LocalInstance.CurrentStamina.Value,
                PlayerAttributes.LocalInstance.MaxStamina.Value
            );
            UpdateManaUI(
                PlayerAttributes.LocalInstance.CurrentMana.Value,
                PlayerAttributes.LocalInstance.MaxMana.Value
            );
        }
        else
        {
            // NE, hráč ještě neexistuje.
            // Skryjeme UI a počkáme, až OnNetworkSpawn zavolá 
            // události, které spustí Update...UI metody.
            if (_playerHudPanel != null)
                _playerHudPanel.SetActive(false);
        }
    }

    private void ShowHudPanel()
    {
        // Jakmile dostaneme první update, UI se zobrazí
        if (_playerHudPanel != null && !_playerHudPanel.activeSelf)
            _playerHudPanel.SetActive(true);
    }

    private void UpdateHealthUI(int current, int max)
    {
        ShowHudPanel(); // Zobrazí UI, pokud je skryté

        if (_healthSlider != null)
        {
            _healthSlider.maxValue = max;
            _healthSlider.value = current;
        }

        if (_healthText != null)
        {
            _healthText.text = $"{current} / {max}";
        }
    }

    private void UpdateStaminaUI(float current, int max)
    {
        ShowHudPanel(); // Zobrazí UI, pokud je skryté

        if (_staminaSlider != null)
        {
            _staminaSlider.maxValue = max;
            _staminaSlider.value = current;
        }

        if (_staminaText != null)
        {
            // Zaokrouhlíme jen pro zobrazení
            _staminaText.text = $"{Mathf.FloorToInt(current)} / {max}";
        }
    }

    private void UpdateManaUI(float current, int max)
    {
        ShowHudPanel(); // Zobrazí UI, pokud je skryté

        if (_manaSlider != null)
        {
            _manaSlider.maxValue = max;
            _manaSlider.value = current;
        }

        if (_manaText != null)
        {
            _manaText.text = $"{Mathf.FloorToInt(current)} / {max}";
        }
    }

    /// <summary>
    /// Zobrazí název lokace s fade-in a fade-out efektem
    /// </summary>
    public void ShowLocationName(string locationName)
    {
        if (_locationNameText == null) return;

        // Pokud už běží nějaká animace, zastavíme ji
        if (_locationCoroutine != null)
        {
            StopCoroutine(_locationCoroutine);
        }

        // Spustíme novou animaci
        _locationCoroutine = StartCoroutine(ShowLocationRoutine(locationName));
    }

    private IEnumerator ShowLocationRoutine(string locationName)
    {
        _locationNameText.text = locationName;
        _locationNameText.gameObject.SetActive(true);

        float timer = 0f;
        Color color = _locationNameText.color;

        // 1. Fade In (z 0 na 1)
        while (timer < _locationFadeTime)
        {
            timer += Time.deltaTime;
            color.a = Mathf.Lerp(0, 1, timer / _locationFadeTime);
            _locationNameText.color = color;
            yield return null;
        }

        // 2. Hold (zůstane viditelný)
        yield return new WaitForSeconds(_locationHoldTime);

        // 3. Fade Out (z 1 na 0)
        timer = 0f;
        while (timer < _locationFadeTime)
        {
            timer += Time.deltaTime;
            color.a = Mathf.Lerp(1, 0, timer / _locationFadeTime);
            _locationNameText.color = color;
            yield return null;
        }

        _locationNameText.gameObject.SetActive(false);
        _locationCoroutine = null;
    }
}