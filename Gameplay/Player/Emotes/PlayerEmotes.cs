using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

public class PlayerEmotes : NetworkBehaviour
{
    [Header("Seznam Emotů")]
    [Tooltip("Sem přetáhni vytvořené EmoteData objekty")]
    [SerializeField] private List<EmoteData> _availableEmotes;

    [Header("Komponenty")]
    [SerializeField] private Animator _animator;
    [SerializeField] private PlayerAudio _playerAudio; // Volitelné, pokud chceš zvuky

    // Cache pro hashe animátoru (optimalizace)
    private Dictionary<int, int> _emoteHashCache = new Dictionary<int, int>();

    private bool _isEmoting = false;
    public bool IsEmoting => _isEmoting;

    private void Awake()
    {
        if (_animator == null) _animator = GetComponent<Animator>();
        if (_playerAudio == null) _playerAudio = GetComponent<PlayerAudio>();

        // Předpočítáme hashe pro triggery
        for (int i = 0; i < _availableEmotes.Count; i++)
        {
            if (_availableEmotes[i] != null)
            {
                int hash = Animator.StringToHash(_availableEmotes[i].AnimatorTriggerName);
                _emoteHashCache.Add(i, hash);
            }
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

    /// <summary>
    /// Volá PlayerController při stisku klávesy (1, 2, 3...)
    /// </summary>
    public void TryPlayEmote(int emoteIndex)
    {
        if (!IsOwner) return;

        // Kontrola indexu
        if (emoteIndex < 0 || emoteIndex >= _availableEmotes.Count) return;

        // Zde by mohla být kontrola staminy, cooldownu atd.

        PlayEmoteServerRpc(emoteIndex);
    }

    /// <summary>
    /// Zruší emote (např. při pohybu)
    /// </summary>
    public void CancelEmote()
    {
        if (!IsOwner || !_isEmoting) return;

        // Resetujeme stav - v Animatoru to většinou řeší přechod do "Locomotion"
        // ale můžeme poslat signál pro jistotu.
        _isEmoting = false;

        // Volitelně: Můžeš mít trigger "CancelEmote" v animatoru
        // CancelEmoteServerRpc(); 
    }

    [ServerRpc]
    private void PlayEmoteServerRpc(int index)
    {
        PlayEmoteClientRpc(index);
    }

    [ClientRpc]
    private void PlayEmoteClientRpc(int index)
    {
        if (index >= _availableEmotes.Count) return;

        EmoteData data = _availableEmotes[index];

        // 1. Spustit Animaci
        if (_emoteHashCache.TryGetValue(index, out int hash))
        {
            _animator.SetTrigger(hash);
            _isEmoting = true;
        }

        // 2. Přehrát zvuk (pokud je a jsme v dosahu)
        /* Pokud máš PlayerAudio upravené pro jednorázové klipy, můžeš to propojit zde:
         * if (data.EmoteSound != null && _playerAudio != null) { ... }
         */
    }

    // Voláno z Update v PlayerControlleru - pokud se hýbeme, vypneme flag
    public void SetEmotingState(bool state)
    {
        _isEmoting = state;
    }
}