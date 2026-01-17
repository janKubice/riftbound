using UnityEngine;
using Unity.Netcode;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class StatusEffectDebugger : NetworkBehaviour
{
    public static bool IsMenuOpen { get; private set; }
    
    // Defaultně vypnuto, SerializeField necháme pro debug, ale v Start to přepíšeme
    [SerializeField] private bool _showDebugMenu = false; 
    
    private StatusEffectData[] _allEffects;
    private StatusEffectReceiver _receiver;

    private void Start()
    {
        // 1. BEZPEČNOST: Pokud nejsme v Editoru a nejsme Owner, pryč s tím.
        // V ostrém buildu tento script nechceme vůbec (nebo jen pro adminy, dle potřeby).
        if (!Application.isEditor && !Debug.isDebugBuild)
        {
            Destroy(this);
            return;
        }

        if (!IsOwner) 
        {
            Destroy(this);
            return;
        }

        _showDebugMenu = false; // Vždy začít se zavřeným menu
        IsMenuOpen = false;

        _allEffects = Resources.LoadAll<StatusEffectData>("StatusEffects");
        _receiver = GetComponent<StatusEffectReceiver>();
    }

    private void Update()
    {
        // Kontrola pouze v Editoru nebo Debug buildu
        if (!Application.isEditor && !Debug.isDebugBuild) return;

        if (UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
        {
            ToggleMenu();
        }
    }

    private void ToggleMenu()
    {
        _showDebugMenu = !_showDebugMenu;
        IsMenuOpen = _showDebugMenu;

        if (_showDebugMenu)
        {
            // OTEVŘENO: Odemknout myš, povolit kurzor
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            
            // Volitelné: Zde můžeš zavolat metodu na PlayerControlleru pro zamknutí pohybu
            // GetComponent<PlayerController>().SetInputLocked(true);
        }
        else
        {
            // ZAVŘENO: Zamknout myš zpět (TOTO CHYBĚLO)
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            
            // Volitelné: Odemknout pohyb
            // GetComponent<PlayerController>().SetInputLocked(false);
        }
    }

    public override void OnDestroy()
    {
        IsMenuOpen = false;
        base.OnDestroy();
    }

    private void OnGUI()
    {
        // Vykreslovat jen pokud je menu aktivní
        if (!_showDebugMenu || _receiver == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 250, 400), "Status Effect Admin (F1)", GUI.skin.window);
        
        GUILayout.Label("Aplikovat Efekt:");

        if (_allEffects != null)
        {
            foreach (var effect in _allEffects)
            {
                GUILayout.BeginHorizontal();
                if (GUILayout.Button(effect.EffectName))
                {
                    ApplyEffectServerRpc(effect.EffectName);
                }
                GUILayout.EndHorizontal();
            }
        }
        
        GUILayout.Space(10);
        if (_receiver != null)
        {
            GUILayout.Label($"Speed Mult: {_receiver.CurrentSpeedMultiplier:F2}");
        }
        
        if (GUILayout.Button("Zavřít"))
        {
            ToggleMenu();
        }

        GUILayout.EndArea();
    }

    [ServerRpc]
    private void ApplyEffectServerRpc(string effectName)
    {
        // ... (beze změny) ...
        var data = GameEffectDatabase.GetEffectByName(effectName); // Předpokládám existenci této statické třídy z kontextu
        if (data != null)
        {
            _receiver.ApplyStatusEffect(data);
        }
    }
}