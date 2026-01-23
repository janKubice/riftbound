using UnityEngine;
using Unity.Netcode;

public class PlayerCollector : NetworkBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private float _baseCollectionRadius = 5.0f;
    [SerializeField] private LayerMask _lootLayer; // Nezapomeň nastavit Layer "Loot"!

    private Collider[] _hitBuffer = new Collider[50];
    private PlayerAttributes _attributes; // Předpokládám, že XP máš v Attributes nebo Progression
    private PlayerProgression _progression;
    private void Awake()
    {
        _attributes = GetComponent<PlayerAttributes>();
        _progression = GetComponent<PlayerProgression>();
    }

    private void FixedUpdate()
    {
        // Loot sbírá jen ten, kdo hraje za tuto postavu
        if (!IsOwner) return;

        DetectLoot();
    }

    private void DetectLoot()
    {
        // Zde můžeš později přidat multiplikátor z talentů
        float currentRadius = _baseCollectionRadius; 

        int count = Physics.OverlapSphereNonAlloc(transform.position, currentRadius, _hitBuffer, _lootLayer);

        for (int i = 0; i < count; i++)
        {
            // Získáme orb. Protože je to lokální objekt, GetComponent je rychlý.
            if (_hitBuffer[i].TryGetComponent(out CollectableOrb orb))
            {
                // Pokud ještě není magnetizovaný, přitáhneme ho
                if (!orb.IsMagnetized)
                {
                    orb.StartMagnet(this);
                }
            }
        }
    }

    // Voláno z CollectableOrb, když doletí do hráče
    public void OnOrbCollectedLocal(LootType type, int amount)
    {
        // Pošleme validaci na server
        RequestCollectLootServerRpc(type, amount);
        
        // Vizuální feedback (zvuk cinknutí) můžeš přehrát hned tady lokálně
    }

    [ServerRpc]
    private void RequestCollectLootServerRpc(LootType type, int amount)
    {
        // Server autoritativně přičte hodnoty
        if (_attributes == null) return;

        switch (type)
        {
            case LootType.Experience:
                _progression.AddXPServer(amount);
                break;
            case LootType.Gold:
                _progression.AddGold(amount);
                break;
            case LootType.HealthPotion: 
                _attributes.Heal(amount);
                break;
        }
    }
}