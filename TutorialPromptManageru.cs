using UnityEngine;
using System;

public class TutorialPromptManager : MonoBehaviour
{
    [Header("UI Prompty (Panely/Texty)")]
    [SerializeField] private GameObject _movePrompt;   // WASD ikona
    [SerializeField] private GameObject _attackPrompt; // Myš (LMB) ikona
    [SerializeField] private GameObject _dodgePrompt;  // Mezerník ikona

    // Lokální stav
    private bool _hasMoved;
    private bool _hasAttacked;
    private bool _hasDodged;

    private void Start()
    {
#if UNITY_EDITOR
        PlayerPrefs.DeleteKey("Tut_Moved");
        PlayerPrefs.DeleteKey("Tut_Attacked");
        PlayerPrefs.DeleteKey("Tut_Dodged");
        _hasMoved = false;
        _hasAttacked = false;
        _hasDodged = false;
#else
    _hasMoved = PlayerPrefs.GetInt("Tut_Moved", 0) == 1;
    _hasAttacked = PlayerPrefs.GetInt("Tut_Attacked", 0) == 1;
    _hasDodged = PlayerPrefs.GetInt("Tut_Dodged", 0) == 1;
#endif

        HideAll();
        if (!_hasMoved) _movePrompt.SetActive(true);
    }

    private void OnEnable()
    {
        // Přihlášení k odběru událostí
        PlayerController.OnLocalPlayerMoved += HandlePlayerMoved;
        PlayerController.OnLocalPlayerAttacked += HandlePlayerAttacked;
        PlayerController.OnLocalPlayerDodged += HandlePlayerDodged;
        WeaponManager.OnLocalWeaponEquipped += HandleWeaponEquipped;
        DirectorSpawner.OnEnemySpawned += ShowDodgePrompt;
    }

    private void OnDisable()
    {
        // Odhlášení (důležité pro prevenci memory leaks)
        PlayerController.OnLocalPlayerMoved -= HandlePlayerMoved;
        PlayerController.OnLocalPlayerAttacked -= HandlePlayerAttacked;
        PlayerController.OnLocalPlayerDodged -= HandlePlayerDodged;
        WeaponManager.OnLocalWeaponEquipped -= HandleWeaponEquipped;
        DirectorSpawner.OnEnemySpawned -= ShowDodgePrompt;
    }

    private void HideAll()
    {
        if (_movePrompt) _movePrompt.SetActive(false);
        if (_attackPrompt) _attackPrompt.SetActive(false);
        if (_dodgePrompt) _dodgePrompt.SetActive(false);
    }

    // --- Reakce na Události ---

    private void HandlePlayerMoved()
    {
        if (_hasMoved) return;
        _hasMoved = true;
        PlayerPrefs.SetInt("Tut_Moved", 1);
        PlayerPrefs.Save();

        if (_movePrompt) _movePrompt.SetActive(false); // Ideálně nahradit animací (FadeOut)
    }

    private void HandleWeaponEquipped()
    {
        // Zbraň sebrána z truhly! Pokud jsme ještě neútočili, ukaž prompt.
        if (!_hasAttacked && _attackPrompt)
        {
            _attackPrompt.SetActive(true);
        }
    }

    private void HandlePlayerAttacked()
    {
        if (_hasAttacked) return;
        _hasAttacked = true;
        PlayerPrefs.SetInt("Tut_Attacked", 1);
        PlayerPrefs.Save();

        if (_attackPrompt) _attackPrompt.SetActive(false);
    }

    private void HandlePlayerDodged()
    {
        if (_hasDodged) return;
        _hasDodged = true;
        PlayerPrefs.SetInt("Tut_Dodged", 1);
        PlayerPrefs.Save();

        if (_dodgePrompt) _dodgePrompt.SetActive(false);
    }

    // Metoda pro spuštění Dodge promptu zvenčí (např. od ArenaManageru při prvním spawnu nepřítele)
    public void ShowDodgePrompt()
    {
        if (!_hasDodged && _dodgePrompt)
        {
            _dodgePrompt.SetActive(true);
        }
    }
}