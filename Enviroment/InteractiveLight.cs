using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkedAudioSource))]
public class InteractiveLight : NetworkBehaviour, IInteractable
{
    [Header("Visuals")]
    [SerializeField] private GameObject _fireParticles; 
    [SerializeField] private Light _lightSource;

    [Header("Audio Settings")]
    [Tooltip("Standardní AudioSource pro smyčku (např. praskání ohně). Nastav v inspektoru 'Loop' na true.")]
    [SerializeField] private AudioSource _loopingAudioSource;
    
    [Tooltip("Zvuk zapnutí/vypnutí (One Shot). Index v NetworkedAudioSource.")]
    [SerializeField] private int _toggleSoundIndex = 0;

    [Header("State")]
    [SerializeField] private bool _startOn = true;

    private NetworkVariable<bool> _isOn = new NetworkVariable<bool>();
    private NetworkedAudioSource _netAudio;

    private void Awake()
    {
        _netAudio = GetComponent<NetworkedAudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _isOn.Value = _startOn;
        }

        _isOn.OnValueChanged += OnStateChanged;
        
        // Inicializace stavu při spawnu
        UpdateVisuals(_isOn.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isOn.OnValueChanged -= OnStateChanged;
    }

    public string InteractionPrompt => _isOn.Value ? "E - Uhasit" : "E - Zapálit";

    public void Interact(NetworkObject interactor)
    {
        if (!IsServer) return;

        // Přepnutí stavu
        _isOn.Value = !_isOn.Value;
        
        // Přehrání zvuku "cvaknutí" (One Shot)
        _netAudio.PlayOneShotNetworked(_toggleSoundIndex);
    }

    private void OnStateChanged(bool oldVal, bool newVal)
    {
        UpdateVisuals(newVal);
    }

    private void UpdateVisuals(bool isOn)
    {
        // 1. Vizuály
        if (_fireParticles) _fireParticles.SetActive(isOn);
        if (_lightSource) _lightSource.enabled = isOn;

        // 2. Audio Smyčka (lokální ovládání na každém klientovi)
        if (_loopingAudioSource != null)
        {
            if (isOn)
            {
                if (!_loopingAudioSource.isPlaying) _loopingAudioSource.Play();
            }
            else
            {
                _loopingAudioSource.Stop();
            }
        }
    }
}