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
    private ulong _attackerObjectId;
    private int _pierceLeft;
    private Vector3 _startPosition;
    private HashSet<GameObject> _hitHistory = new HashSet<GameObject>();
    private Rigidbody _rb;
    [SerializeField] private GameObject _impactVfxPrefab;

    // --- FIX 1: Ochrana proti okamžitému výbuchu (Grace Period) ---
    private float _spawnTime;
    private const float COLLISION_GRACE_PERIOD = 0.05f; // 50ms ignorování kolizí po spawnu

    public void Initialize(NetworkObject attacker, Vector3 direction, WeaponStats stats)
    {
        _attackerObjectId = attacker.NetworkObjectId;
        _stats = stats;
        _pierceLeft = stats.PierceCount;
        _startPosition = transform.position;

        // Zaznamenáme čas vzniku
        _spawnTime = Time.time;

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
        if (Time.time < _spawnTime + COLLISION_GRACE_PERIOD) return;

        // Kontrola vlastníka (aby si hráč nestřílel pod nohy)
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) return;

        // Piercing historie
        if (_hitHistory.Contains(other.gameObject)) return;

        bool destroyProjectile = false;
        bool hitSomethingValid = false;

        // 1. Zásah Foliage (Listí/Tráva) - Projektil NEZASTAVÍ (letí skrz)
        if (other.TryGetComponent(out InteractiveFoliage foliage))
        {
            // Vypočítáme směr letu pro efekt
            foliage.OnHit(transform.forward);
            // Nepřidáváme do hitHistory, aby to mohlo trefit víc listů za sebou
            // destroyProjectile zůstává false
        }

        // 2. Zásah Destructible Prop (Bedna/Sud) - Projektil ZASTAVÍ
        else if (other.TryGetComponent(out DestructibleProp prop))
        {
            prop.TakeHit(); // Voláme destrukci
            hitSomethingValid = true;
            destroyProjectile = true; // Bedna střelu zastaví
        }

        // 3. Zásah Nepřítele
        else if (other.TryGetComponent(out EnemyHealth enemy) || (enemy = other.GetComponentInParent<EnemyHealth>()))
        {
            enemy.TakeDamage(_stats.Damage);
            hitSomethingValid = true;
            destroyProjectile = true;
        }

        // 4. Zásah Hráče (PvP)
        else if (other.TryGetComponent(out PlayerAttributes player) || (player = other.GetComponentInParent<PlayerAttributes>()))
        {
            player.TakeDamageServerRpc(_stats.Damage);
            hitSomethingValid = true;
            destroyProjectile = true;
        }

        // 5. Zásah Zdi (cokoliv co není Trigger a nebylo ošetřeno výše)
        else if (!other.isTrigger)
        {
            destroyProjectile = true;
            hitSomethingValid = true; // Trefili jsme zeď
        }

        // --- VYHODNOCENÍ ---
        if (hitSomethingValid)
        {
            _hitHistory.Add(other.gameObject);

            // Efekt dopadu
            Vector3 hitPos = other.ClosestPoint(transform.position);
            SpawnImpact(hitPos, -transform.forward);

            if (destroyProjectile)
            {
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
    }

    private void SpawnImpact(Vector3 pos, Vector3 normal)
    {
        // Pokud nemáme efekt, končíme
        if (_impactVfxPrefab == null) return;

        // Zavoláme ClientRpc, aby se efekt přehrál u všech hráčů
        SpawnImpactClientRpc(pos, normal);
    }

    [ClientRpc]
    private void SpawnImpactClientRpc(Vector3 pos, Vector3 normal)
    {
        // Vytvoříme efekt lokálně (nemusí mít NetworkObject, je to jen vizuál)
        GameObject vfx = Instantiate(_impactVfxPrefab, pos, Quaternion.LookRotation(normal));

        // Zničíme efekt po 2 sekundách, aby nezaplnil paměť
        Destroy(vfx, 2.0f);
    }

    private void DestroyProjectile()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            gameObject.NetDestroy();
        }
    }
}