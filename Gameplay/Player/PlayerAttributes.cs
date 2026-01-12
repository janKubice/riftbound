using Unity.Netcode;
using UnityEngine;
using System;
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

            // Max hodnoty (pro UI update při změně maxima)
            MaxHealth.OnValueChanged += (oldVal, newVal) => HandleHealthChanged(CurrentHealth.Value, CurrentHealth.Value);
            MaxStamina.OnValueChanged += (oldVal, newVal) => HandleStaminaChanged(CurrentStamina.Value, CurrentStamina.Value);
            MaxMana.OnValueChanged += (oldVal, newVal) => HandleManaChanged(CurrentMana.Value, CurrentMana.Value);

            if (IsServer)
            {
                // Init hodnot ze SerializedField
                MaxHealth.Value = _defaultMaxHealth;
                MaxStamina.Value = _defaultMaxStamina;
                MaxMana.Value = _defaultMaxMana;

                HealthRegenRate.Value = _defaultHealthRegen;
                StaminaRegenRate.Value = _defaultStaminaRegen;
                ManaRegenRate.Value = _defaultManaRegen;

                CurrentHealth.Value = MaxHealth.Value;
                CurrentStamina.Value = MaxStamina.Value;
                CurrentMana.Value = MaxMana.Value;
                IsInvulnerable.Value = false;

                // Spuštění regenerace
                _regenRoutine = StartCoroutine(RegenerateAttributesRoutine());
            }

            if (IsOwner)
            {
                LocalInstance = this;
                Debug.Log($"[PlayerAttributes] Lokální instance hráče nastavena");
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
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! Invalid Destroy.");
            }
        }
        base.OnDestroy();
    }

    public override void OnNetworkDespawn()
    {
        CurrentHealth.OnValueChanged -= HandleHealthChanged;
        CurrentStamina.OnValueChanged -= HandleStaminaChanged;
        CurrentMana.OnValueChanged -= HandleManaChanged;

        // Anonymní lambdy nelze snadno odhlásit bez uložení reference,
        // ale při Despawnu se objekt ničí/resetuje, takže to kriticky nevadí,
        // pokud se NetworkVariable instance recykluje správně.
        // Pro čistotu by bylo lepší mít pojmenované metody, ale pro tento kontext to stačí.

        if (IsServer)
        {
            if (_regenRoutine != null) StopCoroutine(_regenRoutine);
            if (_invulnerabilityCoroutine != null) StopCoroutine(_invulnerabilityCoroutine);
        }

        if (IsOwner && LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    private void InvokeAllLocalUIEvents()
    {
        OnLocalPlayerHealthChanged?.Invoke(CurrentHealth.Value, MaxHealth.Value);
        OnLocalPlayerStaminaChanged?.Invoke(CurrentStamina.Value, MaxStamina.Value);
        OnLocalPlayerManaChanged?.Invoke(CurrentMana.Value, MaxMana.Value);
    }

    // --- NetworkVariable Callbacks ---

    private void HandleHealthChanged(int previousValue, int newValue)
    {
        if (IsOwner) OnLocalPlayerHealthChanged?.Invoke(newValue, MaxHealth.Value);
    }

    private void HandleStaminaChanged(float previousValue, float newValue)
    {
        if (IsOwner) OnLocalPlayerStaminaChanged?.Invoke(newValue, MaxStamina.Value);
    }

    private void HandleManaChanged(float previousValue, float newValue)
    {
        if (IsOwner) OnLocalPlayerManaChanged?.Invoke(newValue, MaxMana.Value);
    }

    // --- Regenerace (Pouze Server) ---

    private IEnumerator RegenerateAttributesRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(1.0f);

            // Zdraví
            if (CurrentHealth.Value < MaxHealth.Value && CurrentHealth.Value > 0)
            {
                CurrentHealth.Value = Mathf.Min(CurrentHealth.Value + (int)HealthRegenRate.Value, MaxHealth.Value);
            }

            // Stamina
            if (Time.time > _lastStaminaUseTime + _staminaRegenDelay && CurrentStamina.Value < MaxStamina.Value)
            {
                CurrentStamina.Value = Mathf.Min(CurrentStamina.Value + StaminaRegenRate.Value, MaxStamina.Value);
            }

            // Mana
            if (CurrentMana.Value < MaxMana.Value)
            {
                CurrentMana.Value = Mathf.Min(CurrentMana.Value + ManaRegenRate.Value, MaxMana.Value);
            }
        }
    }

    // --- ZAPOUZDŘENÍ ZDROJŮ (Encapsulation) ---

    /// <summary>
    /// Pokusí se spotřebovat Staminu. Vrací true, pokud byl dostatek a akce proběhla/byla odeslána.
    /// </summary>
    public bool ConsumeStamina(float amount)
    {
        // 1. Lokální predikce / kontrola
        if (CurrentStamina.Value < amount) return false;

        // 2. Aplikace
        if (IsServer)
        {
            PerformStaminaConsumption(amount);
        }
        else
        {
            // Klient pošle požadavek. Lokálně se hodnota nezmění okamžitě (čeká na sync),
            // ale vrátíme true, aby klient mohl přehrát animaci/efekt.
            ConsumeStaminaServerRpc(amount);
        }

        return true;
    }

    /// <summary>
    /// Pokusí se spotřebovat Manu. Vrací true, pokud byl dostatek a akce proběhla/byla odeslána.
    /// </summary>
    public bool ConsumeMana(float amount)
    {
        if (CurrentMana.Value < amount) return false;

        if (IsServer)
        {
            PerformManaConsumption(amount);
        }
        else
        {
            ConsumeManaServerRpc(amount);
        }

        return true;
    }

    // --- Interní logika změny hodnot (pouze Server) ---

    private void PerformStaminaConsumption(float amount)
    {
        _lastStaminaUseTime = Time.time;
        if (CurrentStamina.Value >= amount)
        {
            CurrentStamina.Value -= amount;
        }
    }

    private void PerformManaConsumption(float amount)
    {
        if (CurrentMana.Value >= amount)
        {
            CurrentMana.Value -= amount;
        }
    }

    // --- Server RPCs pro spotřebu ---

    [ServerRpc]
    private void ConsumeStaminaServerRpc(float amount)
    {
        if (CurrentStamina.Value >= amount)
        {
            PerformStaminaConsumption(amount);
        }
    }

    [ServerRpc]
    private void ConsumeManaServerRpc(float amount)
    {
        if (CurrentMana.Value >= amount)
        {
            PerformManaConsumption(amount);
        }
    }

    // --- Ostatní veřejné metody (Combat, Heal, Upgrade) ---

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

            if (ArenaManager.Instance != null && ArenaManager.Instance.IsPlayerInArena(OwnerClientId))
            {
                ArenaManager.Instance.OnPlayerDiedInArena(OwnerClientId);
                
                // Reset atributů pro stav v lobby
                CurrentHealth.Value = MaxHealth.Value;
                CurrentStamina.Value = MaxStamina.Value;
                CurrentMana.Value = MaxMana.Value;
            }
            else
            {
                Respawn();
            }
        }
    }

    public void Heal(int amount)
    {
        if (!IsServer) return;

        if (CurrentHealth.Value > 0)
        {
            CurrentHealth.Value = Mathf.Clamp(CurrentHealth.Value + amount, 0, MaxHealth.Value);
        }
    }

    [ServerRpc]
    public void UpgradeMaxHealthServerRpc(int amountToAdd)
    {
        MaxHealth.Value += amountToAdd;
        CurrentHealth.Value += amountToAdd;
    }

    [ServerRpc]
    public void SetInvulnerableServerRpc(float duration)
    {
        if (_invulnerabilityCoroutine != null)
        {
            StopCoroutine(_invulnerabilityCoroutine);
        }
        _invulnerabilityCoroutine = StartCoroutine(InvulnerabilityRoutine(duration));
    }

    private IEnumerator InvulnerabilityRoutine(float duration)
    {
        IsInvulnerable.Value = true;
        yield return new WaitForSeconds(duration);
        IsInvulnerable.Value = false;
        _invulnerabilityCoroutine = null;
    }

    // --- Respawn ---

    private void Respawn()
    {
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

    // --- Debug / Input Test ---
    private void Update()
    {
        if (!IsOwner) return;

#if UNITY_EDITOR
        if (Keyboard.current != null)
        {
            // Test poškození
            if (Keyboard.current.kKey.wasPressedThisFrame)
            {
                TakeDamageServerRpc(10);
            }
            
            // Test spotřeby staminy (nová metoda)
            if (Keyboard.current.lKey.wasPressedThisFrame)
            {
                bool success = ConsumeStamina(15);
                if (!success) Debug.Log("Not enough stamina!");
            }
        }
#endif
    }
}