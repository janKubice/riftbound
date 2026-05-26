using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;
using System.Collections;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(NetworkObject))]
public class SmartProjectile : NetworkBehaviour
{
    public enum ImpactBehavior
    {
        SingleDamage_SingleStatus, // Výchozí (poškodí a dá status 1 cíli)
        SingleDamage_AreaStatus,   // Poškodí 1 cíl, ale status dá všem v okolí
        AreaDamage_AreaStatus      // Exploze (poškodí a dá status všem v okolí)
    }

    [Header("Impact Settings")]
    [Tooltip("Chování při zásahu (plošné vs single).")]
    [SerializeField] private ImpactBehavior _impactBehavior = ImpactBehavior.SingleDamage_SingleStatus;

    [Tooltip("Plošný dosah exploze. (Použije se jen pokud nemá zbraň specifikovaný _stats.ExplosionRadius)")]
    [SerializeField] private float _fallbackAreaRadius = 3f;

    // Buffer pro optimalizovaný raycasting (zamezení alokace paměti během hry)
    private static readonly Collider[] _hitBuffer = new Collider[50];

    protected NetworkObject _attackerObj;
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

    private enum ShooterTeam
    {
        Unknown,
        Player,
        Enemy
    }

    private ShooterTeam _shooterTeam = ShooterTeam.Unknown;

    // Toto je ID klienta pro credit/damage multipliery.
    // Nepoužívej k tomu NetworkObjectId, protože EnemyHealth u tebe čeká spíše clientId hráče.
    private ulong _damageCreditClientId = ulong.MaxValue;

    public virtual void Initialize(NetworkObject attacker, Vector3 direction, WeaponStats stats, List<HitEffect> payload = null, HashSet<GameObject> passedHitHistory = null)
    {
        _attackerObjectId = attacker.NetworkObjectId;
        _stats = stats;
        _pierceLeft = stats.PierceCount;
        _startPosition = transform.position;
        _attackerObj = attacker;
        _attackerObjectId = attacker.NetworkObjectId;

        _shooterTeam = ResolveShooterTeam(attacker);

        // Pokud střílí hráč, chceme do EnemyHealth poslat OwnerClientId,
        // aby fungovaly player bonusy, statistiky, damage multipliery atd.
        // Pokud střílí enemy, neposílej OwnerClientId serveru, protože host player má často clientId 0
        // a enemy by pak host hráči omylem nedával damage kvůli self-damage kontrole.
        _damageCreditClientId = _shooterTeam == ShooterTeam.Player
            ? attacker.OwnerClientId
            : ulong.MaxValue;

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

        // Foliage reakce (nepoškozuje, ale projde skrz a spustí animaci)
        if (other.TryGetComponent(out InteractiveFoliage foliage))
        {
            foliage.OnHit(transform.forward);
            return;
        }

        if (other.GetComponentInParent<SmartProjectile>() != null)
            return;

        GameObject hitKey = ResolveHitHistoryKey(other);
        if (hitKey != null && _hitHistory.Contains(hitKey))
            return;

        var netObj = other.GetComponentInParent<NetworkObject>();
        if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) return; // Ignorovat kolize s vlastní sítí (např. zbraní, která může mít kolider)

        bool destroyProjectile = false;
        bool forceDestroy = false;
        bool hitSomethingValid = false;

        Vector3 hitPos = GetSafeHitPosition(other);

        // --- ZJISTĚNÍ TYPU CÍLE ---
        bool isEnemy = other.GetComponentInParent<EnemyHealth>() != null || other.GetComponentInParent<PlayerAttributes>() != null;
        bool isProp = other.GetComponentInParent<DestructibleProp>() != null;
        bool isWall = !other.isTrigger && !isEnemy && !isProp;

