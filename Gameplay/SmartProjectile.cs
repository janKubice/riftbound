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
    protected ulong _attackerObjectId;
    private int _pierceLeft;
    private Vector3 _startPosition;
    private HashSet<GameObject> _hitHistory = new HashSet<GameObject>();
    protected Rigidbody _rb;
    [SerializeField] private GameObject _impactVfxPrefab;
    protected List<HitEffect> _payload = new List<HitEffect>();

    // --- FIX 1: Ochrana proti okamžitému výbuchu (Grace Period) ---
    private float _spawnTime;
    private const float COLLISION_GRACE_PERIOD = 0.05f; // 50ms ignorování kolizí po spawnu

    public void Initialize(NetworkObject attacker, Vector3 direction, WeaponStats stats, List<HitEffect> payload = null)
    {
        _attackerObjectId = attacker.NetworkObjectId;
        _stats = stats;
        _pierceLeft = stats.PierceCount;
        _startPosition = transform.position;

        if (payload != null)
        {
            _payload = new List<HitEffect>(payload); // Vytvoříme kopii
        }
        else if (stats.OnHitEffects != null)
        {
            _payload = new List<HitEffect>(stats.OnHitEffects); // Kopie ze zbraně
        }

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

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) return;

        if (_hitHistory.Contains(other.gameObject)) return;

        bool destroyProjectile = false;
        bool hitSomethingValid = false;

        // 1. Foliage (jen vizuál, payload nespouštíme)
        if (other.TryGetComponent(out InteractiveFoliage foliage))
        {
            foliage.OnHit(transform.forward);
        }

        // 2. Destructible Prop
        else if (other.TryGetComponent(out DestructibleProp prop))
        {
            prop.TakeHit();
            hitSomethingValid = true;
            destroyProjectile = true;

            // I na bednu můžeme aplikovat efekty (např. exploze ji zničí víc)
            ExecutePayload(other.gameObject, transform.position);
        }

        // 3. Enemy
        else if (other.TryGetComponent(out EnemyHealth enemy) || (enemy = other.GetComponentInParent<EnemyHealth>()))
        {
            // A) Základní poškození (ze statistik zbraně)
            enemy.TakeDamage(_stats.Damage, _attackerObjectId);

            // B) Spuštění PROC efektů (Chain Lightning, atd.)
            ExecutePayload(other.gameObject, other.ClosestPoint(transform.position));

            hitSomethingValid = true;
            destroyProjectile = true;
        }

        // 4. Player (PvP)
        else if (other.TryGetComponent(out PlayerAttributes player) || (player = other.GetComponentInParent<PlayerAttributes>()))
        {
            player.TakeDamageServerRpc(_stats.Damage);

            // B) Spuštění efektů i na hráče
            ExecutePayload(other.gameObject, other.ClosestPoint(transform.position));

            hitSomethingValid = true;
            destroyProjectile = true;
        }

        // 5. Zeď / Podlaha
        else if (!other.isTrigger)
        {
            // I do zdi můžeme pustit efekt (např. exploze, která zraní okolí)
            ExecutePayload(other.gameObject, other.ClosestPoint(transform.position));

            destroyProjectile = true;
            hitSomethingValid = true;
        }

        // --- VYHODNOCENÍ ---
        if (hitSomethingValid)
        {
            _hitHistory.Add(other.gameObject);

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

    // --- NOVÁ METODA PRO SPUŠTĚNÍ EFEKTŮ ---
    protected void ExecutePayload(GameObject target, Vector3 hitPosition)
    {
        // Pokud nemáme žádné efekty, končíme
        if (_payload == null || _payload.Count == 0) return;

        // Potřebujeme referenci na útočníka a jeho WeaponManager
        // Najdeme NetworkObject útočníka podle ID
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(_attackerObjectId, out NetworkObject attackerObj))
        {
            // Získáme WeaponManager (aby efekty mohly volat další útoky)
            WeaponManager wm = attackerObj.GetComponent<WeaponManager>();

            // Projdeme všechny efekty v batohu a odpálíme je
            foreach (var effect in _payload)
            {
                if (effect != null)
                {
                    effect.OnHit(hitPosition, target, attackerObj, wm);
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