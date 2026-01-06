using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerScreenFX : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] private float _volumePriority = 100f;

    [Header("Audio FX")]
    [SerializeField] private AudioClip _heartbeatClip; // Zde přetáhni zvuk tlukotu
    [SerializeField] private float _basePitch = 1.0f;
    [SerializeField] private float _maxPitch = 1.8f; // Rychlost při 0% HP

    [Header("Health FX")]
    [SerializeField] private Color _healthVignetteColor = Color.red;
    [Range(0f, 1f)][SerializeField] private float _healthThreshold = 0.3f;
    [SerializeField] private float _maxAberration = 1.0f;
    [SerializeField] private float _maxVignetteIntensity = 0.5f;

    [Header("Stamina FX")]
    [SerializeField] private Color _staminaVignetteColor = Color.black;
    [Range(0f, 1f)][SerializeField] private float _staminaThreshold = 0.25f;

    // Interní reference
    private Volume _localVolume;
    private Vignette _vignette;
    private ChromaticAberration _aberration;
    private AudioSource _audioSource;
    private PlayerAttributes _attributes;
    private bool _initialized = false;

    private void Start()
    {
        SetupLocalVolume();
        SetupAudio();
        StartCoroutine(WaitForPlayer());
    }

    private void SetupLocalVolume()
    {
        _localVolume = gameObject.AddComponent<Volume>();
        _localVolume.isGlobal = true;
        _localVolume.priority = _volumePriority;

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _localVolume.profile = profile;

        if (!profile.TryGet(out _vignette)) _vignette = profile.Add<Vignette>(true);
        if (!profile.TryGet(out _aberration)) _aberration = profile.Add<ChromaticAberration>(true);

        _vignette.active = true;
        _vignette.intensity.Override(0f);
        _vignette.color.Override(_healthVignetteColor);
        
        _aberration.active = true;
        _aberration.intensity.Override(0f);
    }

    private void SetupAudio()
    {
        if (_heartbeatClip == null) return;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.clip = _heartbeatClip;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.volume = 0f;
        _audioSource.spatialBlend = 0f; // 2D zvuk (hraje přímo do uší)
    }

    private IEnumerator WaitForPlayer()
    {
        yield return new WaitUntil(() => PlayerAttributes.LocalInstance != null);
        _attributes = PlayerAttributes.LocalInstance;
        _initialized = true;
    }

    private void Update()
    {
        if (!_initialized || _attributes == null) return;

        float maxHealth = Mathf.Max(_attributes.MaxHealth.Value, 1f);
        float maxStamina = Mathf.Max(_attributes.MaxStamina.Value, 1f);
        float currentHealthPct = _attributes.CurrentHealth.Value / maxHealth;
        float currentStaminaPct = _attributes.CurrentStamina.Value / maxStamina;

        // --- HEALTH LOGIC ---
        if (currentHealthPct < _healthThreshold)
        {
            float severity = 1.0f - (currentHealthPct / _healthThreshold);
            
            // Visuals
            _vignette.color.value = _healthVignetteColor;
            _vignette.intensity.value = Mathf.Lerp(0, _maxVignetteIntensity, severity) + (Mathf.Sin(Time.time * 10f) * 0.05f);
            _aberration.intensity.value = Mathf.Lerp(0, _maxAberration, severity);

            // Audio
            if (_audioSource != null)
            {
                if (!_audioSource.isPlaying) _audioSource.Play();
                _audioSource.volume = Mathf.Lerp(0f, 1f, severity); // Hlasitost dle zranění
                _audioSource.pitch = Mathf.Lerp(_basePitch, _maxPitch, severity); // Rychlost dle zranění
            }
        }
        // --- STAMINA LOGIC ---
        else if (currentStaminaPct < _staminaThreshold)
        {
            float severity = 1.0f - (currentStaminaPct / _staminaThreshold);

            // Visuals
            _vignette.color.value = _staminaVignetteColor;
            _vignette.intensity.value = Mathf.Lerp(0, 0.45f, severity);
            _aberration.intensity.value = 0f;
            
            // Audio OFF (u staminy tlukot nechceme)
            FadeOutAudio();
        }
        // --- RECOVERY ---
        else
        {
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, 0f, Time.deltaTime * 5f);
            _aberration.intensity.value = Mathf.Lerp(_aberration.intensity.value, 0f, Time.deltaTime * 5f);
            FadeOutAudio();
        }
    }

    private void FadeOutAudio()
    {
        if (_audioSource == null || !_audioSource.isPlaying) return;

        _audioSource.volume = Mathf.Lerp(_audioSource.volume, 0f, Time.deltaTime * 5f);
        if (_audioSource.volume < 0.01f)
        {
            _audioSource.Stop();
        }
    }

    private void OnDestroy()
    {
        if (_localVolume != null && _localVolume.profile != null)
        {
            Destroy(_localVolume.profile);
        }
    }
}