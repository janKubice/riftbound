using UnityEngine;
using Unity.Netcode;

public class PlayerCollector : NetworkBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private float _baseCollectionRadius = 5.0f;
    [SerializeField] private LayerMask _lootLayer; // Nezapomeň nastavit Layer "Loot"!
    [Header("Vizuální Dávkování")]
    [SerializeField] private float _popupInterval = 0.5f; // Jak často vyskočí číslo

    private Collider[] _hitBuffer = new Collider[50];
    private PlayerAttributes _attributes; // Předpokládám, že XP máš v Attributes nebo Progression
    private PlayerProgression _progression;

    private int _accumulatedXP = 0;
    private int _accumulatedGold = 0;
    private float _popupTimer = 0f;
    private void Awake()
    {
        _attributes = GetComponent<PlayerAttributes>();
        _progression = GetComponent<PlayerProgression>();
    }

    private void Update()
    {
        if (!IsOwner) return;

        // Odpočet pro zobrazení vizuálních čísel
        if (_accumulatedXP > 0 || _accumulatedGold > 0)
        {
            _popupTimer -= Time.deltaTime;
            if (_popupTimer <= 0f)
            {
                FlushPopups();
            }
        }
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
        RequestCollectLootServerRpc(type, amount);

        if (SteamStatsManager.Instance != null)
        {
            SteamStatsManager.Instance.IncrementStat(SteamStatIds.OrbsCollected, 1);

            switch (type)
            {
                case LootType.Experience:
                    SteamStatsManager.Instance.IncrementStat(SteamStatIds.XpCollected, amount);
                    break;

                case LootType.Gold:
                    SteamStatsManager.Instance.IncrementStat(SteamStatIds.GoldCollected, amount);
                    break;
            }
        }

        if (type == LootType.Experience) _accumulatedXP += amount;
        else if (type == LootType.Gold) _accumulatedGold += amount;
    }

    private void FlushPopups()
    {
        if (DamageNumberManager.Instance == null) return;

        // Pokud máme nastřádané XP, ukážeme je a vynulujeme
        if (_accumulatedXP > 0)
        {
            DamageNumberManager.Instance.SpawnPopupLocal(transform.position, _accumulatedXP, PopupType.Experience);
            _accumulatedXP = 0;
        }

        // Totéž pro zlato
        if (_accumulatedGold > 0)
        {
            // Posuneme zlato trošku výš, ať se nepřekrývá s XP, pokud vyskočí naráz
            DamageNumberManager.Instance.SpawnPopupLocal(transform.position + (Vector3.up * 0.5f), _accumulatedGold, PopupType.Gold);
            _accumulatedGold = 0;
        }

        _popupTimer = _popupInterval; // Reset časovače
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