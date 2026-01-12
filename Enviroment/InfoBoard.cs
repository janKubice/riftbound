using UnityEngine;

public class InfoBoard : MonoBehaviour, IInteractable
{
    [TextArea(5, 10)]
    [SerializeField] private string _messageText;
    [SerializeField] private string _headerText = "Oznámení města";
    
    // Odkaz na nějaký obecný UI Manager, který umí zobrazit okno
    // Pokud nemáš, uděláme jednoduchý Event
    public static System.Action<string, string> OnOpenBoard; 

    public string InteractionPrompt => "E - Číst";

    // Jelikož IInteractable vyžaduje NetworkObject v parametru Interact,
    // ale my nepotřebujeme server, prostě to ignorujeme.
    public void Interact(Unity.Netcode.NetworkObject interactor)
    {
        // Zjistíme, jestli interaguje lokální hráč
        if (interactor.IsOwner)
        {
            Debug.Log($"Board Info: {_headerText}\n{_messageText}");
            
            // Zde zavoláš své UI. Příklad:
            // UIManager.Instance.ShowMessage(_headerText, _messageText);
            
            // Nebo provizorně přes event:
            OnOpenBoard?.Invoke(_headerText, _messageText);
        }
    }
}