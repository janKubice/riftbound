using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkedAudioSource))]
public class ArenaEntrance : NetworkBehaviour
{
    [Header("Nastavení")]
    [SerializeField] private int _entranceID; // 0 až 3

    [Header("Vizuál")]
    [SerializeField] private ParticleSystem _particleSystem;
    [SerializeField] private Color _emptyColor = Color.blue;
    [SerializeField] private Color _readyColor = Color.green;
    [SerializeField] private Color _crowdedColor = Color.red;

    [Header("Audio")]
    [Tooltip("Index zvuku v NetworkedAudioSource pro vstup (např. 0)")]
    [SerializeField] private int _enterSoundIndex = 0;
    [Tooltip("Index zvuku v NetworkedAudioSource pro odchod (např. 1)")]
    [SerializeField] private int _exitSoundIndex = 1;

    private NetworkedAudioSource _netAudio;
    // Počet hráčů v tomto kruhu (Synchronizováno)
    private NetworkVariable<int> _occupantCount = new NetworkVariable<int>(0);

    // Reference na Material particle systému pro změnu barvy
    private ParticleSystem.MainModule _psMain;

    public int EntranceID => _entranceID;

    private void Awake()
    {
        if (_particleSystem != null)
        {
            _psMain = _particleSystem.main;
        }
        _netAudio = GetComponent<NetworkedAudioSource>();
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
        // Klient reaguje na změnu počtu lidí změnou barvy
        _occupantCount.OnValueChanged += (oldVal, newVal) => UpdateVisuals(newVal);

        // Inicializace vizuálu
        UpdateVisuals(_occupantCount.Value);
    }

    public override void OnNetworkDespawn()
    {
        _occupantCount.OnValueChanged -= (oldVal, newVal) => UpdateVisuals(newVal);
    }

    // --- Server Logic ---

    private void OnTriggerEnter(Collider other)
    {
        if (!IsServer) return;

        // Předpokládáme, že hráč má tag "Player" a komponentu NetworkObject
        if (other.CompareTag("Player") && other.TryGetComponent(out NetworkObject netObj))
        {
            _occupantCount.Value++;
            _netAudio.PlayOneShotNetworked(_enterSoundIndex);
            // Pokud je tu právě jeden hráč, zkusíme ho zaregistrovat v Manageru
            if (_occupantCount.Value == 1)
            {
                ArenaManager.Instance.OnPlayerEnteredEntrance(_entranceID, netObj.OwnerClientId);
            }
            // Pokud je jich víc, je to neplatné -> Manager musí vědět, že tento vchod je "špinavý"
            else if (_occupantCount.Value > 1)
            {
                ArenaManager.Instance.OnEntranceOvercrowded(_entranceID);
            }
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsServer) return;

        if (other.CompareTag("Player") && other.TryGetComponent(out NetworkObject netObj))
        {
            _occupantCount.Value = Mathf.Max(0, _occupantCount.Value - 1);
            _netAudio.PlayOneShotNetworked(_exitSoundIndex);

            // Hráč odešel, musíme aktualizovat Managera
            if (_occupantCount.Value == 0)
            {
                // Nikdo tu není -> vchod je prázdný
                ArenaManager.Instance.OnEntranceEmpty(_entranceID);
            }
            else if (_occupantCount.Value == 1)
            {
                // Zůstal tu už jen jeden (předtím bylo crowded) -> Musíme zjistit KDO tu zbyl
                // Poznámka: TriggerExit nám neřekne, kdo zbyl uvnitř, jen kdo odešel.
                // Pro jednoduchost v této fázi: Pokud se stav změní z 2 na 1, 
                // Manager zatím nebude vědět přesné ID zbylého hráče bez složitějšího tracking listu.
                // Vylepšíme v dalším kroku pomocí Listu uvnitř triggeru, pokud to bude nutné.
                // Pro teď: Resetujeme stav v Manageru na "Waiting" (nevalidní), dokud hráč nevystoupí a nenastoupí.
                // (Alternativně lze použít OnTriggerStay nebo List<ulong> insidePlayers)

                // FIX: Pro robustnost v Fázi 1 raději v Manageru odregistrujeme vchod, 
                // dokud se nevyčistí na 0 nebo hráč "re-triggerne".
                ArenaManager.Instance.OnEntranceOvercrowded(_entranceID);
            }
        }
    }

    // --- Client Visuals ---

    private void UpdateVisuals(int count)
    {
        if (_particleSystem == null) return;

        Color targetColor = _emptyColor;

        if (count == 1) targetColor = _readyColor;
        else if (count > 1) targetColor = _crowdedColor;

        _psMain.startColor = targetColor;
    }

    public void SetVisualsActive(bool active)
    {
        if (_particleSystem != null)
        {
            if (active && !_particleSystem.isPlaying) _particleSystem.Play();
            else if (!active && _particleSystem.isPlaying) _particleSystem.Stop();
        }
    }
}