using UnityEngine;
using Unity.Netcode;
using TMPro; // Vyžaduje TextMeshPro

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))]
public class SignboardInteractable : NetworkBehaviour, IInteractable
{
    [Header("Obsah Cedulky")]
    [Tooltip("Text, který je fyzicky napsaný na cedulce ve světě.")]
    [SerializeField, TextArea] private string _signText = "Neznámý text";
    
    [Tooltip("Komponenta TextMeshPro umístěná na modelu cedulky.")]
    [SerializeField] private TextMeshPro _worldTextComponent;

    [Header("Interakce (HUD)")]
    [Tooltip("Text, který se zobrazí hráči na obrazovce při zamíření.")]
    [SerializeField] private string _interactionPrompt = "E - Přečíst cedulku";

    [Header("Audio (Volitelné)")]
    [Tooltip("Zvuk pro interakci (např. šustění papíru/dřeva).")]
    [SerializeField] private NetworkedAudioSource _networkedAudio;
    [SerializeField] private int _readSoundIndex = 0;

    // --- IInteractable ---
    public string InteractionPrompt => _interactionPrompt;

    private void Start()
    {
        // Aplikujeme text na 3D model při startu
        if (_worldTextComponent != null)
        {
            _worldTextComponent.text = _signText;
        }
        else
        {
            Debug.LogWarning($"[Signboard] Cedulka {gameObject.name} nemá přiřazený TextMeshPro komponent!");
        }

        // Zajištění, že collider je trigger (nebo alespoň neblokuje hráče, pokud to není žádoucí)
        // Pokud má cedulka fyzický kolizní model, nechte toto zakomentované a přidejte samostatný Trigger collider.
        // GetComponent<Collider>().isTrigger = true; 
    }

    public void Interact(NetworkObject interactor)
    {
        // Voláno pouze na serveru přes PlayerInteractor
        
        // Přehrání zvuku při čtení (pokud je nastaveno)
        if (_networkedAudio != null)
        {
            _networkedAudio.PlayOneShotNetworked(_readSoundIndex);
        }

        // Zde můžete přidat ClientRpc volání, které např. zobrazí text ve velkém UI na obrazovce
        // ShowSignTextClientRpc(_signText, interactor.OwnerClientId);
    }
}