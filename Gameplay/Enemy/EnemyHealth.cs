using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections.Generic;

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

    [Header("VFX")]
    [Tooltip("Prefab částicového efektu (Particle System) přehráný při smrti.")]
    [SerializeField] private GameObject _deathVFXPrefab;

    [Header("Gore System")]
    [Tooltip("Seznam prefabů kousků těla (hlava, ruka, kosti). Každý prefab musí mít Rigidbody a Collider.")]
    [SerializeField] private List<GameObject> _gorePrefabs = new List<GameObject>();
    private bool _isExplosiveKill = false;
    private Vector3 _explosionCenter;
    private float _explosionForce;
    private float _explosionRadius;
    private bool _isDead = false;

    public int MaxHealth => _baseMaxHealth > 0 ? _baseMaxHealth : _maxHealth;
    public bool IsInjured => CurrentHealth.Value < MaxHealth;

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

        // 1. Znovu zapnout všechny collidery
        Collider[] allColliders = GetComponentsInChildren<Collider>();
        foreach (var col in allColliders)
        {
            col.enabled = true;
        }

        // 2. Obnovit fyziku (při smrti jsme zapínali isKinematic = true)
        if (_rb != null)
        {
            _rb.isKinematic = false;
        }

        // 3. Reset nesmrtelnosti (pro jistotu)
        IsInvulnerable = false;
        _isDead = false;
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
        if (!IsSpawned || _isDead) return;
        if (IsInvulnerable)
        {
            // Můžeme nechat log pro debug, ale bez 'IsServer' varování
            // Debug.Log("[EnemyHealth] Zásah ignorován - Nepřítel je Invulnerable.");
            return;
        }

        if (!_isTrainingDummy && CurrentHealth.Value <= 0) return;

        int finalDamage = amount;
        // Pokud útočník je hráč, aplikujeme jeho bonusy na serveru
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out var client))
        {
            var playerProg = client.PlayerObject.GetComponent<PlayerProgression>();
            if (playerProg != null)
            {
                finalDamage = (int)(amount * playerProg.GetStatMultiplier(StatType.DamageMultiplier));
            }
        }

        CurrentHealth.Value -= finalDamage;
        _lastAttackerId = attackerId;

        if (attackerId != 9999 && SteamStatsManager.Instance != null && SteamStatsManager.Instance.IsSpawned) // PŘIDÁNO IsSpawned
        {
            ClientRpcParams clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { attackerId } }
            };

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
                _isDead = true;

                if (_isExplosiveKill)
                {
                    DieWithExplosion();
                }
                else
                {
                    Die();
                }
            }
        }
    }

    public void TakeExplosiveDamage(int amount, Vector3 expCenter, float expForce, float expRadius, ulong attackerId = 9999)
    {
        if (IsServer)
        {
            _isExplosiveKill = true;
            _explosionCenter = expCenter;
            _explosionForce = expForce;
            _explosionRadius = expRadius;

            ApplyDamageLogic(amount, attackerId);

            _isExplosiveKill = false; // Reset pro jistotu
        }
        else
        {
            RequestExplosiveDamageServerRpc(amount, expCenter, expForce, expRadius, attackerId);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    private void RequestExplosiveDamageServerRpc(int amount, Vector3 expCenter, float expForce, float expRadius, ulong attackerId)
    {
        TakeExplosiveDamage(amount, expCenter, expForce, expRadius, attackerId);
    }

    public void InitializeHealth(int maxHp)
    {
        if (!IsServer) return;
        _maxHealth = maxHp;
        CurrentHealth.Value = maxHp;

        // 1. Znovu zapnout všechny collidery
        Collider[] allColliders = GetComponentsInChildren<Collider>();
        foreach (var col in allColliders)
        {
            col.enabled = true;
        }

        // 2. Obnovit fyziku (při smrti jsme zapínali isKinematic = true)
        if (_rb != null)
        {
            _rb.isKinematic = false;
        }

        // 3. Reset nesmrtelnosti (pro jistotu)
        IsInvulnerable = false;
    }

    private void Die()
    {
        PerformSharedDeathLogic();

        // Spawn běžného rozpadu (není exploze)
        SpawnGorePrefabsClientRpc(transform.position, false, Vector3.zero, 0f, 0f);

        DestroySelf();
    }

    private void DieWithExplosion()
    {
        if (_rb != null) _rb.isKinematic = true;
        Collider[] allColliders = GetComponentsInChildren<Collider>();
        foreach (var col in allColliders) col.enabled = false;

        // Spawn rozmetání meteoritem
        SpawnGorePrefabsClientRpc(transform.position, true, _explosionCenter, _explosionForce, _explosionRadius);

        if (IsServer && gameObject.activeInHierarchy)
        {
            StartCoroutine(DelayedDespawnRoutine());
        }
    }

    private System.Collections.IEnumerator DelayedDespawnRoutine()
    {
        // Počkáme 0.1s, aby síť zaručeně stihla odeslat RPC
        yield return new WaitForSeconds(0.1f);

        // AŽ TEĎ zavoláme zbytek logiky smrti (loot, OnDeath eventy) a zničíme
        PerformSharedDeathLogic();
        DestroySelf();
    }

    private void PerformSharedDeathLogic()
    {
        OnDeath?.Invoke();

        if (_netAudio != null)
            _netAudio.PlayOneShotNetworked(_deathSoundIndex);

        if (IsServer && DirectorSpawner.Instance != null)
        {
            DirectorSpawner.Instance.EnemyDied();
        }

        if (IsServer && _lootTable != null && LootManager.Instance != null)
        {
            int dropRolls = 1;
            float chanceMultiplier = 1.0f;
            float fortuneBonus = 0f;
            float expMultiplier = 1f;
            float tierAmountMultiplier = 1f; // Přidáno pro škálování hodnoty lootu

            // Získání Fortune bonusu z PlayerProgression útočníka
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(_lastAttackerId, out var client))
            {
                var playerProg = client.PlayerObject.GetComponent<PlayerProgression>();
                if (playerProg != null)
                {
                    fortuneBonus = playerProg.GetStatBonus(StatType.Luck);
                    expMultiplier = 1 + playerProg.GetStatBonus(StatType.ExperienceGain);
                }
            }

            switch (_currentTier)
            {
                case EnemyTier.Normal:
                    dropRolls = 1; chanceMultiplier = 1.0f; tierAmountMultiplier = 1.0f; break;
                case EnemyTier.Elite:
                    dropRolls = 2; chanceMultiplier = 1.2f; tierAmountMultiplier = 2.0f; break;
                case EnemyTier.Champion:
                    dropRolls = 3; chanceMultiplier = 1.5f; tierAmountMultiplier = 3.0f; break;
                case EnemyTier.Boss:
                    dropRolls = 5; chanceMultiplier = 10.0f; tierAmountMultiplier = 5.0f; break;
            }

            float finalChance = (_lootChance * chanceMultiplier) + fortuneBonus;
            int finalRolls = dropRolls + (int)(fortuneBonus * 2);

            // Výpočet finálního násobiče (hráčský bonus * tier bonus)
            float finalAmountMultiplier = expMultiplier * tierAmountMultiplier;

            for (int i = 0; i < finalRolls; i++)
            {
                if (UnityEngine.Random.value < finalChance)
                {
                    Vector3 randomOffset = new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        0.5f,
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    );

                    // Použití finalAmountMultiplier
                    LootManager.Instance.SpawnLoot(transform.position + randomOffset, _lootTable, finalAmountMultiplier);
                }
            }
        }

        if (_rb != null) _rb.isKinematic = true;
        Collider[] allColliders = GetComponentsInChildren<Collider>();
        foreach (var col in allColliders)
        {
            col.enabled = false;
        }

        DestroySelf();
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

        // Pouze ho bezpečně odspawnujeme (to ho automaticky vrátí do tvého Poolu).
        // NIKDY nevoláme Destroy(gameObject), pokud používáme pooling!
        if (netObj != null && netObj.IsSpawned)
        {
            netObj.Despawn(true);
        }
    }

    [ClientRpc]
    private void SpawnGorePrefabsClientRpc(Vector3 pos, bool isExplosion, Vector3 expCenter, float expForce, float expRadius)
    {
        // Pokud nemáme nastavené žádné části, ignorujeme
        if (_gorePrefabs == null || _gorePrefabs.Count == 0) return;

        if (_deathVFXPrefab != null)
        {
            // Instanciace lokálního ne-síťového objektu
            GameObject vfx = Instantiate(_deathVFXPrefab, pos, Quaternion.identity);

            // Pojistka pro smazání objektu, pokud particle system nemá nastaveno "Stop Action: Destroy"
            Destroy(vfx, 5f);
        }

        // Náhodně určíme, kolik kusů (1 až VŠECHNY) z nepřítele vypadne
        int countToSpawn = UnityEngine.Random.Range(1, _gorePrefabs.Count + 1);

        for (int i = 0; i < countToSpawn; i++)
        {
            // Vybereme náhodný prefab z listu pro každý kus
            GameObject prefab = _gorePrefabs[UnityEngine.Random.Range(0, _gorePrefabs.Count)];

            // Mírný rozptyl pozice (aby se nespawnuly přesně v sobě a neexplodovaly o sebe chybou fyziky)
            Vector3 spawnOffset = new Vector3(
                UnityEngine.Random.Range(-0.5f, 0.5f),
                UnityEngine.Random.Range(0.5f, 1.5f),
                UnityEngine.Random.Range(-0.5f, 0.5f)
            );

            GameObject goreObj = Instantiate(prefab, pos + spawnOffset, UnityEngine.Random.rotation);

            // Aplikace fyziky
            if (goreObj.TryGetComponent(out Rigidbody rb))
            {
                if (isExplosion)
                {
                    // Rozmetání do okolí (přidán upthrust 3.0f pro obloukový let)
                    rb.AddExplosionForce(expForce, expCenter, expRadius, 3.0f, ForceMode.Impulse);
                    rb.AddTorque(UnityEngine.Random.insideUnitSphere * 50f, ForceMode.Impulse);
                }
                else
                {
                    // Běžná smrt - kousky jen lehce vyskočí a spadnou
                    Vector3 popDirection = new Vector3(UnityEngine.Random.Range(-1f, 1f), 2f, UnityEngine.Random.Range(-1f, 1f));
                    rb.AddForce(popDirection * 3f, ForceMode.Impulse);
                    rb.AddTorque(UnityEngine.Random.insideUnitSphere * 10f, ForceMode.Impulse);
                }
            }

            // Náhodné zničení po 10 až 30 sekundách (úklid paměti)
            Destroy(goreObj, UnityEngine.Random.Range(10f, 30f));
        }
    }
}