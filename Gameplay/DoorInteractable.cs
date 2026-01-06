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

    public string InteractionPrompt => _isOpen.Value ? _promptClose : _promptOpen;

    public void Interact(NetworkObject interactor)
    {
        // Logika pouze na serveru
        if (_isOpen.Value)
        {
            // ZAVÍRÁME
            CloseDoorLogic();
        }
        else
        {
            // OTEVÍRÁME
            OpenDoorLogic();
        }
    }

    // --- Server Logic ---

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
        // Vypočítáme čas, jak dlouho to potrvá: čas = dráha / rychlost
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