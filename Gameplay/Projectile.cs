using Unity.Netcode;
using UnityEngine;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(NetworkObject))]
public class Projectile : NetworkBehaviour
{
    [SerializeField] private float _speed = 25f;
    [SerializeField] private float _lifeTime = 3f;
    [SerializeField] private int _damage = 10;

    [Header("VFX")]
    [SerializeField] private GameObject _impactVFX;

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Životnost projektilu řídí výhradně server
            StartCoroutine(LifetimeTimer());
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

    private IEnumerator LifetimeTimer()
    {
        yield return new WaitForSeconds(_lifeTime);
        DespawnProjectile();
    }

    public void Launch(Vector3 direction)
    {
        _rb.linearVelocity = direction.normalized * _speed;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer) return;

        // Damage Logic
        if (collision.gameObject.TryGetComponent<EnemyHealth>(out EnemyHealth enemyHealth))
        {
            enemyHealth.TakeDamage(_damage);
        }

        if (collision.gameObject.TryGetComponent<PlayerAttributes>(out PlayerAttributes playerAttr))
        {
            playerAttr.TakeDamageServerRpc(_damage);
        }

        // VFX Broadcast
        if (_impactVFX != null)
        {
            Vector3 hitPos = collision.contacts[0].point;
            Vector3 hitNormal = collision.contacts[0].normal;
            SpawnImpactVFXClientRpc(hitPos, hitNormal);
        }

        DespawnProjectile();
    }

    private void DespawnProjectile()
    {
        gameObject.NetDestroy();
    }

    [ClientRpc]
    private void SpawnImpactVFXClientRpc(Vector3 pos, Vector3 normal)
    {
        // Lokální vizuální efekt bez NetworkObjectu
        Instantiate(_impactVFX, pos, Quaternion.LookRotation(normal));
    }
}