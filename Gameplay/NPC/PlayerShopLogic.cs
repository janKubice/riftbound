using UnityEngine;
using Unity.Netcode;

public class PlayerShopLogic : NetworkBehaviour
{
    public static PlayerShopLogic LocalInstance { get; private set; }
    private PlayerProgression _progression;
    private WeaponManager _weaponManager;

    public override void OnNetworkSpawn()
    {
        // Pokud je tento objekt můj (jsem vlastník), nastavím se jako LocalInstance
        if (IsOwner)
        {
            if (LocalInstance != null) 
            {
                // Pojistka proti duplicitám při znovunačtení scény
                LocalInstance = null; 
            }
            LocalInstance = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Úklid při odpojení/smrti
        if (IsOwner && LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    private void Awake()
    {
        _progression = GetComponent<PlayerProgression>();
        _weaponManager = GetComponent<WeaponManager>();
    }

    // Volá se z UI tlačítka
    public void RequestBuyWeapon(int weaponIndex, int cost)
    {
        if (IsOwner) BuyWeaponServerRpc(weaponIndex, cost);
    }

    public void RequestSellWeapon(int refundAmount)
    {
        if (IsOwner) SellWeaponServerRpc(refundAmount);
    }

    [ServerRpc]
    private void BuyWeaponServerRpc(int index, int cost)
    {
        // 1. Ověření financí na serveru
        if (_progression.Gold.Value >= cost)
        {
            // 2. Odečíst peníze
            _progression.TrySpendGold(cost);

            // 3. Vybavit zbraň (WeaponManager se postará o vizuál a výměnu)
            _weaponManager.SetWeaponOnServer(index);
        }
    }

    [ServerRpc]
    private void SellWeaponServerRpc(int amount)
    {
        // 1. Ověřit, že má co prodat (není -1)
        if (_weaponManager._currentWeaponIndex.Value != -1)
        {
            // 2. Přičíst peníze
            _progression.AddGold(amount);

            // 3. Nastavit Unarmed (-1)
            _weaponManager.SetWeaponOnServer(-1);
        }
    }
}