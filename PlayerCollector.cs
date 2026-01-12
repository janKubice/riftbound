using UnityEngine;
using Unity.Netcode;

public class PlayerCollector : NetworkBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private float _collectionRadius = 5.0f;
    [SerializeField] private LayerMask _lootLayer; // Nastav v Unity novou vrstvu "Loot"

    private Collider[] _hitBuffer = new Collider[20];
    private PlayerProgression _progression;
    private PlayerAttributes _attributes; // Pro léčení HP/Mana orbů

    private void Awake()
    {
        _progression = GetComponent<PlayerProgression>();
        _attributes = GetComponent<PlayerAttributes>();
    }

    private void FixedUpdate()
    {
        // Sbírá jen vlastník postavy (lokální hráč)
        if (!IsOwner) return;

        DetectLoot();
    }

    private void DetectLoot()
    {
        // Zde můžeme v budoucnu načítat "_collectionRadius" z Progression systému (Pickup Range upgrade)
        float currentRadius = _collectionRadius;
        if (_progression != null) 
        {
             // Příklad: currentRadius *= _progression.GetStatMultiplier(StatType.PickupRange);
        }

        int count = Physics.OverlapSphereNonAlloc(transform.position, currentRadius, _hitBuffer, _lootLayer);

        for (int i = 0; i < count; i++)
        {
            var orb = _hitBuffer[i].GetComponent<CollectableOrb>();
            if (orb != null)
            {
                // Aktivujeme magnetismus orbu směrem k nám
                // Přihlásíme se k eventu, abychom věděli, kdy dorazí
                orb.OnCollected -= HandleOrbCollected; // Prevence double sub
                orb.OnCollected += HandleOrbCollected;
                
                orb.StartMagnet(transform);
            }
        }
    }

    // Volá se, když orba fyzicky doletí do hráče
    private void HandleOrbCollected(LootType type, int amount)
    {
        // Pošleme Serveru zprávu, že jsme něco sebrali
        RequestCollectLootServerRpc(type, amount);
    }

    [ServerRpc]
    private void RequestCollectLootServerRpc(LootType type, int amount)
    {
        // Tady by měla být validace (Anti-cheat): Je možné, že hráč sebral tolik XP?
        // Prozatím věříme klientovi.

        switch (type)
        {
            case LootType.Experience:
                if (_progression != null) _progression.AddXPServer(amount);
                break;
            case LootType.HealthOrb:
                if (_attributes != null) _attributes.Heal(amount);
                break;
            // Další typy...
        }
    }
}