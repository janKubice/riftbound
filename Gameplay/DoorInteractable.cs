using System.Collections;
using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedAudioSource))]
public class DoorInteractable : NetworkBehaviour, IInteractable
{
    [Header("Rotation Settings")]
    [Tooltip("Objekt, kterým se má otáčet. Pokud je prázdné, točí se tento objekt.")]
    [SerializeField] private Transform _hingeTransform;
    
    [Tooltip("Úhel při zavření (obvykle 0).")]
    [SerializeField] private float _closedAngle = 0f;
    
    [Tooltip("Úhel při otevření (např. 90 nebo -90).")]
    [SerializeField] private float _openAngle = 90f;
    
    [Tooltip("Rychlost otáčení (stupně za sekundu).")]
    [SerializeField] private float _rotationSpeed = 90f;

    [Header("Lock Settings")]
    [Tooltip("Pokud je zaškrtnuto, dveře začnou zamčená.")]
    [SerializeField] private bool _startsLocked = false;
    [SerializeField] private string _lockedPrompt = "E - Zamčeno";
    [Tooltip("Index zvuku pro zamčeno v poli 'OneShotClips' na NetworkedAudioSource.")]
    [SerializeField] private int _lockedSoundIndex = 3;

    [Header("Audio Settings")]
    [Tooltip("Index zvuku pro OTEVŘENÍ (vrzání/pohyb).")]
    [SerializeField] private int _openSoundIndex = 0;

    [Tooltip("Index zvuku pro ZAČÁTEK zavírání.")]
    [SerializeField] private int _closeStartSoundIndex = 1;

    [Tooltip("Index zvuku pro DOZAVŘENÍ (klapnutí do rámu).")]
    [SerializeField] private int _closeFinishSoundIndex = 2;

    [Header("Interaction Config")]
    [SerializeField] private string _promptOpen = "E - Otevřít";
    [SerializeField] private string _promptClose = "E - Zavřít";

    // --- State ---
    private NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(false);

    private NetworkedAudioSource _networkedAudio;
    
    // Pro lokální plynulý pohyb
    private float _currentAngle;

    private void Awake()
    {
        _networkedAudio = GetComponent<NetworkedAudioSource>();
        
        // Pokud není přiřazen pant, použijeme transform tohoto objektu
        if (_hingeTransform == null) _hingeTransform = transform;

        // Nastavíme výchozí rotaci
        _currentAngle = _closedAngle;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            _isOpen.Value = false;
            _isLocked.Value = _startsLocked;
        }
        
        // Inicializace pozice podle aktuálního stavu při spawnu
        float target = _isOpen.Value ? _openAngle : _closedAngle;
        _currentAngle = target;
        ApplyRotation(_currentAngle);
    }

    private void Update()
    {
        // Klient i Server: Plynule interpolovat rotaci k cílovému úhlu
        float target = _isOpen.Value ? _openAngle : _closedAngle;

        // Pokud nejsme na cílové rotaci, pohneme se
        if (Mathf.Abs(_currentAngle - target) > 0.01f)
        {
            // MoveTowards zajistí lineární, mechanický pohyb (vhodné pro dveře)
            _currentAngle = Mathf.MoveTowards(_currentAngle, target, _rotationSpeed * Time.deltaTime);
            ApplyRotation(_currentAngle);
        }
    }

    private void ApplyRotation(float yAngle)
    {
        // Zachováme původní X a Z, měníme jen Y
        Vector3 rot = _hingeTransform.localEulerAngles;
        // EulerAngles mají svá úskalí, pro jednoduché dveře je bezpečnější nastavit localRotation přímo
        _hingeTransform.localRotation = Quaternion.Euler(rot.x, yAngle, rot.z);
    }

    // --- IInteractable Implementation ---

    public string InteractionPrompt
    {
        get
        {
            if (_isLocked.Value) return _lockedPrompt;
            return _isOpen.Value ? _promptClose : _promptOpen;
        }
    }

    public void Interact(NetworkObject interactor)
    {
        // Logika pouze na serveru
        
        // 1. Kontrola zamčení
        if (_isLocked.Value)
        {
            _networkedAudio.PlayOneShotNetworked(_lockedSoundIndex);
            return;
        }

        // 2. Otevření / Zavření
        if (_isOpen.Value)
        {
            CloseDoorLogic();
        }
        else
        {
            OpenDoorLogic();
        }
    }

    // --- Server Logic ---

    // Veřejná metoda pro odemčení/zamčení (např. klíčem nebo pákou)
    public void SetLocked(bool locked)
    {
        if (IsServer)
        {
            _isLocked.Value = locked;
        }
    }

    private void OpenDoorLogic()
    {
        _isOpen.Value = true;
        _networkedAudio.PlayOneShotNetworked(_openSoundIndex);
    }

    private void CloseDoorLogic()
    {
        _isOpen.Value = false;
        _networkedAudio.PlayOneShotNetworked(_closeStartSoundIndex);

        // Spustíme Coroutine pro zvuk bouchnutí na konci
        float travelDistance = Mathf.Abs(_openAngle - _closedAngle);
        float duration = travelDistance / _rotationSpeed;

        StartCoroutine(PlaySlamSoundDelayed(duration));
    }

    private IEnumerator PlaySlamSoundDelayed(float delay)
    {
        // Počkáme dobu trvání pohybu
        yield return new WaitForSeconds(delay);

        // Pokud jsou dveře stále zavřené (hráč je neotevřel v průběhu zavírání)
        if (!_isOpen.Value)
        {
            _networkedAudio.PlayOneShotNetworked(_closeFinishSoundIndex);
        }
    }
}