using UnityEngine;
using Unity.Netcode;
using System.Collections;
using Unity.VisualScripting.Antlr3.Runtime.Misc;

[RequireComponent(typeof(Rigidbody))]
public class ExplosiveProjectile : NetworkBehaviour
{
    [Header("Visuals")]
    [SerializeField] private GameObject _explosionVFX; // Prefab výbuchu

    private WeaponStats _stats;
    private ulong _ownerId;

    public void Initialize(ulong ownerId, Vector3 velocity, WeaponStats stats)
    {
        _ownerId = ownerId;
        _stats = stats;

        GetComponent<Rigidbody>().linearVelocity = velocity;

        // Spustíme odpočet
        StartCoroutine(ExplodeRoutine());
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

    private IEnumerator ExplodeRoutine()
    {
        yield return new WaitForSeconds(_stats._fuseTime);
        Explode();
    }

    private void Explode()
    {
        if (!IsServer) return;

        // 1. Detekce oblasti výbuchu
        // Použijeme ExplosionRadius ze statů
        float radius = _stats.ExplosionRadius > 0 ? _stats.ExplosionRadius : 3.0f;
        Collider[] hits = Physics.OverlapSphere(transform.position, radius);

        foreach (var hit in hits)
        {
            // Ochrana proti zranění sebe sama (volitelné)
            if (hit.TryGetComponent(out NetworkObject netObj) && netObj.OwnerClientId == _ownerId) continue;

            // A) Nepřátelé
            if (hit.TryGetComponent(out EnemyHealth enemy))
            {
                enemy.TakeDamage(_stats.Damage);

                // Bomba má velký knockback směrem od středu výbuchu
                Vector3 knockDir = (hit.transform.position - transform.position).normalized;
                enemy.ApplyKnockback(knockDir * _stats.Knockback);

                // Aplikace efektů (např. Fire Bomb zapálí)
                if (_stats.Effect.Type != StatusEffectType.None)
                {
                    enemy.ApplyStatusEffect(_stats.Effect);
                }
            }
            // B) Hráči (PvP)
            else if (hit.TryGetComponent(out PlayerAttributes player))
            {
                player.TakeDamageServerRpc(_stats.Damage);
            }
        }

        // 2. Vizuální efekt (ClientRpc)
        SpawnExplosionVFXClientRpc(transform.position, radius);

        // 3. Zničení bomby
        gameObject.NetDestroy();
    }

    [ClientRpc]
    private void SpawnExplosionVFXClientRpc(Vector3 pos, float scale)
    {
        if (_explosionVFX == null) return;

        GameObject vfx = Instantiate(_explosionVFX, pos, Quaternion.identity);
        vfx.transform.localScale = Vector3.one * scale;

        // Oprava: Místo Destroy(vfx, 2.0f) použijeme bezpečnou metodu se zpožděním
        StartCoroutine(DelayedNetDestroy(vfx, 2.0f));
    }

    private IEnumerator DelayedNetDestroy(GameObject obj, float delay)
    {
        yield return new WaitForSeconds(delay);
        // Pokud obj nemá NetworkObject, NetDestroy zavolá standardní Object.Destroy
        // Pokud ho omylem má, klient ho pouze deaktivuje (SetActive)
        obj.NetDestroy();
    }
}