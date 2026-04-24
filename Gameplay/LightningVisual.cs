using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(AudioSource))]
public class LightningVisual : NetworkBehaviour
{
    [Header("Settings")]
    [Tooltip("Za jak dlouho se má objekt smazat ze sítě (v sekundách)")]
    [SerializeField] private float _lifeTime = 2f;
    [SerializeField] private AudioClip _strikeSound;
    [SerializeField] private ParticleSystem[] _lightningParticles;

    private AudioSource _audioSource;

    private void Awake()
    {
        _audioSource = GetComponent<AudioSource>();
    }

    public override void OnNetworkSpawn()
    {
        // Přehrání efektů probíhá lokálně u každého klienta (minimalizace síťového provozu)
        PlayEffects();

        // Úklid paměti a sítě řídí výhradně server
        if (IsServer)
        {
            Invoke(nameof(DespawnObject), _lifeTime);
        }
    }

    private void PlayEffects()
    {
        if (_strikeSound != null)
        {
            _audioSource.PlayOneShot(_strikeSound);
        }

        if (_lightningParticles != null)
        {
            foreach (var ps in _lightningParticles)
            {
                if (ps != null) ps.Play();
            }
        }
    }

    private void DespawnObject()
    {
        if (IsServer && NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn(true);
        }
    }
}