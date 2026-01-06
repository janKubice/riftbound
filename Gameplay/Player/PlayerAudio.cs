using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic; // Pro List

// Tento skript bude na prefabu hráče
[RequireComponent(typeof(AudioSource))]
public class PlayerAudio : NetworkBehaviour
{
    [Header("Data")]
    [SerializeField] private PlayerAudioData _audioData;

    [Header("Komponenty")]
    [SerializeField] private AudioSource _audioSource;

    // Seznam pro snadný přístup přes index
    private List<AudioClip> _audioClipList;

    private void Awake()
    {
        if (_audioSource == null)
        {
            _audioSource = GetComponent<AudioSource>();
        }

        // Důležité: Nastavte AudioSource na 3D zvuk,
        // aby byl zvuk slyšet z místa, kde hráč je.
        _audioSource.spatialBlend = 1.0f;
        _audioSource.playOnAwake = false;

        // Vytvoříme seznam klipů ve specifickém pořadí,
        // abychom mohli posílat jen číslo (index) přes síť.
        InitializeAudioList();
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

    private void InitializeAudioList()
    {
        if (_audioData == null)
        {
            Debug.LogError($"[PlayerAudio] CHYBA: V Inspectoru chybí přiřazený '_audioData'! Objekt: {gameObject.name}");
            // Inicializujeme prázdný list, aby zbytek kódu nepadal na NullReference
            _audioClipList = new List<AudioClip>();
            return;
        }

        // POZOR: Pořadí v tomto seznamu MUSÍ být konzistentní!
        // Pokud přidáte nový zvuk, přidejte ho na konec seznamu.
        _audioClipList = new List<AudioClip>
        {
            _audioData.Jump,         // Index 0
            _audioData.Land,         // Index 1
            _audioData.DodgeSwoosh,  // Index 2
            _audioData.Footstep,     // Index 3
            _audioData.AttackSwing,  // Index 4
            _audioData.HitReceived,  // Index 5
            _audioData.HitDealt,     // Index 6
            _audioData.OutOfStamina, // Index 7
            _audioData.ItemPickup    // Index 8
        };
    }

    /// <summary>
    /// Veřejná metoda, kterou volá LOKÁLNÍ HRÁČ (IsOwner).
    /// Pošle serveru žádost o přehrání zvuku.
    /// </summary>
    /// <param name="clipIndex">Index zvuku v _audioClipList</param>
    [ServerRpc]
    public void RequestPlaySoundServerRpc(int clipIndex)
    {
        // Tento kód běží POUZE NA SERVERU.

        // Server ověří index (bezpečnostní pojistka)
        if (clipIndex < 0 || clipIndex >= _audioClipList.Count)
        {
            Debug.LogError($"[Server] Hráč {OwnerClientId} poslal neplatný audio index: {clipIndex}");
            return;
        }

        // Server nyní řekne VŠEM klientům, aby přehráli tento zvuk
        PlaySoundClientRpc(clipIndex);
    }

    /// <summary>
    /// Tento RPC běží na VŠECH klientech (včetně serveru/hosta).
    /// Finálně přehraje zvuk.
    /// </summary>
    [ClientRpc]
    private void PlaySoundClientRpc(int clipIndex)
    {
        // Tento kód běží na VŠECH klientech
        // (InitializeAudioList() v Awake() zajistila, že _audioClipList existuje)
        AudioClip clipToPlay = _audioClipList[clipIndex];

        if (clipToPlay != null)
        {
            // PlayOneShot přehraje zvuk jednou a nepřeruší zvuk, který už hraje
            _audioSource.PlayOneShot(clipToPlay);
        }
    }

    // --- Pomocné konstanty (pro lepší čitelnost v jiných skriptech) ---
    // Můžeme je použít místo "magických čísel" (0, 1, 2...)
    public const int AUDIO_JUMP = 0;
    public const int AUDIO_LAND = 1;
    public const int AUDIO_DODGE = 2;
    public const int AUDIO_FOOTSTEP = 3;
    public const int AUDIO_ATTACK_SWING = 4;
    public const int AUDIO_HIT_RECEIVED = 5;
    public const int AUDIO_HIT_DEALT = 6;
    public const int AUDIO_OUT_OF_STAMINA = 7;
    public const int AUDIO_ITEM_PICKUP = 8;
}