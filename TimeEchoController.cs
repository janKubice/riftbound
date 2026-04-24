using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class TimeEchoController : NetworkBehaviour
{
    private NetworkObject _attacker;
    private WeaponManager _manager;
    private List<HitEffect> _payload;
    
    private float _duration;
    private float _tickInterval;
    
    private float _lifeTimer;
    private float _tickTimer;

    public void Initialize(NetworkObject attacker, WeaponManager manager, List<HitEffect> payload, float duration, float tickInterval)
    {
        _attacker = attacker;
        _manager = manager;
        // Zkopírujeme batoh
        _payload = new List<HitEffect>(payload);
        _duration = duration;
        _tickInterval = tickInterval;
        
        // Záměrně nastavíme tickTimer na maximum, aby ozvěna odpálila první útok OKAMŽITĚ po spawnu
        _tickTimer = tickInterval; 
    }

    private void Update()
    {
        if (!IsServer) return;

        // Kontrola životnosti ozvěny
        _lifeTimer += Time.deltaTime;
        if (_lifeTimer >= _duration)
        {
            if (NetworkObject.IsSpawned) NetworkObject.Despawn(true);
            return;
        }

        // Tikání a odpalování batohu
        _tickTimer += Time.deltaTime;
        if (_tickTimer >= _tickInterval)
        {
            _tickTimer = 0f;
            ExecuteTick();
        }
    }

    private void ExecuteTick()
    {
        // Pokud nemáme co pálit, nebo hráč (útočník) se mezitím odpojil, končíme
        if (_payload.Count > 0 && _attacker != null)
        {
            HitEffect activeEffect = _payload[0];
            
            List<HitEffect> nextPayload = new List<HitEffect>();
            for (int i = 1; i < _payload.Count; i++) nextPayload.Add(_payload[i]);

            if (activeEffect != null)
            {
                // Odpalujeme z pozice této ozvěny. Jako "target" předáme sami sebe, 
                // aby efekty (jako Split) věděly, odkud se mají rozletět.
                activeEffect.OnHit(transform.position, gameObject, _attacker, _manager, nextPayload);
            }
        }
    }
}