using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PhaseTransitionFX : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("Při kolika zbývajících sekundách začne efekt.")]
    [SerializeField] private float _warningThreshold = 5.0f;
    [SerializeField] private float _volumePriority = 101f;

    [Header("Visual FX")]
    [SerializeField] private Color _vignetteColor = new Color(1f, 0.4f, 0f); // Oranžová/Červená
    [Range(0f, 1f)] [SerializeField] private float _maxVignetteIntensity = 0.35f;

    [Header("Audio FX")]
    [SerializeField] private AudioClip _tickClip;
    [SerializeField] private AudioClip _startClip;
    [Range(0f, 1f)] [SerializeField] private float _audioVolume = 0.8f;

    private Volume _localVolume;
    private VolumeProfile _runtimeProfile;
    private Vignette _vignette;
    private AudioSource _audioSource;

    private float _lastTargetTime = -1f;
    private float _localRemainingSeconds;
    private int _lastTickSecond = -1;
    
    private void Start()
    {
        SetupLocalVolume();
        SetupAudio();
    }

    private void SetupLocalVolume()
    {
        _localVolume = gameObject.AddComponent<Volume>();
        _localVolume.isGlobal = true;
        _localVolume.priority = _volumePriority;

        _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _runtimeProfile.name = "PhaseTransitionFX_RuntimeProfile";
        _localVolume.profile = _runtimeProfile;

        if (!_runtimeProfile.TryGet(out _vignette))
            _vignette = _runtimeProfile.Add<Vignette>(true);

        _vignette.active = true;
        _vignette.intensity.Override(0f);
        _vignette.color.Override(_vignetteColor);
        _vignette.smoothness.Override(0.4f);
    }

    private void SetupAudio()
    {
        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 0f; // 2D zvuk, slyší to jen lokální hráč
    }

    private void Update()
    {
        DirectorSpawner spawner = DirectorSpawner.Instance;
        
        if (spawner == null)
            return;

        // 1. Synchronizace sítě s lokálním plynulým časem
        if (spawner.PhaseEndTimeSecondsNetVar.Value != _lastTargetTime)
        {
            _lastTargetTime = spawner.PhaseEndTimeSecondsNetVar.Value;
            _localRemainingSeconds = _lastTargetTime - spawner.RunTimeSecondsNetVar.Value;
        }

        bool isPaused = spawner.IsSpawningPausedNetVar.Value;

        if (isPaused && _localRemainingSeconds > 0f)
        {
            _localRemainingSeconds -= Time.deltaTime;
            ProcessWarningEffect();
        }
        else
        {
            RecoverFx();
        }
    }

    private void ProcessWarningEffect()
    {
        if (_localRemainingSeconds > _warningThreshold)
        {
            RecoverFx();
            _lastTickSecond = -1; // Reset pro další odpočet
            return;
        }

        // --- VIZUÁL ---
        // Fraction zajistí, že při celé vteřině (např. 4.99) je intenzita nejvyšší a klesá k 0
        float fraction = _localRemainingSeconds % 1f; 
        _vignette.intensity.value = Mathf.Lerp(0f, _maxVignetteIntensity, fraction);

        // --- AUDIO ---
        int currentCeilSecond = Mathf.CeilToInt(_localRemainingSeconds);

        if (currentCeilSecond <= _warningThreshold && currentCeilSecond > 0 && currentCeilSecond != _lastTickSecond)
        {
            _lastTickSecond = currentCeilSecond;
            PlayClip(_tickClip);
        }
        else if (_localRemainingSeconds <= 0f && _lastTickSecond != 0)
        {
            _lastTickSecond = 0;
            PlayClip(_startClip);
            _vignette.intensity.value = 0f; // Okamžitý reset vizuálu při startu
        }
    }

    private void RecoverFx()
    {
        if (_vignette.intensity.value > 0.01f)
        {
            _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, 0f, Time.deltaTime * 5f);
        }
        else
        {
            _vignette.intensity.value = 0f;
        }
    }

    private void PlayClip(AudioClip clip)
    {
        if (clip != null && _audioSource != null)
        {
            _audioSource.PlayOneShot(clip, _audioVolume);
        }
    }

    private void OnDestroy()
    {
        if (_runtimeProfile != null)
        {
            Destroy(_runtimeProfile);
        }
    }
}