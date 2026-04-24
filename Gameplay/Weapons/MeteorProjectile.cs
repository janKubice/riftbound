using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class MeteorProjectile : NetworkBehaviour
{
    [Header("VFX")]
    [Tooltip("Prefab masivního výbuchu (částice). Musí se umět sám zničit (např. přes PooledVFX nebo Destroy).")]
    [SerializeField] private GameObject _explosionVfxPrefab;

    [Header("Stagnation Detection")]
    [Tooltip("Maximální doba, po kterou může být meteor v klidu, než vynutíme explozi.")]
    [SerializeField] private float _maxStagnationTime = 0.2f;
    [Tooltip("Tolerance rychlosti, která se považuje za 'stání na místě'.")]
    [SerializeField] private float _velocityThreshold = 0.1f;

    private ulong _attackerObjectId;
    private WeaponStats _stats;
    private bool _hasExploded = false;
    private const float COLLISION_GRACE_PERIOD = 0.15f;
    private float _spawnTime;

    private Rigidbody _rb;
    private float _stagnationTimer;

    public void Initialize(ulong attackerId, Vector3 velocity, WeaponStats stats)
    {
        _attackerObjectId = attackerId;
        _stats = stats;

        // Nastavíme rychlost pádu
        _rb = GetComponent<Rigidbody>();
        _rb.useGravity = false; // Řídíme to čistě rychlostí (linearVelocity)
        _rb.linearVelocity = velocity;

        // Záchranná brzda - pokud meteor mine mapu, zničí se po 10s
        if (IsServer)
        {
            _spawnTime = Time.time;
            Invoke(nameof(DestroyMeteor), 10f);
        }
    }

    private void Update()
    {
        if (!IsServer || _hasExploded) return;

        // Kontrola stagnace: Pokud je meteor už venku z grace periody a nehýbe se
        if (Time.time > _spawnTime + COLLISION_GRACE_PERIOD)
        {
            if (_rb.linearVelocity.magnitude < _velocityThreshold)
            {
                _stagnationTimer += Time.deltaTime;
                if (_stagnationTimer >= _maxStagnationTime)
                {
                    Debug.Log($"[MeteorProjectile] Stagnation detected. Forcing explosion.");
                    Explode();
                }
            }
            else
            {
                _stagnationTimer = 0f; // Resetuj timer, pokud se meteor pohnul
            }
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // Spouštíme pouze na serveru a jen jednou
        if (!IsServer || _hasExploded) return;

        Collider other = collision.collider;

        // 1. Ochrana proti okamžitému výbuchu (např. spawnutí uvnitř stropu)
        if (Time.time < _spawnTime + COLLISION_GRACE_PERIOD) return;

        // 2. Ignorovat útočníka (pokud by meteor spadl přímo na hráče, co ho vyvolal)
        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) return;

        // 3. Ignorovat listí (jen vizuál)
        if (other.GetComponent<InteractiveFoliage>() != null) return;

        // 4. Ignorovat ostatní projektily (ochrana před srážkou s letícími firebally)
        if (other.GetComponent<SmartProjectile>() != null || other.GetComponent<ExplosiveProjectile>() != null) return;

        // Pokud narazí na cokoliv pevného (podlaha, stěna, nepřítel, bedna), exploduje
        Explode();
    }

    private void Explode()
    {
        _hasExploded = true;
        CancelInvoke(nameof(DestroyMeteor));

        Vector3 explosionCenter = transform.position;

        // 1. Zjistíme všechny zasažené objekty v masivním rádiu
        Collider[] hits = Physics.OverlapSphere(explosionCenter, _stats.ExplosionRadius);
        HashSet<GameObject> processedObjects = new HashSet<GameObject>();

        foreach (var hit in hits)
        {
            // Ochrana, abychom jednu entitu (s více collidery) nezasáhli víckrát
            GameObject rootObj = hit.transform.root.gameObject;
            if (processedObjects.Contains(rootObj)) continue;
            processedObjects.Add(rootObj);

            // 2. Aplikujeme explozivní poškození na nepřátele
            if (hit.TryGetComponent(out EnemyHealth enemy) || (enemy = hit.GetComponentInParent<EnemyHealth>()))
            {
                // Zde aplikujeme staty z meteoru (např. Knockback poslouží jako síla exploze)
                enemy.TakeExplosiveDamage(
                    amount: _stats.Damage,
                    expCenter: explosionCenter,
                    expForce: _stats.Knockback,
                    expRadius: _stats.ExplosionRadius,
                    attackerId: _attackerObjectId
                );
            }

            // (Volitelně) Zničení beden/destructibles
            if (hit.TryGetComponent(out DestructibleProp prop))
            {
                prop.TakeHit();
            }
        }

        // 3. Spustíme gigantický vizuální efekt u všech klientů
        SpawnExplosionClientRpc(explosionCenter);

        // 4. Úklid
        DestroyMeteor();
    }

    [ClientRpc]
    private void SpawnExplosionClientRpc(Vector3 pos)
    {
        if (_explosionVfxPrefab == null) return;

        // Spawn vizuálního efektu lokálně (šetří síť)
        GameObject vfx = Instantiate(_explosionVfxPrefab, pos, Quaternion.identity);

        // Necháme vizuál škálovat podle rádiusu v WeaponStats, pokud chceš
        // vfx.transform.localScale = Vector3.one * _stats.ExplosionRadius;

        Destroy(vfx, 5f); // Úklid paměti po přehrání částic
    }

    private void DestroyMeteor()
    {
        if (IsServer && NetworkObject != null && NetworkObject.IsSpawned)
        {
            gameObject.NetDestroy(); // Tvá extension metoda, kterou používáš (např. u ExplosiveProjectile)
        }
    }
}