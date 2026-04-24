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
    protected HashSet<GameObject> _hitHistory = new HashSet<GameObject>();    
    protected Rigidbody _rb;
    
    [SerializeField] private GameObject _impactVfxPrefab;
    protected List<HitEffect> _payload = new List<HitEffect>();

    // Ochrana proti okamžitému výbuchu (Grace Period) ---
    private float _spawnTime;
    private const float COLLISION_GRACE_PERIOD = 0.05f; // 50ms ignorování kolizí po spawnu
    public HashSet<GameObject> HitHistory => _hitHistory;

    public virtual void Initialize(NetworkObject attacker, Vector3 direction, WeaponStats stats, List<HitEffect> payload = null, HashSet<GameObject> passedHitHistory = null)
    {
        _attackerObjectId = attacker.NetworkObjectId;
        _stats = stats;
        _pierceLeft = stats.PierceCount;
        _startPosition = transform.position;

        if (payload != null)
        {
            _payload = new List<HitEffect>(payload);
        }
        else if (stats.OnHitEffects != null)
        {
            _payload = new List<HitEffect>(stats.OnHitEffects);
        }

        // Přenos historie z předchozího projektilu (Ricochet)
        if (passedHitHistory != null)
        {
            _hitHistory = new HashSet<GameObject>(passedHitHistory);
        }

        _spawnTime = Time.time;

        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false;
        _rb.linearVelocity = direction.normalized * stats.ProjectileSpeed;
    }

    public override void OnDestroy()
    {
        // 1. Ochrana proti Memory Leaku ze ScriptableObjects
        if (_payload != null)
        {
            foreach (var effect in _payload)
            {
                // Pokud byl efekt naklonován za běhu (např. Ricochet s nižším MaxBounces)
                if (effect != null && effect.name.Contains("(Clone)"))
                {
                    Destroy(effect); // Bezpečně odstraní SO z paměti
                }
            }
            _payload.Clear();
        }

        // 2. Kontrola sítě
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

        if (Vector3.SqrMagnitude(_startPosition - transform.position) >= _stats.Range * _stats.Range)
        {
            DestroyProjectile();
        }
    }

    protected virtual void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;
        if (Time.time < _spawnTime + COLLISION_GRACE_PERIOD) return;
        if (_hitHistory.Contains(other.gameObject)) return;

        // Foliage reakce (nepoškozuje, ale projde skrz a spustí animaci)
        if (other.TryGetComponent(out InteractiveFoliage foliage))
        {
            foliage.OnHit(transform.forward);
            return; 
        }

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) return; // Ignorovat kolize s vlastní sítí (např. zbraní, která může mít kolider)
        
        bool destroyProjectile = false;
        bool forceDestroy = false; 
        bool hitSomethingValid = false;

        Vector3 hitPos = GetSafeHitPosition(other);

        // 2. Destructible Prop
        if (other.TryGetComponent(out DestructibleProp prop))
        {
            prop.TakeHit();
            hitSomethingValid = true;
            destroyProjectile = true;
            ExecutePayload(other.gameObject, hitPos);
        }
        // 3. Enemy
        else if (other.TryGetComponent(out EnemyHealth enemy) || (enemy = other.GetComponentInParent<EnemyHealth>()))
        {
            enemy.TakeDamage(_stats.Damage, _attackerObjectId);
            ExecutePayload(other.gameObject, hitPos);
            hitSomethingValid = true;
            destroyProjectile = true;
        }
        // 4. Player (PvP)
        else if (other.TryGetComponent(out PlayerAttributes player) || (player = other.GetComponentInParent<PlayerAttributes>()))
        {
            player.TakeDamageServerRpc(_stats.Damage, _attackerObjectId);
            ExecutePayload(other.gameObject, hitPos);
            hitSomethingValid = true;
            destroyProjectile = true;
        }
        // 5. Zeď / Podlaha
        else if (!other.isTrigger)
        {
            ExecutePayload(other.gameObject, hitPos);
            hitSomethingValid = true;
            destroyProjectile = true;
            forceDestroy = true; 
        }

        // --- VYHODNOCENÍ ---
        if (hitSomethingValid)
        {
            _hitHistory.Add(other.gameObject);
            SpawnImpact(hitPos, -transform.forward);

            if (destroyProjectile)
            {
                if (_pierceLeft > 0 && !forceDestroy)
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

    protected void ExecutePayload(GameObject target, Vector3 hitPosition)
    {
        // KASKÁDOVÁNÍ: Pokud nemáme efekty, končíme
        if (_payload == null || _payload.Count == 0) return;

        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(_attackerObjectId, out NetworkObject attackerObj))
        {
            WeaponManager wm = attackerObj.GetComponent<WeaponManager>();

            // 1. Vezmeme POUZE PRVNÍ efekt na řadě
            HitEffect activeEffect = _payload[0];

            // 2. Vytvoříme zbytek fronty (vše kromě prvního efektu)
            List<HitEffect> remainingPayload = new List<HitEffect>();
            for (int i = 1; i < _payload.Count; i++)
            {
                remainingPayload.Add(_payload[i]);
            }

            // 3. Spustíme aktivní efekt a předáme mu zbytek batohu
            if (activeEffect != null)
            {
                activeEffect.OnHit(hitPosition, target, attackerObj, wm, remainingPayload);
            }
        }
    }

    private Vector3 GetSafeHitPosition(Collider other)
    {
        if (other is MeshCollider meshCollider && !meshCollider.convex)
        {
            return transform.position;
        }
        return other.ClosestPoint(transform.position);
    }

    public void AddIgnoredTarget(GameObject target)
    {
        if (target != null)
        {
            _hitHistory.Add(target);
        }
    }

    private void SpawnImpact(Vector3 pos, Vector3 normal)
    {
        if (_impactVfxPrefab == null) return;
        SpawnImpactClientRpc(pos, normal);
    }

    [ClientRpc]
    private void SpawnImpactClientRpc(Vector3 pos, Vector3 normal)
    {
        GameObject vfx = Instantiate(_impactVfxPrefab, pos, Quaternion.LookRotation(normal));
        Destroy(vfx, 2.0f);
    }

    protected void DestroyProjectile()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            gameObject.NetDestroy();
        }
    }
}