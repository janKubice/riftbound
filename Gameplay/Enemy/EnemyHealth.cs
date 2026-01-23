using UnityEngine;
using Unity.Netcode;
using System;

public class EnemyHealth : NetworkBehaviour
{
    [SerializeField] private int _maxHealth = 30;
    [SerializeField] private bool _isTrainingDummy = false;
    private StatusEffectReceiver _statusReceiver;
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(30);
    private Rigidbody _rb;

    // Flag pro nesmrtelnost (Spawn fáze)
    public bool IsInvulnerable { get; set; } = false;

    // Event pro AI Controller (aby věděl, že má zahrát animaci)
    public event Action OnDeath;
    public event Action<int> OnDamageTaken;

    [Header("Loot")]
    [SerializeField] private LootTable _lootTable;
    [Range(0f, 1f)][SerializeField] private float _lootChance = 0.3f;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _statusReceiver = GetComponent<StatusEffectReceiver>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Pokud je to panák, dáme mu hodně HP, aby se UI slider nezbláznil
            CurrentHealth.Value = _isTrainingDummy ? 999999 : _maxHealth;
        }
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
        // Nezničíme objekt hned, ale řekneme Controlleru "Jsem mrtvý"
        OnDeath?.Invoke();

        if (_lootTable != null && LootManager.Instance != null)
        {
            // Náhoda na drop (pokud není v tabulce 100%)
            if (UnityEngine.Random.value < _lootChance)
            {
                LootManager.Instance.SpawnLoot(transform.position + Vector3.up * 0.5f, _lootTable);
            }
        }

        // Vypneme kolize a fyziku, aby do něj hráči nekopali během animace
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
        if (IsServer) gameObject.NetDestroy();
    }
}