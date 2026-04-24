using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class ParasiteController : MonoBehaviour
{
    private NetworkObject _attacker;
    private WeaponManager _manager;
    private List<HitEffect> _payload;
    
    private float _duration;
    private float _tickInterval;
    private int _tickDamage;

    private float _timer;
    private float _lifeTimer;
    private EnemyHealth _health;
    private GameObject _visualInstance;

    // Inicializace od HitEffectu
    public void Initialize(NetworkObject attacker, WeaponManager manager, List<HitEffect> payload, float duration, float tickInterval, int damage, GameObject visualPrefab)
    {
        _attacker = attacker;
        _manager = manager;
        // DŮLEŽITÉ: Musíme si batoh zkopírovat, aby se neztratil
        _payload = new List<HitEffect>(payload); 
        _duration = duration;
        _tickInterval = tickInterval;
        _tickDamage = damage;
        _health = GetComponent<EnemyHealth>();

        // Spawn vizuálu (např. zelené bubliny) přímo na těle nepřítele
        if (visualPrefab != null)
        {
            _visualInstance = Instantiate(visualPrefab, transform.position + Vector3.up * 1f, Quaternion.identity, transform);
        }
    }

    private void Update()
    {
        // Parazit tiká pouze na serveru
        if (!NetworkManager.Singleton.IsServer) return;

        // Pokud hostitel zemřel, nebo vypršel čas, parazit umírá
        _lifeTimer += Time.deltaTime;
        if (_lifeTimer >= _duration || _health == null || _health.CurrentHealth.Value <= 0)
        {
            RemoveParasite();
            return;
        }

        // Tikání (DoT a odpalování Payloadu)
        _timer += Time.deltaTime;
        if (_timer >= _tickInterval)
        {
            _timer = 0f;
            ExecuteTick();
        }
    }

    private void ExecuteTick()
    {
        // 1. Zraníme hostitele (DoT)
        if (_tickDamage > 0)
        {
            ulong attackerId = _attacker != null ? _attacker.OwnerClientId : 0;
            _health.TakeDamage(_tickDamage, attackerId);
        }

        // 2. KASKÁDOVÁNÍ (Odpálíme z něj náš batoh!)
        if (_payload.Count > 0 && _attacker != null)
        {
            HitEffect activeEffect = _payload[0];
            
            List<HitEffect> nextPayload = new List<HitEffect>();
            for (int i = 1; i < _payload.Count; i++) nextPayload.Add(_payload[i]);

            // Efekt vyletí přímo ze středu hostitele
            Vector3 firePos = transform.position + Vector3.up * 1f;

            if (activeEffect != null)
            {
                activeEffect.OnHit(firePos, gameObject, _attacker, _manager, nextPayload);
            }
        }
    }

    private void RemoveParasite()
    {
        if (_visualInstance != null)
        {
            Destroy(_visualInstance);
        }
        Destroy(this); // Zničí jen tuto komponentu, ne nepřítele
    }
}