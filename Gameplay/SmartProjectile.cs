using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class SmartProjectile : NetworkBehaviour
{
    private WeaponStats _stats;
    private ulong _ownerClientId;
    private int _pierceLeft;
    private Vector3 _startPosition;
    private HashSet<GameObject> _hitHistory = new HashSet<GameObject>();
    private Rigidbody _rb;

    public void Initialize(ulong ownerId, Vector3 direction, WeaponStats stats)
    {
        _ownerClientId = ownerId;
        _stats = stats;
        _pierceLeft = stats.PierceCount;
        _startPosition = transform.position;

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.linearVelocity = direction.normalized * stats.ProjectileSpeed;
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

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Serverová pojistka pro automatické zničení po čase
            StartCoroutine(LifetimeLimit());
        }
    }

    private IEnumerator LifetimeLimit()
    {
        yield return new WaitForSeconds(5.0f);
        DestroyProjectile();
    }

    private void FixedUpdate()
    {
        if (!IsServer) return;

        // Kontrola dostřelu
        if (Vector3.SqrMagnitude(_startPosition - transform.position) >= _stats.Range * _stats.Range)
        {
            DestroyProjectile();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        if (other.TryGetComponent(out NetworkObject netObj))
        {
            if (netObj.OwnerClientId == _ownerClientId) return;
        }

        if (_hitHistory.Contains(other.gameObject)) return;

        bool hitEnemy = false;

        if (other.TryGetComponent(out EnemyHealth enemy))
        {
            enemy.TakeDamage(_stats.Damage);
            hitEnemy = true;
        }
        else if (other.TryGetComponent(out PlayerAttributes player))
        {
            player.TakeDamageServerRpc(_stats.Damage);
            hitEnemy = true;
        }
        else if (!other.isTrigger)
        {
            SpawnImpact(transform.position, -transform.forward);
            DestroyProjectile();
            return;
        }

        if (hitEnemy)
        {
            _hitHistory.Add(other.gameObject);
            SpawnImpact(other.ClosestPoint(transform.position), -transform.forward);

            if (_pierceLeft > 0)
            {
                _pierceLeft--;
            }
            else
            {
                DestroyProjectile();
            }
        }
    }

    private void SpawnImpact(Vector3 pos, Vector3 normal)
    {
        // Zde implementujte ClientRpc pro VFX
    }

    private void DestroyProjectile()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            gameObject.NetDestroy();
        }
    }
}