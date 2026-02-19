using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic; // Potřeba pro List

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedAudioSource))]
public class DestructibleProp : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("Pokud je > 0, objekt se po zničení po čase obnoví.")]
    [SerializeField] private float _respawnTime = 30.0f;

    [Header("Collision Settings")]
    [Tooltip("Povolit zničení fyzickým nárazem.")]
    [SerializeField] private bool _breakOnCollision = false;
    [SerializeField] private float _collisionThreshold = 2.0f;

    [Header("Visuals")]
    // _intactModel JE PRYČ - skript si ho najde sám
    
    [Tooltip("Volitelné. Pokud je prázdné, objekt prostě zmizí.")]
    [SerializeField] private GameObject _brokenModel; 
    
    [SerializeField] private GameObject _breakVFXPrefab;
    [SerializeField] private GameObject _respawnVFXPrefab;

    [Header("Loot")]
    [SerializeField] private LootTable _lootTable;
    [Range(0f, 1f)][SerializeField] private float _lootChance = 0.3f;

    [Header("Audio")]
    [SerializeField] private NetworkedAudioSource _netAudio;
    [SerializeField] private int _breakSoundIndex = 0;

    private NetworkVariable<bool> _isBroken = new NetworkVariable<bool>(false);

    // Seznamy komponent, které budeme vypínat/zapínat
    private List<Renderer> _intactRenderers = new List<Renderer>();
    private List<Collider> _intactColliders = new List<Collider>();

    private void Awake()
    {
        // 1. Najdeme všechny renderery a collidery na tomto objektu a dětech
        var allRenderers = GetComponentsInChildren<Renderer>(true);
        var allColliders = GetComponentsInChildren<Collider>(true);

        if (_netAudio == null)
        {
            _netAudio = GetComponent<NetworkedAudioSource>();
        }

        // 2. Filtrujeme je. 
        // Pokud máme _brokenModel, nesmíme do seznamu "intact" přidat jeho části.
        foreach (var r in allRenderers)
        {
            // Pokud je to ParticleSystem renderer (VFX), ignorujeme ho
            if (r is ParticleSystemRenderer) continue;

            // Pokud máme broken model a tento renderer je jeho součástí, ignorujeme ho
            if (_brokenModel != null && r.transform.IsChildOf(_brokenModel.transform)) continue;

            _intactRenderers.Add(r);
        }

        foreach (var c in allColliders)
        {
            // Pokud máme broken model a tento collider je jeho součástí, ignorujeme ho
            if (_brokenModel != null && c.transform.IsChildOf(_brokenModel.transform)) continue;

            _intactColliders.Add(c);
        }

        // Pokud je nastaven broken model, ujistíme se, že je na začátku vypnutý
        if (_brokenModel != null) _brokenModel.SetActive(false);
    }

    public override void OnNetworkSpawn()
    {
        _isBroken.OnValueChanged += OnStateChanged;
        // Inicializace stavu (bez přehrání efektů)
        UpdateVisuals(_isBroken.Value, playVFX: false);
    }

    public override void OnNetworkDespawn()
    {
        _isBroken.OnValueChanged -= OnStateChanged;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !_breakOnCollision || _isBroken.Value) return;
        CheckImpact(collision.relativeVelocity.magnitude);
    }

    public void CheckImpact(float forceMagnitude)
    {
        if (!IsServer || !_breakOnCollision || _isBroken.Value) return;
        if (forceMagnitude >= _collisionThreshold) TakeHit();
    }

    public void TakeHit()
    {
        if (!IsServer || _isBroken.Value) return;

        _isBroken.Value = true;

        // Loot logic...
        if (_lootTable != null) // && LootManager...
        {
             if (Random.value < _lootChance)
             {
                 // Spawn loot
             }
        }

        if (_netAudio != null) _netAudio.PlayOneShotNetworked(_breakSoundIndex);
        if (_respawnTime > 0) StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        // Protože nevypínáme tento GameObject, coroutina bezpečně běží
        yield return new WaitForSeconds(_respawnTime);
        _isBroken.Value = false;
    }

    private void OnStateChanged(bool oldVal, bool newVal)
    {
        UpdateVisuals(newVal, playVFX: true);
    }

    private void UpdateVisuals(bool isBroken, bool playVFX)
    {
        // 1. Změníme viditelnost "Intact" částí (původní objekt)
        foreach (var r in _intactRenderers)
        {
            if (r != null) r.enabled = !isBroken;
        }

        // 2. Změníme kolize "Intact" částí
        foreach (var c in _intactColliders)
        {
            if (c != null) c.enabled = !isBroken;
        }

        // 3. Pokud existuje broken model, aktivujeme ho
        if (_brokenModel != null)
        {
            _brokenModel.SetActive(isBroken);
        }

        // 4. VFX Efekty
        if (!playVFX) return;

        if (isBroken)
        {
            SpawnVFX(_breakVFXPrefab);
        }
        else
        {
            SpawnVFX(_respawnVFXPrefab);
        }
    }

    private void SpawnVFX(GameObject prefab)
    {
        if (prefab == null) return;
        GameObject instance = Instantiate(prefab, transform.position, transform.rotation);
        
        float duration = 2.0f;
        ParticleSystem ps = instance.GetComponent<ParticleSystem>();
        if (ps != null)
        {
            if (!ps.main.loop) duration = ps.main.duration + ps.main.startLifetime.constantMax;
            else duration = 3.0f;
        }
        
        AudioSource audio = instance.GetComponent<AudioSource>();
        if (audio != null && audio.clip != null) duration = Mathf.Max(duration, audio.clip.length);

        Destroy(instance, duration);
    }
}