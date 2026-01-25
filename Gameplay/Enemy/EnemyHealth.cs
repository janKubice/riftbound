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

    // Event pro AI Controller (aby věděl, že má zahrát animaci)
    public event Action OnDeath;
    public event Action<int> OnDamageTaken;
    private EnemyTier _currentTier = EnemyTier.Normal;

    [Header("Loot")]
    [SerializeField] private LootTable _lootTable;
    [Range(0f, 1f)][SerializeField] private float _lootChance = 0.3f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _statusReceiver = GetComponent<StatusEffectReceiver>();
        _baseMaxHealth = _maxHealth;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Pokud je to panák, dáme mu hodně HP, aby se UI slider nezbláznil
            CurrentHealth.Value = _isTrainingDummy ? 999999 : _maxHealth;
        }
    }

    public void SetEnemyTier(EnemyTier tier)
    {
        _currentTier = tier;
    }

    public void TakeDamage(int amount, ulong attackerId = 9999)
    {
        // 1. Zjistíme, jestli se metoda vůbec zavolala
        Debug.Log($"[EnemyHealth] TakeDamage zavoláno! Amount: {amount}, IsServer: {IsServer}");

        if (!IsServer || IsInvulnerable)
        {
            Debug.Log("[EnemyHealth] Ignoruji zásah (Nejsem Server nebo jsem Invulnerable).");
            return;
        }

        if (!_isTrainingDummy && CurrentHealth.Value <= 0) return;

        CurrentHealth.Value -= amount;

        // 2. Zjistíme, jestli existuje Manažer
        if (DamageNumberManager.Instance != null)
        {
            Debug.Log("[EnemyHealth] Manažer nalezen, volám SpawnDamageNumber.");
            DamageNumberManager.Instance.SpawnDamageNumber(transform.position, amount, false);
        }
        else
        {
            Debug.LogError("[EnemyHealth] CHYBA: DamageNumberManager.Instance je NULL! Chybí ve scéně?");
        }

        OnDamageTaken?.Invoke(amount);

        // LOGIKA SMRTI vs DUMMY
        if (CurrentHealth.Value <= 0)
        {
            if (_isTrainingDummy)
            {
                // Panák neumírá, jen resetujeme HP na max, aby to "vypadalo" nekonečně
                CurrentHealth.Value = 999999;
            }
            else
            {
                Die();
            }
        }
    }

    public void InitializeHealth(int maxHp)
    {
        if (!IsServer) return;
        _maxHealth = maxHp;
        CurrentHealth.Value = maxHp;
    }

    private void Die()
    {
        OnDeath?.Invoke();

        // --- UPRAVENÁ LOGIKA LOOTU ---
        if (IsServer && _lootTable != null && LootManager.Instance != null)
        {
            int dropRolls = 1;       // Kolikrát se pokusíme dropnout item
            float chanceMultiplier = 1.0f; // Zvýšení šance (pro vyšší tiery)

            // Nastavení pravidel podle Tieru
            switch (_currentTier)
            {
                case EnemyTier.Normal:
                    dropRolls = 1;
                    chanceMultiplier = 1.0f;
                    break;
                case EnemyTier.Elite:
                    dropRolls = 2; // Elite zkusí dropnout 2x
                    chanceMultiplier = 1.2f; // +20% šance
                    break;
                case EnemyTier.Champion:
                    dropRolls = 3;
                    chanceMultiplier = 1.5f;
                    break;
                case EnemyTier.Boss:
                    dropRolls = 5; // Boss vyhodí hromadu věcí
                    chanceMultiplier = 10.0f; // Garantovaný drop (pokud base chance není 0)
                    break;
            }

            // Smyčka pro dropování
            for (int i = 0; i < dropRolls; i++)
            {
                // Upravená šance
                float currentChance = _lootChance * chanceMultiplier;

                // Pokud je šance > 1, dropne vždy.
                if (UnityEngine.Random.value < currentChance)
                {
                    // Malý rozptyl pozice, aby itemy nepadly přesně na sebe
                    Vector3 randomOffset = new Vector3(
                        UnityEngine.Random.Range(-0.5f, 0.5f),
                        0.5f,
                        UnityEngine.Random.Range(-0.5f, 0.5f)
                    );

                    LootManager.Instance.SpawnLoot(transform.position + randomOffset, _lootTable);
                }
            }
        }
        // -----------------------------

        if (_rb != null) _rb.isKinematic = true;
        var col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

    public void ApplyKnockback(Vector3 force)
    {
        if (!IsServer || _rb == null || IsInvulnerable) return;
        _rb.AddForce(force, ForceMode.Impulse);
    }

    public void ApplyStatusEffect(StatusEffectData effectData)
    {
        if (!IsServer || IsInvulnerable || _statusReceiver == null) return;

        // Receiver se postará o coroutiny, vizuály i damage ticky
        _statusReceiver.ApplyStatusEffect(effectData);
    }

    // Tuto metodu zavolá AI Controller až skončí animace smrti
    public void DestroySelf()
    {
        if (!IsServer) return;

        // PŮVODNĚ: gameObject.NetDestroy();

        // NOVĚ: Vrátíme objekt do Poolu
        var netObj = GetComponent<NetworkObject>();

        if (netObj != null && netObj.IsSpawned)
        {
            // Parametr 'true' normálně ničí objekt, ale díky našemu Handleru
            // ho to pouze vypne a vrátí do PoolManagera.
            netObj.Despawn(true);
        }
        else
        {
            // Fallback pro případ, že testuješ offline bez sítě
            Destroy(gameObject);
        }
    }
}