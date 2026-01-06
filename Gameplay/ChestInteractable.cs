using UnityEngine;
using Unity.Netcode;

// Vyžadujeme všechny potřebné komponenty na jednom objektu
[RequireComponent(typeof(NetworkObject), typeof(Collider), typeof(NetworkedAudioSource))]
public class ChestInteractable : NetworkBehaviour, IInteractable
{
    [Header("State (Stav)")]
    [Tooltip("Pokud je zaškrtnuto, bedna začne zamčená.")]
    [SerializeField] private bool _startsLocked = false;

    [Header("Prompts (Texty)")]
    [SerializeField] private string _openPrompt = "E - Zavřít";
    [SerializeField] private string _closedPrompt = "E - Otevřít";
    [SerializeField] private string _lockedPrompt = "E - Zamčeno";

    [Header("Audio (Indexy v NetworkedAudioSource)")]
    [Tooltip("Index zvuku pro otevření v poli 'OneShotClips' na NetworkedAudioSource.")]
    [SerializeField] private int _openSoundIndex = 0;

    [Tooltip("Index zvuku pro zavření v poli 'OneShotClips'.")]
    [SerializeField] private int _closeSoundIndex = 1;

    [Tooltip("Index zvuku pro zamčeno v poli 'OneShotClips'.")]
    [SerializeField] private int _lockedSoundIndex = 2;

    [Header("Visuals (Volitelné)")]
    [Tooltip("Animator, který má bool parametr 'IsOpen' pro animaci víka.")]
    [SerializeField] private Animator _animator;
    [SerializeField] private string _animatorBoolName = "IsOpen";

    // --- Síťové Stavy ---
    // Sledujeme, zda je otevřená. Začíná zavřená (false).
    private NetworkVariable<bool> _isOpen = new NetworkVariable<bool>(false);
    // Sledujeme, zda je zamčená.
    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(false);

    // Odkaz na audio komponentu
    private NetworkedAudioSource _networkedAudio;

    private void Awake()
    {
        // Získáme odkaz na audio
        _networkedAudio = GetComponent<NetworkedAudioSource>();
        // Zajistíme, že collider je trigger, aby se hráč nezasekl
        GetComponent<Collider>().isTrigger = true;
    }

    public override void OnDestroy()
    {
        if (NetworkObject != null && NetworkObject.IsSpawned && !NetworkManager.Singleton.IsServer)
        {
            if (!NetworkManager.Singleton.ShutdownInProgress)
            {
                Debug.LogError($"[Security Alert] Objekt {gameObject.name} byl smazán lokálně na klientovi! " +
                               $"To způsobí Invalid Destroy chybu. Prověřte volání v tomto skriptu.");
            }
        }
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        // Server nastaví počáteční stav
        if (IsServer)
        {
            _isLocked.Value = _startsLocked;
            _isOpen.Value = false; // Vždy začínáme zavření
        }

        // Všichni klienti (i server) se přihlásí k odběru změn
        // pro synchronizaci animací.
        _isOpen.OnValueChanged += OnOpenStateChanged;

        // Okamžitě nastavíme správný vizuální stav (zavřené/otevřené víko)
        OnOpenStateChanged(false, _isOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        // Vždy se odhlásíme z odběru
        _isOpen.OnValueChanged -= OnOpenStateChanged;
    }

    // --- Implementace IInteractable ---

    /// <summary>
    /// Toto čte tvůj PlayerInteractor na KLIENTOVI.
    /// Díky NetworkVariable má vždy aktuální stav.
    /// </summary>
    public string InteractionPrompt
    {
        get
        {
            // Stav je synchronizován přes NetworkVariable
            if (_isLocked.Value)
            {
                return _lockedPrompt;
            }

            // Je odemčená, vrátíme text podle toho, zda je otevřená
            return _isOpen.Value ? _openPrompt : _closedPrompt;
        }
    }

    /// <summary>
    /// Toto volá PlayerInteractor na SERVERU.
    /// </summary>
    public void Interact(NetworkObject interactor)
    {
        // Všechen kód zde běží POUZE na serveru

        // 1. Zkontrolujeme zamčení
        if (_isLocked.Value)
        {
            // Bedna je zamčená.
            // Zde by mohla být logika (např. má 'interactor' klíč?)
            // ...

            // Pro teď jen přehrajeme zvuk "zamčeno" pro všechny
            _networkedAudio.PlayOneShotNetworked(_lockedSoundIndex);

            // Interakce končí, nic se neotevře
            return;
        }

        // 2. Není zamčená, takže ji otevřeme/zavřeme
        // Jednoduše invertujeme síťovou proměnnou
        _isOpen.Value = !_isOpen.Value;

        // 3. Přehrajeme správný zvuk
        // NetworkVariable se aktualizuje i na serveru okamžitě,
        // takže můžeme číst novou hodnotu.
        if (_isOpen.Value)
        {
            // Právě se otevřela
            _networkedAudio.PlayOneShotNetworked(_openSoundIndex);

            // Zde by server mohl dát hráči 'interactor' itemy...
        }
        else
        {
            // Právě se zavřela
            _networkedAudio.PlayOneShotNetworked(_closeSoundIndex);
        }
    }

    // --- Vizuální a Audio Logika (Běží na všech klientech) ---

    /// <summary>
    /// Volá se na všech klientech, když se změní _isOpen.Value
    /// </summary>
    private void OnOpenStateChanged(bool previousValue, bool newValue)
    {
        // Aktualizujeme animátor, pokud existuje
        if (_animator != null)
        {
            _animator.SetBool(_animatorBoolName, newValue);
        }
    }
}