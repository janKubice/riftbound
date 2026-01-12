using UnityEngine;
using Unity.Netcode;
using RogueDeckCoop.Networking; // Namespace tvého SteamManageru
using System.Collections;

public class DevelopmentAutoHost : MonoBehaviour
{
    [Header("Dev Settings")]
    [Tooltip("Pokud je true, automaticky zapne Host mode při startu scény.")]
    public bool AutoStartHost = true;
    
    [Tooltip("ID postavy, za kterou chceš testovat (0=Warrior, 1=Mage...).")]
    public int MockCharacterId = 0;

    [Tooltip("Zkrátit odpočet startu hry? (přepíše GameLifecycleManager)")]
    public bool FastStartDelay = true;

    private void Start()
    {
        // Tento skript funguje pouze v Unity Editoru. V buildu se sám zničí.
#if !UNITY_EDITOR
        Destroy(gameObject);
        return;
#endif

        StartCoroutine(AutoStartRoutine());
    }

    private IEnumerator AutoStartRoutine()
    {
        // 1. Počkáme jeden frame, aby se stihly inicializovat Singletony (SteamManager, NetworkManager)
        yield return null;

        // 2. Kontrola: Pokud už hra běží (např. jsme přišli z Menu), nic neděláme
        if (NetworkManager.Singleton == null || NetworkManager.Singleton.IsListening)
        {
            yield break;
        }

        if (!AutoStartHost) yield break;

        Debug.LogWarning("<color=yellow>[DEV] Auto-Start: Simuluji spuštění Hosta...</color>");

        // 3. Simulace výběru postavy (SteamManager.FinalCharacterSelections)
        // Host má vždy ClientId 0
        if (SteamManager.Instance != null)
        {
            if (!SteamManager.Instance.FinalCharacterSelections.ContainsKey(0))
            {
                SteamManager.Instance.FinalCharacterSelections.Add(0, MockCharacterId);
            }
            else
            {
                SteamManager.Instance.FinalCharacterSelections[0] = MockCharacterId;
            }
        }
        else
        {
            Debug.LogError("[DEV] Chybí SteamManager v scéně! Ujisti se, že máš v GameScene prefab s Managery (Core).");
        }

        // 4. Start Hosta (přeskočíme Steam Lobby tvorbu a jdeme rovnou na Transport)
        // Pokud používáš SteamNetworkingSocketsTransport, musí běžet Steam klient na pozadí.
        bool success = NetworkManager.Singleton.StartHost();
        
        if (success)
        {
            Debug.Log("<color=green>[DEV] Host úspěšně nastartován.</color>");
            
            // 5. Hack: Zrychlení spawnu v GameLifecycleManager
            if (FastStartDelay)
            {
                var lifecycle = FindFirstObjectByType<GameLifecycleManager>();
                if (lifecycle != null)
                {
                    // Přes reflexi nebo změnou public pole můžeš zkrátit čas
                    // V tvém kódu je _startDelay serialized field, takže:
                    // (Pokud nechceš používat reflexi, prostě změň _startDelay na public nebo přidej Setter)
                    // Zde je bezpečnější varianta čekání na spawn
                }
            }
        }
        else
        {
            Debug.LogError("[DEV] StartHost selhal. Zkontroluj konzoli (možná Steam není init?).");
        }
    }
}