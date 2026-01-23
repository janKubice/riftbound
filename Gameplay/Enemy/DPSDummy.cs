using UnityEngine;
using Unity.Netcode;
using TMPro;

[RequireComponent(typeof(EnemyHealth))]
public class DPSDummy : NetworkBehaviour
{
    [Header("UI Reference")]
    [Tooltip("World Space Canvas TextMeshPro nad hlavou panáka")]
    [SerializeField] private TextMeshProUGUI _dpsText;
    [SerializeField] private GameObject _uiCanvasRoot; // Pro vypínání/zapínání celého UI

    [Header("Settings")]
    [SerializeField] private float _resetTime = 15.0f;

    // --- Server Logic Data ---
    private float _damageAccumulated = 0f;
    private float _combatStartTime = 0f;
    private float _lastHitTime = 0f;
    private bool _isCombatActive = false;

    // Synchronizovaná hodnota DPS pro klienty
    private NetworkVariable<float> _currentDps = new NetworkVariable<float>(0f);

    private EnemyHealth _health;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        if (_uiCanvasRoot) _uiCanvasRoot.SetActive(false); // Začínáme skryté
    }

    public override void OnNetworkSpawn()
    {
        // Klient sleduje změnu DPS a aktualizuje text
        _currentDps.OnValueChanged += UpdateDpsUI;

        if (IsServer)
        {
            _health.OnDamageTaken += OnDamageReceived;
        }
    }

    public override void OnNetworkDespawn()
    {
        _currentDps.OnValueChanged -= UpdateDpsUI;
        if (IsServer)
        {
            _health.OnDamageTaken -= OnDamageReceived;
        }
    }

    private void Update()
    {
        // Billboarding UI (aby text koukal na kameru) - Běží na klientovi
        if (_uiCanvasRoot != null && _uiCanvasRoot.activeSelf && Camera.main != null)
        {
            _uiCanvasRoot.transform.rotation = Quaternion.LookRotation(_uiCanvasRoot.transform.position - Camera.main.transform.position);
        }

        if (!IsServer) return;

        // --- Server Logic: Timeout ---
        if (_isCombatActive)
        {
            if (Time.time > _lastHitTime + _resetTime)
            {
                ResetDPS();
            }
        }
    }

    private void OnDamageReceived(int damage)
    {
        if (!_isCombatActive)
        {
            // První úder - startujeme boj
            _isCombatActive = true;
            _combatStartTime = Time.time;
            _damageAccumulated = 0;
            // Zobrazíme UI na klientech (pomocí změny NetworkVariable, např -1 -> 0, nebo přes ClientRpc)
            // Zde to řešíme prostě tak, že pokud je DPS > 0, UI se ukáže v UpdateDpsUI
        }

        _lastHitTime = Time.time;
        _damageAccumulated += damage;

        // Výpočet DPS
        float duration = Time.time - _combatStartTime;
        if (duration < 1.0f) duration = 1.0f; // Abychom nedělili nulou nebo malým číslem na začátku

        _currentDps.Value = _damageAccumulated / duration;
    }

    private void ResetDPS()
    {
        _isCombatActive = false;
        _damageAccumulated = 0;
        _currentDps.Value = 0f; // 0 vypne UI
    }

    // --- Client Visuals ---

    private void UpdateDpsUI(float oldVal, float newVal)
    {
        if (_dpsText == null || _uiCanvasRoot == null) return;

        if (newVal > 0.1f)
        {
            if (!_uiCanvasRoot.activeSelf) _uiCanvasRoot.SetActive(true);
            _dpsText.text = $"DPS: {newVal:F1}\nTotal: {_damageAccumulated:F0}"; 
            // Poznámka: _damageAccumulated není syncnuté, pokud chceš zobrazovat i Total Damage na klientovi,
            // musíš přidat druhou NetworkVariable<float> TotalDamage.
            // Pro jednoduchost zde ukazuji jen DPS, nebo přepíšu text jen na DPS:
            _dpsText.text = $"DPS: <color=yellow>{newVal:F1}</color>";
        }
        else
        {
            _uiCanvasRoot.SetActive(false);
        }
    }
}