using UnityEngine;
using UnityEngine.Audio; // Pokud používáš AudioMixer

[RequireComponent(typeof(AudioSource))]
public class UIAudioManager : PersistentSingleton<UIAudioManager>
{
    [Header("Defaults")]
    [SerializeField] private AudioClip _defaultClickSound;
    [SerializeField] private AudioClip _defaultHoverSound;

    private AudioSource _audioSource;

    protected override void Awake()
    {
        base.Awake();
        _audioSource = GetComponent<AudioSource>();
        
        // UI zvuky by měly být 2D (Spatial Blend = 0)
        _audioSource.spatialBlend = 0f; 
        _audioSource.playOnAwake = false;
    }

    public void PlayClick(AudioClip clip = null)
    {
        PlaySound(clip != null ? clip : _defaultClickSound);
    }

    public void PlayHover(AudioClip clip = null)
    {
        PlaySound(clip != null ? clip : _defaultHoverSound);
    }

    private void PlaySound(AudioClip clip)
    {
        if (clip == null) return;
        
        // Randomizace pitche pro přirozenější pocit (volitelné)
        _audioSource.pitch = Random.Range(0.95f, 1.05f);
        _audioSource.PlayOneShot(clip);
    }
}