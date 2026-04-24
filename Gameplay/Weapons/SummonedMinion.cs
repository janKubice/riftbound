using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(NetworkObject))]
public class SummonedMinion : NetworkBehaviour
{
    // Statická kolekce pro rychlé zjištění počtu minionů na serveru bez hledání v hierarchii
    public static readonly List<SummonedMinion> ActiveMinions = new List<SummonedMinion>();

    public ulong OwnerId { get; private set; }
    
    [Header("Stats")]
    public float Lifetime = 15f;
    public float AttackRadius = 3f;
    public int Damage = 10;
    public float AttackCooldown = 1f;

    private float _lastAttackTime;
    private Collider[] _hitColliders = new Collider[10]; // Prevence alokace při hledání cílů

    public void Initialize(ulong ownerClientId)
    {
        OwnerId = ownerClientId;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            ActiveMinions.Add(this);
            Invoke(nameof(DespawnMinion), Lifetime);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            ActiveMinions.Remove(this);
        }
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Jednoduchá útočná logika
        if (Time.time >= _lastAttackTime + AttackCooldown)
        {
            PerformAoEAttack();
        }
    }

    private void PerformAoEAttack()
    {
        // Optimalizované hledání kolizí bez GC.Alloc
        int hitCount = Physics.OverlapSphereNonAlloc(transform.position, AttackRadius, _hitColliders);
        bool attacked = false;

        for (int i = 0; i < hitCount; i++)
        {
            if (_hitColliders[i].TryGetComponent(out EnemyHealth enemy) || 
               (_hitColliders[i].transform.parent != null && _hitColliders[i].transform.parent.TryGetComponent(out enemy)))
            {
                enemy.TakeDamage(Damage, OwnerId);
                attacked = true;
            }
        }

        if (attacked)
        {
            _lastAttackTime = Time.time;
        }
    }

    private void DespawnMinion()
    {
        if (NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }
}