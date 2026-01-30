using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerGlobalEffects : NetworkBehaviour
{
    // Tento list drží efekty, které se aplikují na KAŽDÝ útok
    // Protože HitEffect je ScriptableObject (Asset), nemusíme ho složitě synchronizovat,
    // ale musíme zajistit, že klienti vědí, co mají v listu.
    // Pro jednoduchost: Server je autorita, klienty to nezajímá, dokud nevystřelí (což řídí server).
    
    public List<HitEffect> GlobalEffects = new List<HitEffect>();

    [ServerRpc]
    public void AddGlobalEffectServerRpc(int shopItemIndex)
    {
        // Poznámka: Zde musíme nějak vědět, o jaký efekt jde. 
        // V ideálním světě posíláme ID. Pro tento prototyp předpokládáme, 
        // že ShopInteractable má seznam a posíláme index. 
        // Implementaci vyřešíme v ShopInteractable.
    }
    
    // Metoda volaná přímo ze serveru (když se koupí item)
    public void AddEffect(HitEffect effect)
    {
        if(!IsServer) return;
        GlobalEffects.Add(effect);
        Debug.Log($"[GlobalEffects] Přidán efekt: {effect.name}");
    }
}