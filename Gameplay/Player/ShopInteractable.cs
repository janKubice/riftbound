using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class ShopInteractable : NetworkBehaviour, IInteractable
{
    [Header("Inventory")]
    [SerializeField] private List<ShopItemData> _shopItems;

    [Header("UI")]
    [SerializeField] private ShopUI _shopUI;

    [Header("Feedback")]
    [SerializeField] private InteractionFeedback _feedback;

    public string InteractionPrompt => "E - Open Shop";

    public ShopItemData GetItemByIndex(int index)
    {
        if (index >= 0 && index < _shopItems.Count)
            return _shopItems[index];

        return null;
    }

    public void Interact(NetworkObject interactor)
    {
        if (!IsServer)
            return;

        if (interactor == null)
            return;

        ulong clientId = interactor.OwnerClientId;

        ClientRpcParams rpcParams = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[] { clientId }
            }
        };

        OpenShopClientRpc(rpcParams);

        if (_feedback != null)
            _feedback.PlayForAllClients();
    }

    [ClientRpc]
    private void OpenShopClientRpc(ClientRpcParams rpcParams = default)
    {
        if (_shopUI != null)
        {
            _shopUI.OpenShop(this, _shopItems);
        }
    }
}