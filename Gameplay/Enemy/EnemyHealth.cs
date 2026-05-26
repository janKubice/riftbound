using UnityEngine;
using Unity.Netcode;
using System;
using System.Collections.Generic;

public class EnemyHealth : NetworkBehaviour
{
    EnemyDefinition _definition;
    [Header("Settings")]
    private StatusEffectReceiver _statusReceiver;
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(30);
    private Rigidbody _rb;

    // Flag pro nesmrtelnost (Spawn fáze)
    public bool IsInvulnerable { get; set; } = false;

    // Eventy
    public event Action OnDeath;
    public event Action<int> OnDamageTaken;
    private EnemyTier _currentTier;


    [Header("Audio")]
    private NetworkedAudioSource _netAudio;
    private ulong _lastAttackerId = 9999;

    private bool _isExplosiveKill = false;
    private Vector3 _explosionCenter;
    private float _explosionForce;
    private float _explosionRadius;
    private bool _isDead = false;

    private int _currentMaxHealth;
    public int MaxHealth => _currentMaxHealth;
    public bool IsInjured => CurrentHealth.Value < MaxHealth;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _statusReceiver = GetComponent<StatusEffectReceiver>();
        _netAudio = GetComponent<NetworkedAudioSource>();
    }

    public override void OnNetworkSpawn()
    {
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

        if (!_definition._isTrainingDummy && CurrentHealth.Value <= 0) return;

        int finalDamage = amount;
        PlayerAttributes attackerAttributes = null;
        // Pokud útočník je hráč, aplikujeme jeho bonusy na serveru
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(attackerId, out var client))
        {
            attackerAttributes = client.PlayerObject.GetComponent<PlayerAttributes>();
            var playerProg = client.PlayerObject.GetComponent<PlayerProgression>();
            if (playerProg != null)
            {
                finalDamage = (int)(amount * playerProg.GetStatMultiplier(StatType.DamageMultiplier, 1f));
            }
        }

        CurrentHealth.Value -= finalDamage;
        _lastAttackerId = attackerId;


        // --- IMPLEMENTACE LIFE STEALU (VAMPIRISM) ---
        if (attackerAttributes != null && client != null)
        {
            var playerProg = client.PlayerObject.GetComponent<PlayerProgression>();
            if (playerProg != null)
            {
                // Získání procentuální hodnoty Life Stealu (např. 0.05 pro 5%)
                float lifeStealPct = playerProg.GetStatBonus(StatType.LifeSteal);

                if (lifeStealPct > 0f)
                {
                    int healAmount = Mathf.RoundToInt(finalDamage * lifeStealPct);
                    if (healAmount > 0)
                    {
                        attackerAttributes.Heal(healAmount);
                    }
                }
            }
        }

        if (attackerId != 9999 && SteamStatsManager.Instance != null && SteamStatsManager.Instance.IsSpawned) // PŘIDÁNO IsSpawned
        {
            ClientRpcParams clientParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { attackerId } }
            };

            SteamStatsManager.Instance.IncrementStatClientRpc(
                SteamStatIds.TotalDamage,
                finalDamage,
                clientParams
            );
        }

        // Vizuální čísla (spawnuje server, NetworkTransform se postará o zbytek, nebo ClientRpc v Manažerovi)
        if (DamageNumberManager.Instance != null)
        {
            DamageNumberManager.Instance.SpawnDamageNumber(transform.position, amount, PopupType.Damage);
        }

        // Zvuk
        if (_netAudio != null)
            _netAudio.PlayOneShotLocal(_definition.HitSfx);

        // Eventy (zavolají se na serveru, pokud potřebuješ reakci u klienta, použij OnValueChanged na NetworkVariable nebo ClientRpc)
        OnDamageTaken?.Invoke(amount);

        // LOGIKA SMRTI
        if (CurrentHealth.Value <= 0)
        {
            if (_definition._isTrainingDummy)
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

    /// <summary>
    /// Vyléčí nepřítele (běží POUZE na Serveru)
    /// </summary>
    public void Heal(int amount)
    {
        if (!IsServer || _isDead) return;

        // Přidáme HP, ale nesmíme přesáhnout MaxHealth
        CurrentHealth.Value = Mathf.Min(CurrentHealth.Value + amount, MaxHealth);

        if (DamageNumberManager.Instance != null)
        {
            DamageNumberManager.Instance.SpawnDamageNumber(transform.position, amount, PopupType.Heal);
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

    public void InitializeHealth(EnemyDefinition def, EnemyTier tier, int maxHp)
    {
        if (!IsServer) return;
        _definition = def;
        _currentTier = tier;
        _currentMaxHealth = maxHp;
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
        if (IsServer && _lastAttackerId != 9999)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(_lastAttackerId, out var client))
            {
                if (client.PlayerObject != null && client.PlayerObject.TryGetComponent(out PlayerAttributes playerAttr))
                {
                    playerAttr.AddKill();
                    if (SteamStatsManager.Instance != null && SteamStatsManager.Instance.IsSpawned)
                    {
                        SteamStatsManager.Instance.IncrementStatForClient(
                            _lastAttackerId,
                            SteamStatIds.TotalKills,
                            1
                        );

                        switch (_currentTier)
                        {
                            case EnemyTier.Elite:
                                SteamStatsManager.Instance.IncrementStatForClient(
                                    _lastAttackerId,
                                    SteamStatIds.EliteKills,
                                    1
                                );
                                break;

                            case EnemyTier.Champion:
                                SteamStatsManager.Instance.IncrementStatForClient(
                                    _lastAttackerId,
                                    SteamStatIds.ChampionKills,
                                    1
                                );
                                break;
                        }
                    }
                }
            }
        }

        OnDeath?.Invoke();

        if (_netAudio != null)
            _netAudio.PlayOneShotLocal(_definition.DeathSfx);

        if (IsServer && DirectorSpawner.Instance != null)
        {
            DirectorSpawner.Instance.EnemyDied();
        }

        if (IsServer && _definition._lootTable != null && LootManager.Instance != null)
        {
            int dropRolls = 1;
            float chanceMultiplier = 1.0f;
            float luckBonus = 0f;
            float expMultiplier = 1f;
            float tierAmountMultiplier = 1f; // Přidáno pro škálování hodnoty lootu

            // Získání Fortune bonusu z PlayerProgression útočníka
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(_lastAttackerId, out var client))
            {
                var playerProg = client.PlayerObject.GetComponent<PlayerProgression>();
                if (playerProg != null)
                {
                    // Vrací např. 0.15f (což znamená 15 %)
                    luckBonus = playerProg.GetStatBonus(StatType.Luck);

                    // Vrací 1.0f + 0.15f = 1.15f (násobič celkového XP)
                    expMultiplier = 1f + playerProg.GetStatBonus(StatType.ExperienceGain);
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

            // 1. Šance na drop
            // Zde je zvolen multiplikativní přístup (bezpečnější pro balancování). 
            // Např. základní šance 10 % (0.1) a Luck 15 % (0.15) = 0.1 * 1.15 = 11.5 % šance.
            // (Pokud chceš aditivní přístup, použij: finalChance = (_definition._lootChance * chanceMultiplier) + luckBonus;)
            float finalChance = _definition._lootChance * chanceMultiplier * (1f + luckBonus);

            // 2. Počet pokusů o drop (Extra rolls z Lucku)
            // FloorToInt zajistí garantované rolly (např. Luck 120 % = garantovaný 1 extra roll navíc)
            int guaranteedExtraRolls = Mathf.FloorToInt(luckBonus);
            int finalRolls = dropRolls + guaranteedExtraRolls;

            // Zbytek po dělení (např. u 120 % zbyde 0.20) se použije jako pravděpodobnost na další roll
            if (UnityEngine.Random.value < (luckBonus % 1f))
            {
                finalRolls++;
            }

            // 3. Modifikátor objemu lootu (Pro XP/Gold, který se předá do LootTable)
            float finalAmountMultiplier = expMultiplier * tierAmountMultiplier;

            // Generování lootu
            for (int i = 0; i < finalRolls; i++)
            {
                if (UnityEngine.Random.value < finalChance)
                {
                    Vector3 randomOffset = new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        0.5f,
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    );

                    LootManager.Instance.SpawnLoot(transform.position + randomOffset, _definition._lootTable, finalAmountMultiplier);
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
        if (_definition.GorePrefabs == null || _definition.GorePrefabs.Length == 0) return;

        if (_definition.DeathVFX == null)
        {
            // Instanciace lokálního ne-síťového objektu
            GameObject vfx = Instantiate(_definition.DeathVFX, pos, Quaternion.identity);

            // Pojistka pro smazání objektu, pokud particle system nemá nastaveno "Stop Action: Destroy"
            Destroy(vfx, 5f);
        }

        // Náhodně určíme, kolik kusů (1 až VŠECHNY) z nepřítele vypadne
        int countToSpawn = UnityEngine.Random.Range(1, _definition.GorePrefabs.Length + 1);

        for (int i = 0; i < countToSpawn; i++)
        {
            // Vybereme náhodný prefab z listu pro každý kus
            GameObject prefab = _definition.GorePrefabs[UnityEngine.Random.Range(0, _definition.GorePrefabs.Length)];

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