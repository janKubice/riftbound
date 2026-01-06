using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Collider))] // Musí mít collider pro detekci
public class WeaponPickup : NetworkBehaviour, IInteractable
{
    [Header("Data Zbraně")]
    [Tooltip("Hash prefabu zbraně (z NetworkManager -> Prefabs), který se má hráči vybavit")]
    [SerializeField] private int _weaponIndex;

    [Tooltip("Text, který se zobrazí hráči")]
    [SerializeField] private string _prompt = "E - Sebrat zbraň";

    // Implementace z IInteractable
    public string InteractionPrompt => _prompt;

    // Tuto metodu zavolá server (přes PlayerInteractor)
    public void Interact(NetworkObject interactor)
    {
        // Tento kód běží POUZE na serveru

        // 1. Získáme WeaponManager z hráče, který interagoval
        WeaponManager weaponManager = interactor.GetComponent<WeaponManager>();
        if (weaponManager == null)
        {
            Debug.LogError($"[WeaponPickup] Hráč {interactor.name} nemá WeaponManager!");
            return;
        }

        // 2. Řekneme jeho WeaponManageru (na serveru), aby vybavil tuto zbraň
        weaponManager.SetWeaponOnServer(_weaponIndex);

        // 3. NOVÉ: Zničíme (Despawn) tento objekt, aby zmizel ze země
        // 'true' znamená, že se má rovnou zničit
        gameObject.NetDestroy();
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    // Zajistíme, že collider je trigger, aby se hráč nezasekl
    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }
}