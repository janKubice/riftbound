using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Collider))]
public class LocationTrigger : MonoBehaviour
{
    [Header("UI & Notifikace")]
    [Tooltip("Název lokace pro zobrazení na HUDu.")]
    [SerializeField] private string _locationName = "Neznámá oblast";
    
    [Tooltip("Pokud je true, název se zobrazí pouze při prvním vstupu. Atmosféra se ale změní vždy.")]
    [SerializeField] private bool _showNameOnlyOnce = true;

    [Header("Atmosféra & Audio")]
    [Tooltip("Profil definující mlhu, ambient a hudbu pro tuto oblast.")]
    [SerializeField] private LocationProfile _locationProfile;

    // Interní stav
    private bool _hasShownName = false;

    private void Awake()
    {
        // Vynutíme trigger, aby to fungovalo i když to level designer zapomene nastavit
        GetComponent<Collider>().isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        // 1. Validace sítě: Řešíme pouze lokálního hráče
        if (!IsLocalPlayer(other)) return;

        // 2. UI Logika (Možnost One-Shot)
        if (!_showNameOnlyOnce || !_hasShownName)
        {
            if (PlayerHUD.LocalInstance != null)
            {
                PlayerHUD.LocalInstance.ShowLocationName(_locationName);
                _hasShownName = true;
            }
        }

        // 3. Atmosféra (Vždy při vstupu)
        if (_locationProfile != null)
        {
            // Oznámíme AtmosphereManageru, že vstupujeme do ovlivněné zóny
            if (AtmosphereManager.Instance != null)
            {
                AtmosphereManager.Instance.EnterLocation(_locationProfile);
            }

            // Oznámíme MusicManageru změnu tracku
            if (MusicManager.Instance != null)
            {
                MusicManager.Instance.EnterLocation(_locationProfile);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        // 1. Validace sítě
        if (!IsLocalPlayer(other)) return;

        // 2. Opuštění zóny - Návrat k globálnímu cyklu
        if (_locationProfile != null)
        {
            if (AtmosphereManager.Instance != null)
            {
                // Spustí fade-out efektu lokace zpět na 0 (čistý Day/Night)
                AtmosphereManager.Instance.ExitLocation();
            }

            // Volitelně: Reset hudby nebo přechod na "Roaming/World" hudbu
            /* if (MusicManager.Instance != null)
            {
                MusicManager.Instance.ReturnToDefault(); 
            }
            */
        }
    }

    /// <summary>
    /// Helper pro ověření, zda kolidující objekt je lokální hráč.
    /// Šetří GetComponent volání a zpřehledňuje kód.
    /// </summary>
    private bool IsLocalPlayer(Collider other)
    {
        // Rychlý check tagu pro optimalizaci (volitelné, pokud používáš Tagy)
        if (!other.CompareTag("Player")) return false;

        if (other.TryGetComponent<NetworkObject>(out NetworkObject netObj))
        {
            return netObj.IsOwner;
        }
        return false;
    }
}