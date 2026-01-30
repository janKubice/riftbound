using UnityEngine;
using Unity.Netcode;

public class PlayerShopController : NetworkBehaviour
{
    private PlayerProgression _progression;
    private WeaponManager _weaponManager;
    private PlayerGlobalEffects _globalEffects;

    private void Awake()
    {
        _progression = GetComponent<PlayerProgression>();
        _weaponManager = GetComponent<WeaponManager>();
        _globalEffects = GetComponent<PlayerGlobalEffects>();
    }

    [ServerRpc]
    public void TryBuyItemServerRpc(int itemIndex, NetworkBehaviourReference shopRef)
    {
        if (shopRef.TryGet(out ShopInteractable shop))
        {
            ShopItemData item = shop.GetItemByIndex(itemIndex);
            if (item == null) return;

            // 1. Máme peníze?
            if (_progression.Gold.Value >= item.GoldCost)
            {
                // 2. Odečíst peníze
                _progression.Gold.Value -= item.GoldCost;

                // 3. Aplikovat efekt
                if (item.IsGlobalUpgrade)
                {
                    _globalEffects.AddEffect(item.EffectPayload);
                }
                else
                {
                    // Přidat na zbraň
                    _weaponManager.AddWeaponEffectServerRpc(itemIndex, shopRef); 
                    // (Voláme metodu weapon managera přímo, protože jsme na serveru)
                    // Ale WeaponManager má RPC.. upravíme:
                    // Přímo přistoupíme k listu, protože jsme na stejném objektu na serveru:
                    _weaponManager.CurrentRuntimeStats.OnHitEffects.Add(item.EffectPayload);
                }
                
                // 4. Refresh UI klienta (volitelné, client si to může zjistit sám)
                RefreshShopUIClientRpc();
            }
        }
    }

    [ServerRpc]
    public void BuyItemTransactionServerRpc(int itemIndex, NetworkBehaviourReference shopRef)
    {
        // 1. Získáme data o předmětu z obchodu
        // (Posíláme referenci na obchod, protože ShopItemData je jen Asset a nemá NetworkID)
        if (!shopRef.TryGet(out ShopInteractable shopInstance)) 
        {
            Debug.LogError("Nepodařilo se najít instanci obchodu na serveru.");
            return;
        }

        ShopItemData item = shopInstance.GetItemByIndex(itemIndex);
        if (item == null) return;

        // 2. Zkusíme utratit peníze (Atomická operace v PlayerProgression)
        // Metoda TrySpendGold vrací true, pokud měl hráč dost peněz a odečetla je
        if (_progression != null && _progression.TrySpendGold(item.GoldCost))
        {
            // 3. Aplikujeme efekt
            ApplyPurchasedEffect(item);

            // 4. Řekneme klientovi, ať si aktualizuje UI (volitelné)
            RefreshUIClientRpc();
            
            Debug.Log($"[Shop] Hráč {OwnerClientId} koupil {item.ItemName}");
        }
    }

    [ClientRpc]
    private void RefreshUIClientRpc()
    {
        if (IsOwner)
        {
            // Najdeme otevřené okno a aktualizujeme ho
            ShopUI ui = FindFirstObjectByType<ShopUI>();
            if (ui != null && ui.gameObject.activeSelf)
            {
                ui.RefreshWeaponEffects();
            }
        }
    }

    private void ApplyPurchasedEffect(ShopItemData item)
    {
        if (item.IsGlobalUpgrade)
        {
            // Globální efekt (na hráče)
            if (_globalEffects != null)
            {
                _globalEffects.AddEffect(item.EffectPayload);
            }
        }
        else
        {
            // Lokální efekt (na zbraň)
            if (_weaponManager != null)
            {
                _weaponManager.AddRuntimeEffect(item.EffectPayload);
            }
        }
    }

    [ClientRpc]
    private void RefreshShopUIClientRpc()
    {
        if (IsOwner)
        {
            FindFirstObjectByType<ShopUI>()?.RefreshWeaponEffects(); // Refresh pravého panelu
        }
    }
    
    // Metody pro řazení (Forwarding z UI na WeaponManager)
    [ServerRpc]
    public void SwapEffectsServerRpc(int indexA, int indexB)
    {
        _weaponManager.SwapWeaponEffectsServerRpc(indexA, indexB);
        RefreshShopUIClientRpc();
    }
    
    [ServerRpc]
    public void RemoveEffectServerRpc(int index)
    {
        _weaponManager.RemoveWeaponEffectServerRpc(index);
        RefreshShopUIClientRpc();
    }
}