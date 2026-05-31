using System.Collections;
using UnityEngine;
using TMPro;
using RogueDeckCoop.Networking;

public class Bootstrapper : MonoBehaviour
{
    [Header("Jednotné UI")]
    [Tooltip("Text, který ukazuje buď stav načítání, nebo chybovou hlášku.")]
    [SerializeField] private TextMeshProUGUI _statusText;
    
    [Tooltip("Tlačítko pro ukončení (zpočátku je skryté).")]
    [SerializeField] private GameObject _exitButton;

    private IEnumerator Start()
    {
        // 1. Příprava
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Skryjeme tlačítko a napíšeme, co se právě děje
        if (_exitButton != null) _exitButton.SetActive(false);
        if (_statusText != null) _statusText.text = "Verifying Steam Connection...";

        // Chvíli počkáme, aby bylo vidět, že hra něco dělá
        yield return new WaitForSeconds(1.0f);

        // 2. Kontrola
        if (SteamManager.Instance != null && SteamManager.Instance.IsSteamInitialized)
        {
            // --- VŠE JE OK ---
            if (_statusText != null) _statusText.text = "Loading Main Menu...";
            
            // Jdeme do menu. LoadingScreenManager se pak už probudí sám.
            AppManager.Instance.GoToMainMenu();
        }
        else
        {
            // --- CHYBA ---
            // Nespouštíme žádný LoadingScreenManager! Jen změníme text a ukážeme tlačítko.
            ShowCriticalError("Steam is not running or initialization failed.\nThe Steam client must be active!");
        }
    }

    private void ShowCriticalError(string message)
    {
        Debug.LogError($"[Bootstrapper] Kritická chyba: {message}");

        if (_statusText != null)
        {
            _statusText.color = Color.red; // Zvýrazníme chybu červeně
            _statusText.text = message;
        }

        if (_exitButton != null)
        {
            _exitButton.SetActive(true); // Teprve teď dovolíme hráči hru zavřít
        }
    }

    public void OnQuitButtonClicked()
    {
        AppManager.Instance.ExitGame();
    }
}