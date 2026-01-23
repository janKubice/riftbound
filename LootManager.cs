using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class LootManager : NetworkBehaviour
{
    public static LootManager Instance { get; private set; }

    [Header("Prefabs (Lokální)")]
    [SerializeField] private GameObject _xpOrbPrefab;
    [SerializeField] private GameObject _goldPrefab;
    [SerializeField] private GameObject _healthPrefab;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        Instance = this;
    }

    /// <summary>
    /// HLAVNÍ METODA: Zavolá se na serveru (např. když umře mob).
    /// </summary>
    public void SpawnLoot(Vector3 position, LootTable table)
    {
        if (!IsServer || table == null) return;

        // 1. Vybereme, co má padnout
        if (table.TryGetLoot(out LootEntry entry, out int amount))
        {
            // 2. Rozhodneme, komu to padne
            // V Survivor hrách padá loot buď všem (každý má svůj), nebo jen tomu, kdo zabil.
            // Zde uděláme variantu: Každý hráč dostane svůj vlastní loot (instanced loot).
            
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                {
                    // Pošleme RPC konkrétnímu klientovi
                    ClientRpcParams clientParams = new ClientRpcParams
                    {
                        Send = new ClientRpcSendParams { TargetClientIds = new ulong[] { client.ClientId } }
                    };

                    SpawnLootClientRpc(position, entry.Type, amount, clientParams);
                }
            }
        }
    }

    [ClientRpc]
    private void SpawnLootClientRpc(Vector3 position, LootType type, int amount, ClientRpcParams clientParams = default)
    {
        GameObject prefabToSpawn = _xpOrbPrefab;
        
        // Výběr prefabu podle typu
        switch (type)
        {
            case LootType.Gold: prefabToSpawn = _goldPrefab; break;
            case LootType.HealthPotion: prefabToSpawn = _healthPrefab; break;
        }

        if (prefabToSpawn != null)
        {
            // Instancujeme lokální objekt (bez sítě)
            GameObject orbObj = Instantiate(prefabToSpawn, position, Quaternion.identity);
            
            // Inicializace
            CollectableOrb orbScript = orbObj.GetComponent<CollectableOrb>();
            if (orbScript != null)
            {
                orbScript.Initialize(amount, type);
            }
        }
    }
}