using UnityEngine;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedAudioSource))]
public class GateInteractable : NetworkBehaviour, IInteractable
{
    [Header("Movement Settings")]
    [Tooltip("Objekt mříže, který se má hýbat.")]
    [SerializeField] private Transform _gateVisuals;

    [Tooltip("Směr a vzdálenost otevření (lokálně). Pro pohyb dolů nastavte např. Y = -3.")]
    [SerializeField] private Vector3 _openOffset = new Vector3(0, -3, 0);

    [Tooltip("Rychlost pohybu (jednotky za sekundu).")]
    [SerializeField] private float _moveSpeed = 2.0f;

    [Header("Audio Settings")]
    [Tooltip("Index zvuku pro pohyb brány.")]
    [SerializeField] private int _moveSoundIndex = 0;
    
    [Tooltip("Index zvuku pro dopad/zastavení.")]
    [SerializeField] private int _stopSoundIndex = 1;

    [Header("Interaction Config")]
    [SerializeField] private string _promptOpen = "E - Otevřít bránu";
    [SerializeField] private string _promptClose = "E - Zavřít bránu";

    // --- State ---
    private NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(false);
    private NetworkedAudioSource _networkedAudio;

    // Uložené pozice
    private Vector3 _closedPosition;
    private Vector3 _targetOpenPosition;

    private void Awake()
    {
        _networkedAudio = GetComponent<NetworkedAudioSource>();

        if (_gateVisuals == null) _gateVisuals = transform;

        // Uložíme výchozí (zavřenou) pozici
        _closedPosition = _gateVisuals.localPosition;
        // Vypočítáme cílovou (otevřenou) pozici
        _targetOpenPosition = _closedPosition + _openOffset;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _isOpen.Value = false;
        }

        // Okamžitá synchronizace pozice při spawnu (bez lerpu)
        SnapToState();
    }

    private void Update()
    {
        // Klient i Server: Plynulá interpolace pozice
        Vector3 targetPos = _isOpen.Value ? _targetOpenPosition : _closedPosition;

        // Pokud nejsme v cíli, pohneme se
        if (Vector3.Distance(_gateVisuals.localPosition, targetPos) > 0.01f)
        {
            _gateVisuals.localPosition = Vector3.MoveTowards(
                _gateVisuals.localPosition, 
                targetPos, 
                _moveSpeed * Time.deltaTime
            );
        }
    }

    private void SnapToState()
    {
        Vector3 targetPos = _isOpen.Value ? _targetOpenPosition : _closedPosition;
        _gateVisuals.localPosition = targetPos;
    }

    // --- IInteractable Implementation ---

    public string InteractionPrompt => _isOpen.Value ? _promptClose : _promptOpen;

    public void Interact(NetworkObject interactor)
    {
        // Logika pouze na serveru
        // Jednoduchý toggle stavu
        bool newState = !_isOpen.Value;
        _isOpen.Value = newState;

        // Přehrát zvuk začátku pohybu
        _networkedAudio.PlayOneShotNetworked(_moveSoundIndex);

        // Volitelně: Naplánovat zvuk dopadu
        float distance = Vector3.Distance(_gateVisuals.localPosition, newState ? _targetOpenPosition : _closedPosition);
        float duration = distance / _moveSpeed;
        StartCoroutine(PlayStopSoundDelayed(duration));
    }

    private IEnumerator PlayStopSoundDelayed(float delay)
    {
        yield return new WaitForSeconds(delay);
        // Přehrajeme zvuk bouchnutí/dojezdu
        _networkedAudio.PlayOneShotNetworked(_stopSoundIndex);
    }
}