using UnityEngine;
using Unity.Netcode;

public class StatusEffectDebugger : NetworkBehaviour
{
    public static bool IsMenuOpen { get; private set; }
    [SerializeField] private bool _showDebugMenu = false;
    private StatusEffectData[] _allEffects;
    private StatusEffectReceiver _receiver;

    private void Start()
    {
        if (!IsOwner) 
        {
            Destroy(this); // Debug menu jen pro vlastníka
            return;
        }
        _allEffects = Resources.LoadAll<StatusEffectData>("StatusEffects");
        _receiver = GetComponent<StatusEffectReceiver>();
    }

    private void Update()
    {
        if (UnityEngine.InputSystem.Keyboard.current.f1Key.wasPressedThisFrame)
        {
            _showDebugMenu = !_showDebugMenu;
            IsMenuOpen = _showDebugMenu; // Aktualizace statického stavu

            if (_showDebugMenu)
            {
                // Okamžitě odemknout pro UI
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }
    }

    public override void OnDestroy()
    {
        IsMenuOpen = false;
        base.OnDestroy();
    }

    private void OnGUI()
    {
        if (!_showDebugMenu || _receiver == null) return;

        GUILayout.BeginArea(new Rect(10, 10, 250, 400), "Status Effect Admin", GUI.skin.window);
        
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
        GUILayout.Label($"Aktuální Speed Mult: {_receiver.CurrentSpeedMultiplier:F2}");
        
        if (GUILayout.Button("Vyčistit Vše (Clear)"))
        {
             // Implementovat Clear metodu v Receiveru pokud potřeba
        }

        GUILayout.EndArea();
    }

    [ServerRpc]
    private void ApplyEffectServerRpc(string effectName)
    {
        var data = GameEffectDatabase.GetEffectByName(effectName);
        if (data != null)
        {
            _receiver.ApplyStatusEffect(data);
        }
    }
}