        // --- ZPRACOVÁNÍ ZÁSAHU ---
        // Pokud jsme trefili cokoliv validního (zeď, prop nebo entitu)
        if (isEnemy || isProp || isWall)
        {
            // Předáme collider a pozici k vyhodnocení (zde se řeší Area i Single dmg/status)
            ProcessImpact(other, hitPos);

            hitSomethingValid = true;
            destroyProjectile = true;

            // O zeď se projektil vždy rozbije i přes jakýkoliv počet proražení (PierceCount)
            if (isWall)
            {
                forceDestroy = true;
            }
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

    protected GameObject ResolveHitHistoryKey(Collider col)
    {
        if (col == null)
            return null;

        EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
            return enemy.gameObject;

        PlayerAttributes player = col.GetComponentInParent<PlayerAttributes>();
        if (player != null)
            return player.gameObject;

        DestructibleProp prop = col.GetComponentInParent<DestructibleProp>();
        if (prop != null)
            return prop.gameObject;

        NetworkObject netObj = col.GetComponentInParent<NetworkObject>();
        if (netObj != null)
            return netObj.gameObject;

        return col.gameObject;
    }

    private void ProcessImpact(Collider directHitCollider, Vector3 hitPos)
    {
        // 1. ZÁKLADNÍ CHOVÁNÍ (Single Target)
        if (_impactBehavior == ImpactBehavior.SingleDamage_SingleStatus)
        {
            if (directHitCollider.TryGetComponent(out DestructibleProp prop))
            {
                prop.TakeHit();
                ExecutePayload(prop.gameObject, hitPos);
            }
            else if (directHitCollider.GetComponentInParent<EnemyHealth>() != null || directHitCollider.GetComponentInParent<PlayerAttributes>() != null)
            {
                if (CombatTargeting.TryDamage(directHitCollider, _attackerObj, _stats.Damage, out GameObject damagedTarget))
                {
                    // PŘIDÁNO: Status pro Single Cíl
                    if (_stats.Effect != null && damagedTarget.TryGetComponent(out EnemyHealth enemy))
                    {
                        enemy.ApplyStatusEffect(_stats.Effect);
                    }
                    ExecutePayload(damagedTarget, hitPos);
                }
            }
            else
            {
                ExecutePayload(directHitCollider.gameObject, hitPos); // Zeď
            }
            return;
        }

        // 2. PLOŠNÉ CHOVÁNÍ (Area)
        float radius = _stats.ExplosionRadius > 0 ? _stats.ExplosionRadius : _fallbackAreaRadius;
        int hitCount = Physics.OverlapSphereNonAlloc(hitPos, radius, _hitBuffer);

        HashSet<GameObject> processedEntities = new HashSet<GameObject>();

        // Zpracování Direct Hitu (pro SingleDamage_AreaStatus chceme DMG jen do direct cíle)
        if (_impactBehavior == ImpactBehavior.SingleDamage_AreaStatus)
        {
            if (directHitCollider.TryGetComponent(out DestructibleProp prop))
            {
                prop.TakeHit();
                ExecutePayload(prop.gameObject, hitPos);
                processedEntities.Add(prop.gameObject);
            }
            else if (CombatTargeting.TryDamage(directHitCollider, _attackerObj, _stats.Damage, out GameObject directTarget))
            {
                // PŘIDÁNO: Status pro Direct HIt v plošném módu
                if (_stats.Effect != null && directTarget.TryGetComponent(out EnemyHealth enemy))
                {
                    enemy.ApplyStatusEffect(_stats.Effect);
                }
                ExecutePayload(directTarget, hitPos);
                processedEntities.Add(directTarget);
            }
        }

        // Zpracování okolí (aplikuje se na všechny validní cíle v rádiusu)
        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitBuffer[i];

            if (col.isTrigger) continue; // Ignorujeme pomocné triggery

            // Ignorujeme případně vlastní objekt (střelce)
            var netObj = col.GetComponentInParent<NetworkObject>();
            if (netObj != null && netObj.NetworkObjectId == _attackerObjectId) continue;

            GameObject targetEntity = null;
            var enemy = col.GetComponentInParent<EnemyHealth>();
            var player = col.GetComponentInParent<PlayerAttributes>();
            var dProp = col.GetComponentInParent<DestructibleProp>();

            if (enemy != null) targetEntity = enemy.gameObject;
            else if (player != null) targetEntity = player.gameObject;
            else if (dProp != null) targetEntity = dProp.gameObject;

            // Zabrání vícenásobnému poškození
            if (targetEntity == null || processedEntities.Contains(targetEntity)) continue;

            // Vyhodnocení podle chování plošného útoku
            if (_impactBehavior == ImpactBehavior.AreaDamage_AreaStatus)
            {
                // Plošný DMG + Status
                if (dProp != null)
                {
                    dProp.TakeHit();
                    ExecutePayload(targetEntity, col.ClosestPoint(hitPos));
                    processedEntities.Add(targetEntity);
                }
                else if (CombatTargeting.TryDamage(col, _attackerObj, _stats.Damage, out GameObject damagedTarget))
                {
                    if (_stats.Effect != null && damagedTarget.TryGetComponent(out EnemyHealth damagedEnemy))
                    {
                        damagedEnemy.ApplyStatusEffect(_stats.Effect);
                    }
                    ExecutePayload(damagedTarget, col.ClosestPoint(hitPos));
                    processedEntities.Add(damagedTarget);
                }
            }
            else if (_impactBehavior == ImpactBehavior.SingleDamage_AreaStatus)
            {
                // Plošný Status POUZE 
                if (dProp != null || (enemy != null && CanDamageEnemy(enemy)) || (player != null && CanDamagePlayer(player)))
                {
                    // PŘIDÁNO: Status plošně (bez udělení poškození)
                    if (_stats.Effect != null && enemy != null && CanDamageEnemy(enemy))
                    {
                        enemy.ApplyStatusEffect(_stats.Effect);
                    }
                    ExecutePayload(targetEntity, col.ClosestPoint(hitPos));
                    processedEntities.Add(targetEntity);
                }
            }
        }

        // 3. Vyčištění Bufferu po dokončení práce (zamezení leakům)
        System.Array.Clear(_hitBuffer, 0, hitCount);
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

    private ShooterTeam ResolveShooterTeam(NetworkObject attacker)
    {
        if (attacker == null)
            return ShooterTeam.Unknown;

        GameObject attackerGO = attacker.gameObject;

        // Nejrychlejší varianta přes tag
        if (attackerGO.CompareTag("Player"))
            return ShooterTeam.Player;

        if (attackerGO.CompareTag("Enemy"))
            return ShooterTeam.Enemy;

        // Fallback přes komponenty, kdybys někde zapomněl tag
        if (attackerGO.GetComponentInParent<PlayerAttributes>() != null)
            return ShooterTeam.Player;

        if (attackerGO.GetComponentInParent<EnemyHealth>() != null ||
            attackerGO.GetComponentInParent<EnemyBaseAI>() != null)
            return ShooterTeam.Enemy;

        Debug.LogWarning($"[SmartProjectile] Shooter team could not be resolved for {attackerGO.name}. Projectile will not damage Player/Enemy targets.");
        return ShooterTeam.Unknown;
    }

    protected bool CanDamageEnemy(EnemyHealth enemy)
    {
        if (enemy == null)
            return false;

        return _shooterTeam == ShooterTeam.Player;
    }

    protected bool CanDamagePlayer(PlayerAttributes player)
    {
        if (player == null)
            return false;

        return _shooterTeam == ShooterTeam.Enemy;
    }

    protected bool CanUseAsHomingTarget(Collider col)
    {
        if (col == null)
            return false;

        EnemyHealth enemy = col.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
            return CanDamageEnemy(enemy);

        PlayerAttributes player = col.GetComponentInParent<PlayerAttributes>();
        if (player != null)
            return CanDamagePlayer(player);

        return false;
    }
}