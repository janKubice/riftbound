using UnityEngine;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(NetworkedAudioSource))]
public class DestructibleProp : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("Pokud je > 0, objekt se po zničení po čase obnoví.")]
    [SerializeField] private float _respawnTime = 30.0f;

    [Header("Collision Settings")]
    [Tooltip("Povolit zničení fyzickým nárazem (hráč, kámen, auto...).")]
    [SerializeField] private bool _breakOnCollision = false;
    [Tooltip("Minimální rychlost (síla) nárazu nutná ke zničení.")]
    [SerializeField] private float _collisionThreshold = 2.0f;

    [Header("Visuals")]
    [SerializeField] private GameObject _intactModel;
    [SerializeField] private GameObject _brokenModel;
    [SerializeField] private ParticleSystem _breakVFX;
    [Tooltip("Efekt při obnovení (např. 'poof' obláček). Volitelné.")]
    [SerializeField] private ParticleSystem _respawnVFX;

    [Header("Loot")]
    [SerializeField] private LootTable _lootTable;
    [Range(0f, 1f)][SerializeField] private float _lootChance = 0.3f;

    [Header("Audio")]
    [SerializeField] private NetworkedAudioSource _netAudio;
    [SerializeField] private int _breakSoundIndex = 0;

    private NetworkVariable<bool> _isBroken = new NetworkVariable<bool>(false);

    public override void OnNetworkSpawn()
    {
        _isBroken.OnValueChanged += OnStateChanged;
        UpdateVisuals(_isBroken.Value);
    }

    public override void OnNetworkDespawn()
    {
        _isBroken.OnValueChanged -= OnStateChanged;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServer || !_breakOnCollision || _isBroken.Value) return;

        // Fyzická kolize má relativeVelocity
        CheckImpact(collision.relativeVelocity.magnitude);
    }

    // 2. Veřejná metoda pro CharacterController nebo Raycasty
    public void CheckImpact(float forceMagnitude)
    {
        // Logika běží jen na serveru
        if (!IsServer || !_breakOnCollision || _isBroken.Value) return;

        if (forceMagnitude >= _collisionThreshold)
        {
            TakeHit();
        }
    }

    public void TakeHit()
    {
        if (!IsServer || _isBroken.Value) return;

        _isBroken.Value = true;

        // --- SPAWN LOOTU ---
        if (_lootTable != null && LootManager.Instance != null)
        {
            // Náhoda na drop (pokud není v tabulce 100%)
            if (Random.value < _lootChance)
            {
                LootManager.Instance.SpawnLoot(transform.position + Vector3.up * 0.5f, _lootTable);
            }
        }
        // -------------------

        if (_netAudio != null) _netAudio.PlayOneShotNetworked(_breakSoundIndex);
        if (_respawnTime > 0) StartCoroutine(RespawnRoutine());
    }

    private IEnumerator RespawnRoutine()
    {
        yield return new WaitForSeconds(_respawnTime);
        _isBroken.Value = false;
    }

    private void OnStateChanged(bool oldVal, bool newVal)
    {
        UpdateVisuals(newVal);
    }

    private void UpdateVisuals(bool isBroken)
    {
        if (_intactModel) _intactModel.SetActive(!isBroken);
        if (_brokenModel) _brokenModel.SetActive(isBroken);

        var col = GetComponent<Collider>();
        if (col != null) col.enabled = !isBroken;

        if (isBroken)
        {
            if (_breakVFX != null) _breakVFX.Play();
        }
        else
        {
            if (_respawnVFX != null) _respawnVFX.Play();
        }
    }
}