using UnityEngine;
using Unity.Netcode; // Potřebné pro NetworkObject

// Vyžaduje Collider komponentu na stejném objektu
[RequireComponent(typeof(Collider))]
public class LocationTrigger : MonoBehaviour
{
    [Header("Nastavení Lokace")]
    [Tooltip("Text, který se zobrazí")]
    [SerializeField] private string _locationName = "Neznámá lokace";

    [Tooltip("Zobrazí se název pouze jednou?")]
    [SerializeField] private bool _triggerOnce = true;

    private bool _hasBeenTriggered = false;

    private void Awake()
    {
        // Zajistíme, že collider je Trigger, aby se o něj hráč nezasekl
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // Zkontrolujeme, zda už byl trigger aktivován
        if (_triggerOnce && _hasBeenTriggered)
        {
            return;
        }

        // Zjistíme, zda objekt, který vstoupil, je NetworkObject
        if (!other.TryGetComponent<NetworkObject>(out NetworkObject netObj))
        {
            // Není to síťový objekt (např. projektil), ignorujeme
            return;
        }

        // Zkontrolujeme, zda je to NÁŠ LOKÁLNÍ HRÁČ
        if (!netObj.IsOwner)
        {
            // Je to jiný hráč, ignorujeme
            return;
        }

        // Je to náš hráč. Zkusíme najít PlayerHUD.
        if (PlayerHUD.LocalInstance != null)
        {
            // Našli jsme HUD, řekneme mu, ať zobrazí název
            PlayerHUD.LocalInstance.ShowLocationName(_locationName);
            
            _hasBeenTriggered = true;

            // Pokud se má trigger zničit po použití
            if (_triggerOnce)
            {
                // Můžeme ho vypnout, aby se nespouštěl znovu
                gameObject.SetActive(false); 
                // Nebo Destroy(gameObject); pokud ho už nikdy nebudeme potřebovat
            }
        }
    }
}