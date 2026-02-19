using UnityEngine;
using Unity.Netcode;
using System;

public class EnemyHealth : NetworkBehaviour
{
    [SerializeField] private int _maxHealth = 30;
    private int _baseMaxHealth;
    [SerializeField] private bool _isTrainingDummy = false;
    private StatusEffectReceiver _statusReceiver;
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(30);
    private Rigidbody _rb;

    // Flag pro nesmrtelnost (Spawn fáze)
    public bool IsInvulnerable { get; set; } = false;

    // Eventy
    public event Action OnDeath;
    public event Action<int> OnDamageTaken;
    private EnemyTier _currentTier = EnemyTier.Normal;

    [Header("Loot")]
    [SerializeField] private LootTable _lootTable;
    [Range(0f, 1f)][SerializeField] private float _lootChance = 0.3f;

    [Header("Audio")]
    [SerializeField] private int _hurtSoundIndex = 0;
    [SerializeField] private int _deathSoundIndex = 1;
    private NetworkedAudioSource _netAudio;
    private ulong _lastAttackerId = 9999;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _statusReceiver = GetComponent<StatusEffectReceiver>();
        _netAudio = GetComponent<NetworkedAudioSource>();
        _baseMaxHealth = _maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentHealth.Value = _isTrainingDummy ? 999999 : _maxHealth;
        }
    }

    public void SetEnemyTier(EnemyTier tier)
    {
        _currentTier = tier;
    }

    // --- HLAVNÍ ZMĚNA ZDE ---
    
    /// <summary>
    /// Veřejná metoda, kterou může zavolat kdokoliv (Klient i Server).
    /// </summary>
    public void TakeDamage(int amount, ulong attackerId = 9999)
    {
        // Pokud jsme Server, rovnou aplikujeme poškození
        if (IsServer)
        {
            ApplyDamageLogic(amount, attackerId);
        }
        // Pokud jsme Klient, musíme poprosit Server o provedení
        else
        {
            RequestDamageServerRpc(amount, attackerId);
        }
    }

    /// <summary>
    /// RPC volání z klienta na server. 
    /// RequireOwnership = false znamená, že to může zavolat i hráč, který nevlastní tento objekt (což je správně, hráči střílí do cizích nepřátel).
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestDamageServerRpc(int amount, ulong attackerId)
    {
        ApplyDamageLogic(amount, attackerId);
    }

    /// <summary>
    /// Samotná logika poškození (běží POUZE na Serveru)
    /// </summary>
    private void ApplyDamageLogic(int amount, ulong attackerId)
    {
        // Zde už nemusíme kontrolovat !IsServer, protože sem se dostaneme jen na Serveru
        
        if (IsInvulnerable)
        {
            // Můžeme nechat log pro debug, ale bez 'IsServer' varování
            // Debug.Log("[EnemyHealth] Zásah ignorován - Nepřítel je Invulnerable.");
            return;
        }

        if (!_isTrainingDummy && CurrentHealth.Value <= 0) return;

        CurrentHealth.Value -= amount;
        _lastAttackerId = attackerId;

        if (attackerId != 9999 && SteamStatsManager.Instance != null)
        {
            // Připravíme parametry, aby se zpráva poslala JEN tomu, kdo útočil
            ClientRpcParams clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { attackerId }
                }
            };

            // Pošleme RPC konkrétnímu hráči: "Započítej si DMG"
            SteamStatsManager.Instance.IncrementStatClientRpc("stat_total_damage", amount, clientParams);
        }

        // Vizuální čísla (spawnuje server, NetworkTransform se postará o zbytek, nebo ClientRpc v Manažerovi)
        if (DamageNumberManager.Instance != null)
        {
            DamageNumberManager.Instance.SpawnDamageNumber(transform.position, amount, false);
        }

        // Zvuk
        if (_netAudio != null) 
            _netAudio.PlayOneShotNetworked(_hurtSoundIndex);
            
        // Eventy (zavolají se na serveru, pokud potřebuješ reakci u klienta, použij OnValueChanged na NetworkVariable nebo ClientRpc)
        OnDamageTaken?.Invoke(amount);

        // LOGIKA SMRTI
        if (CurrentHealth.Value <= 0)
        {
            if (_isTrainingDummy)
            {
                CurrentHealth.Value = 999999;
            }
            else
            {
                Die();
            }
        }
    }
    // ------------------------

    public void InitializeHealth(int maxHp)
    {
        if (!IsServer) return;
        _maxHealth = maxHp;
        CurrentHealth.Value = maxHp;
    }

    private void Die()
    {
        OnDeath?.Invoke();

        if (_netAudio != null) 
            _netAudio.PlayOneShotNetworked(_deathSoundIndex);

        if (IsServer && _lootTable != null && LootManager.Instance != null)
        {
            int dropRolls = 1;
            float chanceMultiplier = 1.0f;

            switch (_currentTier)
            {
                case EnemyTier.Normal: dropRolls = 1; chanceMultiplier = 1.0f; break;
                case EnemyTier.Elite: dropRolls = 2; chanceMultiplier = 1.2f; break;
                case EnemyTier.Champion: dropRolls = 3; chanceMultiplier = 1.5f; break;
                case EnemyTier.Boss: dropRolls = 5; chanceMultiplier = 10.0f; break;
            }

            for (int i = 0; i < dropRolls; i++)
            {
                if (UnityEngine.Random.value < _lootChance * chanceMultiplier)
                {
                    Vector3 randomOffset = new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        0.5f,
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    );
                    LootManager.Instance.SpawnLoot(transform.position + randomOffset, _lootTable);
                }
            }
        }

        if (_rb != null) _rb.isKinematic = true;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        if (_lastAttackerId != 9999 && SteamStatsManager.Instance != null)
        {
            // Připravíme parametry, aby se zpráva poslala JEN tomu, kdo útočil
            ClientRpcParams clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams
                {
                    TargetClientIds = new ulong[] { _lastAttackerId }
                }
            };

            // Pošleme RPC konkrétnímu hráči: "Započítej si DMG"
            SteamStatsManager.Instance.IncrementStatClientRpc("stat_total_damage", 1, clientParams);
        }
        
        DestroySelf(); // Volání úklidu
    }

    public void ApplyKnockback(Vector3 force)
    {
        if (!IsServer || _rb == null || IsInvulnerable) return;
        _rb.AddForce(force, ForceMode.Impulse);
    }

    public void ApplyStatusEffect(StatusEffectData effectData)
    {
        if (!IsServer || IsInvulnerable || _statusReceiver == null) return;
        _statusReceiver.ApplyStatusEffect(effectData);
    }

    public void DestroySelf()
    {
        if (!IsServer) return;

        var netObj = GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true); // Vypnutí a návrat do poolu
        }
        else
        {
            Destroy(gameObject);
        }
    }
}