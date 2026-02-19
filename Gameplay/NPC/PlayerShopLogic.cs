using UnityEngine;
using Unity.Netcode;

public class PlayerShopLogic : NetworkBehaviour
{
    private PlayerProgression _progression;
    private WeaponManager _weaponManager;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsLocalPlayer)
        {
            Debug.Log("--- DEBUG SÍŤOVÝCH KOMPONENT ---");
            var behaviours = GetComponents<NetworkBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                Debug.Log($"Index {i}: {behaviours[i].GetType().Name}");
            }
            Debug.Log("--------------------------------");
        }
    }

    private void Awake()
    {
        _progression = GetComponent<PlayerProgression>();
        _weaponManager = GetComponent<WeaponManager>();
    }

    // --- KLIENT VOLÁ TOTO ---
    public void ClientBuyWeapon(int weaponIndex, int cost)
    {
        if (!IsOwner) return;

        // Vypíšeme, kdo volá nákup
        Debug.Log($"[SHOP DEBUG] Volám nákup na objektu: {gameObject.name}, NetID: {NetworkObjectId}, BehaviourIndex: 17");

        // Kontrola existence
        if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(NetworkObjectId, out var obj))
        {
            Debug.Log($"[SHOP DEBUG] Objekt {NetworkObjectId} je v Netcode zaregistrován: {obj.name}");
        }
        else
        {
            Debug.LogError($"[SHOP DEBUG] Objekt {NetworkObjectId} NENÍ v Netcode databázi!");
        }

        if (_progression.Gold.Value < cost)
        {
            Debug.Log("[Shop] Nedostatek zlata (Klient check)");
            return;
        }

        BuyWeaponServerRpc(weaponIndex, cost);
    }

    public void ClientSellWeapon(int refundAmount)
    {
        if (!IsOwner) return;
        SellWeaponServerRpc(refundAmount);
    }


    // --- SERVER ---
    // Musí být PUBLIC, aby se předešlo IL2CPP chybám
    [ServerRpc]
    public void BuyWeaponServerRpc(int index, int cost)
    {
        Debug.Log($"[Server SHOP DEBUG] hráč zkouší koupit {index} za {cost} zlatých");
        // 1. Ověříme finance na serveru (autorita)
        if (_progression.TrySpendGold(cost))
        {
            Debug.Log($"[Server] Hráč koupil zbraň ID {index}");

            // 2. Změníme zbraň
            _weaponManager.SetWeaponOnServer(index);

            // 3. Pošleme potvrzení zpět
            PurchaseResultClientRpc(true, "Nákup úspěšný!");
        }
        else
        {
            Debug.Log($"[Server SHOP DEBUG] Nedostatek zlata na serveru!");
            PurchaseResultClientRpc(false, "Nedostatek zlata na serveru!");
        }
    }

    [ServerRpc]
    public void SellWeaponServerRpc(int amount)
    {
        if (_weaponManager._currentWeaponIndex.Value != -1)
        {
            _progression.AddGold(amount);
            _weaponManager.SetWeaponOnServer(-1); // -1 = Žádná zbraň
            PurchaseResultClientRpc(true, "Prodej úspěšný!");
        }
    }

    // --- ODPOVĚĎ PRO KLIENTA ---
    [ClientRpc]
    public void PurchaseResultClientRpc(bool success, string msg)
    {
        if (!IsOwner) return;
        Debug.Log($"[Shop Result] {msg}");
        // Zde můžete napojit zvuk cinknutí mincí
    }
}