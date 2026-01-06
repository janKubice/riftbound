using UnityEngine;
using RogueDeckCoop.Networking; // Namespace našeho SteamManageru

// Bootstrapper pouze ověří, že je vše připraveno, a přesune nás do menu
public class Bootstrapper : MonoBehaviour
{
    void Start()
    {
        // Zde bychom mohli v budoucnu čekat na dokončení více inicializací
        // (načítání uložených dat, login atd.)

        // Prototyp: Jen ověříme Steam a jdeme do menu
        if (SteamManager.Instance != null && SteamManager.Instance.IsSteamInitialized)
        {
            // Přechod do menu
            AppManager.Instance.GoToMainMenu();
        }
        else
        {
            // Zde by mohla být UI obrazovka "Nelze se připojit ke Steamu"
            Debug.LogError("Steam není inicializován. Aplikace nemůže pokračovat.");

            // Můžeme zkusit zavřít hru
            // AppManager.Instance.ExitGame(); 
        }
    }
}