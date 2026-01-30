using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class ShopInteractable : NetworkBehaviour, IInteractable
{
    [Header("Inventory")]
    [SerializeField] private List<ShopItemData> _shopItems;

    [Header("UI")]
    [SerializeField] private ShopUI _shopUI; // Odkaz na UI ve scéně (nebo prefab)

    public string InteractionPrompt => "E - Open Shop";

    // Pomocná metoda pro ServerRPC ve WeaponManageru
    public ShopItemData GetItemByIndex(int index)
    {
        if (index >= 0 && index < _shopItems.Count) return _shopItems[index];
        return null;
    }

    public void Interact(NetworkObject interactor)
    {
        // Interakce běží na serveru, ale UI musíme otevřít na klientovi.
        ulong clientId = interactor.OwnerClientId;
        OpenShopClientRpc(clientId);
    }

    [ClientRpc]
    private void OpenShopClientRpc(ulong targetClientId)
    {
        // Otevřeme UI jen tomu, kdo interagoval
        if (NetworkManager.Singleton.LocalClientId == targetClientId)
        {
            if (_shopUI != null)
            {
                _shopUI.OpenShop(this, _shopItems);
            }
        }
    }
}