using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class LocationTrigger : MonoBehaviour
{
    [Header("Nastavení Lokace")]
    [Tooltip("Text, který se zobrazí")]
    [SerializeField] private string _locationName = "Neznámá lokace";

    [Header("Atmosféra (Visual Juice)")]
    [SerializeField] private LocationProfile _locationProfile; // NOVÉ

    [Tooltip("Zobrazí se název pouze jednou?")]
    [SerializeField] private bool _triggerOnce = false; // Pro visualy chceme spíš false (aby se při návratu atmosféra obnovila)

    private bool _hasBeenTriggered = false;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggerOnce && _hasBeenTriggered) return;

        if (!other.TryGetComponent<NetworkObject>(out NetworkObject netObj)) return;
        if (!netObj.IsOwner) return; // Jen lokální hráč

        // 1. UI (Původní logika)
        if (PlayerHUD.LocalInstance != null)
        {
            PlayerHUD.LocalInstance.ShowLocationName(_locationName);
        }

        // 2. ATMOSFÉRA (Nová logika)
        if (AtmosphereManager.Instance != null && _locationProfile != null)
        {
            AtmosphereManager.Instance.EnterLocation(_locationProfile);
        }
            
        _hasBeenTriggered = true;

        // Visual trigger neničíme, protože hráč se může vrátit
        if (_triggerOnce)
        {
            // Pokud je to jen textový trigger, můžeme ho vypnout
             // gameObject.SetActive(false); 
        }
    }
}