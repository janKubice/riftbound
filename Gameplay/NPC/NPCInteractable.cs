using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class NPCInteractable : NetworkBehaviour, IInteractable
{
    [Header("Settings")]
    [SerializeField] private string _npcName = "Merchant";
    [SerializeField] private DialogueNode _rootDialogue;

    // Getter pro DialogueManager
    public string NpcName => _npcName;

    [Header("Shop Settings")]
    [Tooltip("Indexy zbraní z WeaponManageru, které toto NPC prodává (např. 0, 1, 3)")]
    public List<int> WeaponIndexesForSale;

    private Transform _playerTransform;

    public string InteractionPrompt => $"E - Talk to {_npcName}";

    private void Update()
    {
        if (_playerTransform != null)
        {
            Vector3 dir = _playerTransform.position - transform.position;
            dir.y = 0;
            if (dir != Vector3.zero)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, Time.deltaTime * 5f);
            }
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Player")) _playerTransform = other.transform;
    }
    private void OnTriggerExit(Collider other)
    {
        if (other.CompareTag("Player") && other.transform == _playerTransform) _playerTransform = null;
    }

    public void Interact(NetworkObject interactor)
    {
        // Pokud metodu zavolá klient, musíme požádat server o spuštění
        if (!IsServer)
        {
            RequestInteractServerRpc(interactor.OwnerClientId);
            return;
        }

        // Pokud jsme na serveru, rovnou spustíme logiku
        StartInteractionOnServer(interactor.OwnerClientId);
    }

    // 1. Klient poprosí server: "Chci mluvit s tímto NPC"
    [ServerRpc(RequireOwnership = false)]
    private void RequestInteractServerRpc(ulong requestingClientId)
    {
        StartInteractionOnServer(requestingClientId);
    }

    // 2. Server provede logiku a pošle odpověď zpět klientovi
    private void StartInteractionOnServer(ulong clientId)
    {
        // Tady řekneme konkrétnímu klientovi: "Otevři si dialog"
        OpenDialogueClientRpc(clientId);
    }

    // 3. Klient dostane rozkaz: "Otevři UI"
    [ClientRpc]
    private void OpenDialogueClientRpc(ulong targetClientId)
    {
        // Ověříme, že tato zpráva je pro nás (pro lokálního hráče)
        if (NetworkManager.Singleton.LocalClientId != targetClientId) return;

        // Otevřeme UI
        if (DialogueManager.Instance != null)
        {
            DialogueManager.Instance.StartDialogue(_rootDialogue, this);
        }
        else
        {
            Debug.LogError("DialogueManager Instance chybí ve scéně!");
        }
    }

    // --- NOVÉ: Logika pro akce z Dialogu (Léčení) ---

    [ServerRpc(RequireOwnership = false)]
    public void RequestHealServerRpc(ulong targetClientId)
    {
        // 1. Najdeme hráče na serveru
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(targetClientId, out NetworkClient client))
        {
            GameObject playerObj = client.PlayerObject.gameObject;

            // 2. Zkontrolujeme finance (PlayerProgression)
            var progression = playerObj.GetComponent<PlayerProgression>();
            int healCost = 50; // Cena za heal (může být parametr)

            if (progression != null && progression.Gold.Value >= healCost)
            {
                // 3. Odečteme peníze
                progression.TrySpendGold(healCost);

                // 4. Vyléčíme (PlayerAttributes)
                var attr = playerObj.GetComponent<PlayerAttributes>();
                if (attr != null)
                {
                    attr.Heal(attr.MaxHealth.Value); // Full heal
                }
            }
            else
            {
                // TODO: Poslat zpět zprávu "Not enough gold"
            }
        }
    }
}