using Unity.Netcode;
using UnityEngine;
using System; // Pro System.Action
using System.Collections;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerAttributes : NetworkBehaviour
{
    [Header("Základní Atributy (Výchozí)")]
    [SerializeField] private int _defaultMaxHealth = 100;
    [SerializeField] private int _defaultMaxStamina = 100;
    [SerializeField] private int _defaultMaxMana = 50;

    [Header("Základní Regenerace (za sekundu)")]
    [SerializeField] private float _defaultHealthRegen = 1.0f;
    [SerializeField] private float _defaultStaminaRegen = 10.0f;
    [SerializeField] private float _defaultManaRegen = 5.0f;

    [Header("Nastavení Respawnu")]
    [SerializeField] private Vector3 _respawnPosition = new Vector3(0, 1, 0);

    // --- Network Variables ---
    // Musí být public, aby k nim mělo přístup UI v metodě Start()

    // Zdraví
    public NetworkVariable<int> CurrentHealth { get; private set; } =
        new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> MaxHealth { get; private set; } =
        new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> HealthRegenRate { get; private set; } =
        new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Stamina
    public NetworkVariable<float> CurrentStamina { get; private set; } =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> MaxStamina { get; private set; } =
        new NetworkVariable<int>(100, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> StaminaRegenRate { get; private set; } =
        new NetworkVariable<float>(10f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private float _lastStaminaUseTime;
    [SerializeField] private float _staminaRegenDelay = 1.5f;

    // Mana
    public NetworkVariable<float> CurrentMana { get; private set; } =
        new NetworkVariable<float>(50f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<int> MaxMana { get; private set; } =
        new NetworkVariable<int>(50, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    public NetworkVariable<float> ManaRegenRate { get; private set; } =
        new NetworkVariable<float>(5f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Úhyb (Dodge)
    public NetworkVariable<bool> IsInvulnerable { get; private set; } =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // --- UI Eventy (pouze pro lokálního hráče) ---
    public static event Action<int, int> OnLocalPlayerHealthChanged;
    public static event Action<float, int> OnLocalPlayerStaminaChanged;
    public static event Action<float, int> OnLocalPlayerManaChanged;

    public static PlayerAttributes LocalInstance { get; private set; }

    private CharacterController _controller;
    private Coroutine _regenRoutine;
    private Coroutine _invulnerabilityCoroutine;
    private PlayerAudio _playerAudio;
    private void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _playerAudio = GetComponent<PlayerAudio>();
    }

    public override void OnNetworkSpawn()
    {
        try
        {
            // Přihlášení k odběru změn
            CurrentHealth.OnValueChanged += HandleHealthChanged;
            CurrentStamina.OnValueChanged += HandleStaminaChanged;
            CurrentMana.OnValueChanged += HandleManaChanged;

            // Max hodnoty (pro UI)
            MaxHealth.OnValueChanged += (oldVal, newVal) => HandleHealthChanged(CurrentHealth.Value, CurrentHealth.Value);
            MaxStamina.OnValueChanged += (oldVal, newVal) => HandleStaminaChanged(CurrentStamina.Value, CurrentStamina.Value);
            MaxMana.OnValueChanged += (oldVal, newVal) => HandleManaChanged(CurrentMana.Value, CurrentMana.Value);


            if (IsServer)
            {
                // Nastavení výchozích hodnot ze SerializedField
                MaxHealth.Value = _defaultMaxHealth;
                MaxStamina.Value = _defaultMaxStamina;
                MaxMana.Value = _defaultMaxMana;

                HealthRegenRate.Value = _defaultHealthRegen;
                StaminaRegenRate.Value = _defaultStaminaRegen;
                ManaRegenRate.Value = _defaultManaRegen;

                // Plné atributy při startu
                CurrentHealth.Value = MaxHealth.Value;
                CurrentStamina.Value = MaxStamina.Value;
                CurrentMana.Value = MaxMana.Value;
                IsInvulnerable.Value = false;

                // Spuštění regenerace POUZE na serveru
                _regenRoutine = StartCoroutine(RegenerateAttributesRoutine());
            }

            if (IsOwner)
            {
                LocalInstance = this;
                Debug.Log($"[PlayerAttributes] Lokální instance hráče nastavena");
                // Vyvoláme události pro UI, které už možná poslouchá
                // (pro případ, že by se UI načetlo dříve)
                InvokeAllLocalUIEvents();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[CRITICAL SPAWN ERROR] Chyba v {name}: {e.Message}\n{e.StackTrace}");
        }

    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    public override void OnNetworkDespawn()
    {
        // Vždy se odhlásíme
        CurrentHealth.OnValueChanged -= HandleHealthChanged;
        CurrentStamina.OnValueChanged -= HandleStaminaChanged;
        CurrentMana.OnValueChanged -= HandleManaChanged;

        MaxHealth.OnValueChanged -= (oldVal, newVal) => HandleHealthChanged(CurrentHealth.Value, CurrentHealth.Value);
        MaxStamina.OnValueChanged -= (oldVal, newVal) => HandleStaminaChanged(CurrentStamina.Value, CurrentStamina.Value);
        MaxMana.OnValueChanged -= (oldVal, newVal) => HandleManaChanged(CurrentMana.Value, CurrentMana.Value);

        if (IsServer && _regenRoutine != null)
        {
            StopCoroutine(_regenRoutine);
        }
        if (IsServer && _invulnerabilityCoroutine != null)
        {
            StopCoroutine(_invulnerabilityCoroutine);
        }

        if (IsOwner && LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    /// <summary>
    /// Vynutí aktualizaci všech UI prvků pro lokálního hráče
    /// </summary>
    private void InvokeAllLocalUIEvents()
    {
        OnLocalPlayerHealthChanged?.Invoke(CurrentHealth.Value, MaxHealth.Value);
        OnLocalPlayerStaminaChanged?.Invoke(CurrentStamina.Value, MaxStamina.Value);
        OnLocalPlayerManaChanged?.Invoke(CurrentMana.Value, MaxMana.Value);
    }

    // --- NetworkVariable Callbacks ---

    private void HandleHealthChanged(int previousValue, int newValue)
    {
        if (IsOwner)
        {
            OnLocalPlayerHealthChanged?.Invoke(newValue, MaxHealth.Value);
        }
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        if (IsOwner)
        {
            OnLocalPlayerStaminaChanged?.Invoke(newValue, MaxStamina.Value);
        }
    }

    private void HandleManaChanged(float previousValue, float newValue)
    {
        if (IsOwner)
        {
            OnLocalPlayerManaChanged?.Invoke(newValue, MaxMana.Value);
        }
    }

    // --- Regenerace (Pouze Server) ---

    private IEnumerator RegenerateAttributesRoutine()
    {
        // Běží pouze na serveru
        while (true)
        {
            yield return new WaitForSeconds(1.0f);

            // Regenerace zdraví
            if (CurrentHealth.Value < MaxHealth.Value && CurrentHealth.Value > 0) // Neregenerujeme mrtvé
            {
                CurrentHealth.Value = Mathf.Min(CurrentHealth.Value + (int)HealthRegenRate.Value, MaxHealth.Value);
            }

            // Regenerace staminy
            if (Time.time > _lastStaminaUseTime + _staminaRegenDelay && CurrentStamina.Value < MaxStamina.Value)
            {
                CurrentStamina.Value = Mathf.Min(CurrentStamina.Value + StaminaRegenRate.Value, MaxStamina.Value);
            }

            // Regenerace many
            if (CurrentMana.Value < MaxMana.Value)
            {
                CurrentMana.Value = Mathf.Min(CurrentMana.Value + ManaRegenRate.Value, MaxMana.Value);
            }
        }
    }

    // --- Veřejné metody pro Server ---

    [ServerRpc(RequireOwnership = false)]
    public void TakeDamageServerRpc(int amount)
    {
        if (CurrentHealth.Value <= 0 || IsInvulnerable.Value) return;

        CurrentHealth.Value -= amount;

        if (_playerAudio != null)
        {
            _playerAudio.RequestPlaySoundServerRpc(PlayerAudio.AUDIO_HIT_RECEIVED);
        }

        if (CurrentHealth.Value <= 0)
        {
            CurrentHealth.Value = 0;

            // Zjistíme, zda jsme v aréně
            if (ArenaManager.Instance != null && ArenaManager.Instance.IsPlayerInArena(OwnerClientId))
            {
                // Jsme v boji -> Reportujeme smrt Aréně
                ArenaManager.Instance.OnPlayerDiedInArena(OwnerClientId);

                // Obnovíme zdraví, aby hráč v lobby neležel mrtvý
                CurrentHealth.Value = MaxHealth.Value;
                CurrentStamina.Value = MaxStamina.Value;
                CurrentMana.Value = MaxMana.Value;

                // Poznámka: Teleportaci do lobby zajistí ArenaManager.OnPlayerDiedInArena
            }
            else
            {
                // Nejsme v aréně -> Standardní Respawn
                Respawn();
            }
        }
    }

    /// <summary>
    /// Metoda pro léčení (volaná ze Spellbooku nebo Lektvaru)
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsServer) return; // Měníme NetworkVariable, musí běžet na serveru

        if (CurrentHealth.Value > 0) // Neléčíme mrtvé
        {
            int oldVal = CurrentHealth.Value;
            CurrentHealth.Value = Mathf.Clamp(CurrentHealth.Value + amount, 0, MaxHealth.Value);

            // Zde můžeš přidat audio/vfx pro heal, pokud se hodnota změnila
        }
    }

    /// <summary>
    /// Veřejná metoda pro spotřebu staminy. Volá ji lokální hráč, běží na serveru.
    /// </summary>
    [ServerRpc]
    public void ConsumeStaminaServerRpc(float amount)
    {
        _lastStaminaUseTime = Time.time;
        if (CurrentStamina.Value >= amount)
        {
            CurrentStamina.Value -= amount;
        }
        // Pokud nemá dost, server prostě nic neodečte.
        // Kontrolu (zda vůbec akci provést) by si měl dělat i klient.
    }

    /// <summary>
    /// Veřejná metoda pro spotřebu many.
    /// </summary>
    [ServerRpc]
    public void ConsumeManaServerRpc(float amount)
    {
        if (CurrentMana.Value >= amount)
        {
            CurrentMana.Value -= amount;
        }
    }

    /// <summary>
    /// (Bod 3) Metoda pro upgrade maximálního zdraví.
    /// </summary>
    [ServerRpc]
    public void UpgradeMaxHealthServerRpc(int amountToAdd)
    {
        MaxHealth.Value += amountToAdd;
        // Volitelně můžeme rovnou doplnit i aktuální zdraví
        CurrentHealth.Value += amountToAdd;
    }

    /// <summary>
    /// Klient žádá server, aby se stal na krátkou dobu nesmrtelným
    /// </summary>
    [ServerRpc]
    public void SetInvulnerableServerRpc(float duration)
    {
        // Spustíme coroutine na serveru
        // Zastavíme předchozí, pokud ještě běží, a spustíme novou
        if (_invulnerabilityCoroutine != null)
        {
            StopCoroutine(_invulnerabilityCoroutine);
        }
        _invulnerabilityCoroutine = StartCoroutine(InvulnerabilityRoutine(duration));
    }

    /// <summary>
    /// Coroutine běžící POUZE na serveru
    /// </summary>
    private IEnumerator InvulnerabilityRoutine(float duration)
    {
        IsInvulnerable.Value = true;
        yield return new WaitForSeconds(duration);
        IsInvulnerable.Value = false;
        _invulnerabilityCoroutine = null;
    }

    // Zde mohou být v budoucnu další upgrady (UpgradeStaminaRegenServerRpc atd.)

    // --- Respawn ---

    private void Respawn()
    {
        // Server obnoví zdraví (a případně manu/staminu)
        CurrentHealth.Value = MaxHealth.Value;
        CurrentStamina.Value = MaxStamina.Value;
        CurrentMana.Value = MaxMana.Value;

        RespawnClientRpc(_respawnPosition);
    }

    [ClientRpc]
    private void RespawnClientRpc(Vector3 spawnPosition)
    {
        if (IsOwner)
        {
            _controller.enabled = false;
            transform.position = spawnPosition;
            _controller.enabled = true;
        }
    }

    // --- Pro testování (můžete smazat později) ---
    private void Update()
    {
        if (!IsOwner) return;

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.kKey.wasPressedThisFrame)
        {
            TakeDamageServerRpc(10);
        }
        if (Keyboard.current != null && Keyboard.current.lKey.wasPressedThisFrame)
        {
            // Test spotřeby staminy
            ConsumeStaminaServerRpc(15);
        }
#endif
    }
}