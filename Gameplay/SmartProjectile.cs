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

    // --- FIX 1: Ochrana proti okamžitému výbuchu (Grace Period) ---
    private float _spawnTime;
    private const float COLLISION_GRACE_PERIOD = 0.05f; // 50ms ignorování kolizí po spawnu

    public void Initialize(ulong ownerId, Vector3 direction, WeaponStats stats)
    {
        _ownerClientId = ownerId;
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

        // --- FIX 1: Ignorujeme kolize těsně po spawnu ---
        if (Time.time < _spawnTime + COLLISION_GRACE_PERIOD) return;

        // --- FIX 2: Kontrola Vlastníka (hledáme i v rodičích) ---
        // Používáme GetComponentInParent, abychom našli NetworkObject i na kořeni postavy,
        // pokud jsme trefili child objekt (např. ruku nebo zbraň).
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null)
        {
            // Pokud jsme trefili sami sebe, ignorujeme to
            if (netObj.OwnerClientId == _ownerClientId) return;
        }

        // Kontrola, zda jsme tento objekt už netrefili (pro piercing)
        if (_hitHistory.Contains(other.gameObject)) return;

        bool hitEnemy = false;

        // --- FIX 3: Robustnější detekce zásahu (hledáme i v rodičích) ---
        
        // A) Nepřítel
        if (other.TryGetComponent(out EnemyHealth enemy) || (enemy = other.GetComponentInParent<EnemyHealth>()))
        {
            enemy.TakeDamage(_stats.Damage);
            hitEnemy = true;
        }
        // B) Hráč (PvP)
        else if (other.TryGetComponent(out PlayerAttributes player) || (player = other.GetComponentInParent<PlayerAttributes>()))
        {
            player.TakeDamageServerRpc(_stats.Damage);
            hitEnemy = true;
        }
        // C) Prostředí (Zeď/Podlaha) - pokud to není trigger
        else if (!other.isTrigger)
        {
            SpawnImpact(transform.position, -transform.forward);
            DestroyProjectile();
            return;
        }

        // Pokud jsme někoho trefili
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
        // Zde můžete doplnit logiku pro spawn efektů (ClientRpc)
        // Např. NetworkManager.Singleton.SpawnManager... nebo vaše vlastní VFX řešení
    }

    private void DestroyProjectile()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            gameObject.NetDestroy();
        }
    }
}