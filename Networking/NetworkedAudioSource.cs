using UnityEngine;
using Unity.Netcode;

// Vyžaduje komponentu AudioSource na stejném objektu
[RequireComponent(typeof(AudioSource))]
public class NetworkedAudioSource : NetworkBehaviour
{
    private AudioSource _audioSource;

    // --- 1. ZVUK VE SMYČCE (Stavový) ---
    [Header("Looping Audio (State-Based)")]
    [Tooltip("Zvuk, který se má přehrávat ve smyčce (např. oheň). Automaticky se nastaví na AudioSource.")]
    [SerializeField] private AudioClip _loopingClip;

    [Tooltip("Pokud je zaškrtnuto, smyčka se spustí automaticky při spawnu objektu (pro všechny).")]
    [SerializeField] private bool _playLoopOnSpawn = false;

    // Synchronizovaný stav: Hraje smyčka?
    // Jen server může zapisovat, všichni mohou číst.
    private NetworkVariable<bool> _isLoopPlaying = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // --- 2. JEDNORÁZOVÉ ZVUKY (Interakce) ---
    [Header("One-Shot Audio (Event-Based)")]
    [Tooltip("Pole zvuků, které lze přehrát jednorázově (např. otevření dveří, kliknutí).")]
    [SerializeField] private AudioClip[] _oneShotClips;


    // --- Inicializace ---

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();

        // Nastavení pro smyčku
        if (_loopingClip != null)
        {
            _audioSource.clip = _loopingClip;
            _audioSource.loop = true; // Vynutíme smyčku pro tento klip
        }
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
        // Přihlásíme se k odběru změny stavu smyčky
        _isLoopPlaying.OnValueChanged += OnLoopStateChanged;

        // Okamžité nastavení stavu pro klienta, který se právě připojil
        // (Pokud je _isLoopPlaying.Value true, rovnou spustíme zvuk)
        OnLoopStateChanged(false, _isLoopPlaying.Value);

        // Pokud jsme server a máme hrát hned, nastavíme stav
        if (IsServer && _playLoopOnSpawn && !_isLoopPlaying.Value)
        {
            _isLoopPlaying.Value = true;
        }
    }

    public override void OnNetworkDespawn()
    {
        // Vždy se odhlásíme z odběru
        _isLoopPlaying.OnValueChanged -= OnLoopStateChanged;
    }

    // --- Logika Smyčky ---

    /// <summary>
    /// Běží na všech klientech, když se změní NetworkVariable _isLoopPlaying.
    /// </summary>
    private void OnLoopStateChanged(bool previousValue, bool newValue)
    {
        if (_audioSource.clip != _loopingClip)
        {
            // Pokud byl klip mezitím změněn (např. jednorázovým zvukem),
            // vrátíme ho zpět.
            _audioSource.clip = _loopingClip;
            _audioSource.loop = true;
        }

        if (newValue)
        {
            if (!_audioSource.isPlaying)
            {
                _audioSource.Play();
            }
        }
        else
        {
            if (_audioSource.isPlaying)
            {
                _audioSource.Stop();
            }
        }
    }

    /// <summary>
    /// Veřejná metoda pro spuštění smyčky. Může být volána z jiného skriptu na serveru.
    /// </summary>
    public void StartLoopingAudio()
    {
        if (!IsServer) return; // Může volat jen server
        _isLoopPlaying.Value = true;
    }

    /// <summary>
    /// Veřejná metoda pro zastavení smyčky. Může být volána z jiného skriptu na serveru.
    /// </summary>
    public void StopLoopingAudio()
    {
        if (!IsServer) return; // Může volat jen server
        _isLoopPlaying.Value = false;
    }


    // --- Logika Jednorázových Zvuků (RPC) ---

    /// <summary>
    /// Veřejná metoda, kterou může zavolat JAKÝKOLI skript (např. interakce)
    /// pro přehrání jednorázového zvuku na všech klientech.
    /// </summary>
    /// <param name="clipIndex">Index zvuku z pole _oneShotClips</param>
    public void PlayOneShotNetworked(int clipIndex)
    {
        // Ověření indexu
        if (clipIndex < 0 || clipIndex >= _oneShotClips.Length || _oneShotClips[clipIndex] == null)
        {
            Debug.LogWarning($"NetworkedAudioSource: Neplatný index zvuku {clipIndex}");
            return;
        }

        // Klient žádá server, aby přehrál zvuk.
        // Nevyžadujeme vlastnictví (RequireOwnership = false), protože hráč může interagovat
        // s objektem (dveře), který nevlastní. Server by měl mít vlastní logiku
        // pro ověření, zda je interakce platná.
        PlayOneShotServerRpc(clipIndex);
    }

    [ServerRpc(RequireOwnership = false)]
    private void PlayOneShotServerRpc(int clipIndex, ServerRpcParams rpcParams = default)
    {
        // Zde může server provést validaci (např. je hráč dost blízko? Může dveře otevřít?)
        // ...

        // Pokud je vše v pořádku, rozešle příkaz všem klientům
        PlayOneShotClientRpc(clipIndex);
    }

    [ClientRpc]
    private void PlayOneShotClientRpc(int clipIndex)
    {
        // Ověření indexu (pro jistotu, i když by měl být validní)
        if (clipIndex < _oneShotClips.Length && _oneShotClips[clipIndex] != null)
        {
            // Použijeme PlayOneShot. To nepřeruší hlavní klip (smyčku),
            // pokud nějaká hraje, jen se přehraje "přes" ni.
            _audioSource.PlayOneShot(_oneShotClips[clipIndex]);
        }
    }
}