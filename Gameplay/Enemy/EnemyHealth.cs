using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System;

public class EnemyHealth : NetworkBehaviour
{
    [SerializeField] private int _maxHealth = 30;
    private StatusEffectReceiver _statusReceiver;
    public NetworkVariable<int> CurrentHealth = new NetworkVariable<int>(30);
    private Rigidbody _rb;

    // Flag pro nesmrtelnost (Spawn fáze)
    public bool IsInvulnerable { get; set; } = false;

    // Event pro AI Controller (aby věděl, že má zahrát animaci)
    public event Action OnDeath;
    public event Action<int> OnDamageTaken;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _statusReceiver = GetComponent<StatusEffectReceiver>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            CurrentHealth.Value = _maxHealth;
        }
    }

    public void TakeDamage(int amount, ulong attackerId = 9999) // attackerId připraveno pro skóre
    {
        if (!IsServer || IsInvulnerable || CurrentHealth.Value <= 0) return;

        CurrentHealth.Value -= amount;
        OnDamageTaken?.Invoke(amount);

        if (CurrentHealth.Value <= 0)
        {
            Die();
        }
    }

    private void Die()
    {
        // Nezničíme objekt hned, ale řekneme Controlleru "Jsem mrtvý"
        OnDeath?.Invoke();

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