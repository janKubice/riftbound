using UnityEngine;
using Unity.Netcode;

public class LootManager : NetworkBehaviour
{
    public static LootManager Instance { get; private set; }

    [Header("Prefabs")]
    [SerializeField] private GameObject _xpOrbPrefab;
    [SerializeField] private GameObject _goldPrefab;

    private void Awake()
    {
        Instance = this;
    }

    // --- Server API ---

    /// <summary>
    /// Spawnne loot specifickému hráči (ostatní ho neuvidí).
    /// </summary>
    public void SpawnLootForPlayer(ulong playerId, Vector3 position, LootType type, int amount)
    {
        if (!IsServer) return;

        ClientRpcParams clientParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new ulong[] { playerId }
            }
        };

        SpawnLootClientRpc(position, type, amount, clientParams);
    }

    /// <summary>
    /// Spawnne loot všem (např. truhla pro všechny).
    /// </summary>
    public void SpawnGlobalLoot(Vector3 position, LootType type, int amount)
    {
        if (!IsServer) return;
        SpawnLootClientRpc(position, type, amount);
    }

    // --- Client Side Logic ---

    [ClientRpc]
    private void SpawnLootClientRpc(Vector3 position, LootType type, int amount, ClientRpcParams clientParams = default)
    {
        // Tady se děje magie. Tento kód se provede JEN na klientovi, kterému to patří.
        
        GameObject prefabToSpawn = _xpOrbPrefab;
        if (type == LootType.Gold) prefabToSpawn = _goldPrefab;

        // Instancujeme čistě lokální objekt. Žádný NetworkObject.
        GameObject orbObj = Instantiate(prefabToSpawn, position, Quaternion.identity);
        
        CollectableOrb orbScript = orbObj.GetComponent<CollectableOrb>();
        if (orbScript != null)
        {
            orbScript.Initialize(amount, type);
        }
    }

    // --- DEBUGGING (Editor Only) ---
    private void Update()
    {
        if (!IsServer) return;

        // Klávesa 'O' v Editoru spawnne XP pro všechny hráče kolem nich
        if (Application.isEditor && UnityEngine.InputSystem.Keyboard.current.oKey.wasPressedThisFrame)
        {
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject != null)
                {
                    // Spawnne 5 orbů kolem hráče
                    for (int i = 0; i < 5; i++)
                    {
                        Vector3 randomPos = client.PlayerObject.transform.position + UnityEngine.Random.insideUnitSphere * 3f;
                        randomPos.y = 1f; // Výška nad zemí
                        SpawnLootForPlayer(client.ClientId, randomPos, LootType.Experience, 10);
                    }
                }
            }
            Debug.Log("Debug Loot Spawned!");
        }
    }
}