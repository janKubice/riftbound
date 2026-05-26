using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerScreenFX : MonoBehaviour
{
    [Header("Local Player Safety")]
    [SerializeField] private bool _onlyRunForLocalPlayer = true;

    [Tooltip(
        "Zapnuto = script poběží jen tehdy, když je PlayerAttributes.LocalInstance ve stejné root hierarchii jako tento objekt. " +
        "Vypnout pouze pokud je tento script na samostatné CameraRig hierarchii mimo player prefab."
    )]
    [SerializeField] private bool _requireLocalAttributesInThisHierarchy = true;

    [Header("Settings")]
    [SerializeField] private float _volumePriority = 100f;

    [Header("Audio FX")]
    [SerializeField] private AudioClip _heartbeatClip;
    [SerializeField] private float _basePitch = 1.0f;
    [SerializeField] private float _maxPitch = 1.8f;

    [Header("Health FX")]
    [SerializeField] private Color _healthVignetteColor = Color.red;
    [Range(0f, 1f)] [SerializeField] private float _healthThreshold = 0.3f;

    [Tooltip("Doporučuji spíš 0.15 až 0.35. Hodnota 1.0 je extrémní.")]
    [Range(0f, 1f)] [SerializeField] private float _maxAberration = 0.25f;

    [Range(0f, 1f)] [SerializeField] private float _maxVignetteIntensity = 0.5f;

    [Header("Stamina FX")]
    [SerializeField] private Color _staminaVignetteColor = Color.black;
    [Range(0f, 1f)] [SerializeField] private float _staminaThreshold = 0.25f;
    [Range(0f, 1f)] [SerializeField] private float _maxStaminaVignetteIntensity = 0.35f;

    private Volume _localVolume;
    private VolumeProfile _runtimeProfile;
    private Vignette _vignette;
    private ChromaticAberration _aberration;
    private AudioSource _audioSource;
    private PlayerAttributes _attributes;

    private bool _initialized;

    private IEnumerator Start()
    {
        yield return new WaitUntil(() => PlayerAttributes.LocalInstance != null);

        _attributes = PlayerAttributes.LocalInstance;

        if (_onlyRunForLocalPlayer && _requireLocalAttributesInThisHierarchy)
        {
            bool sameHierarchy = IsSameRootOrChild(_attributes.transform, transform);

            if (!sameHierarchy)
            {
                enabled = false;
                yield break;
            }
        }

        SetupLocalVolume();
        SetupAudio();

        _initialized = true;
    }

    private static bool IsSameRootOrChild(Transform localAttributesTransform, Transform effectTransform)
    {
        if (localAttributesTransform == null || effectTransform == null)
            return false;

        if (localAttributesTransform.root == effectTransform.root)
            return true;

        if (localAttributesTransform.IsChildOf(effectTransform))
            return true;

        if (effectTransform.IsChildOf(localAttributesTransform))
            return true;

        return false;
    }

    private void SetupLocalVolume()
    {
        _localVolume = gameObject.AddComponent<Volume>();
        _localVolume.isGlobal = true;
        _localVolume.priority = _volumePriority;

        _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _runtimeProfile.name = "PlayerScreenFX_RuntimeProfile";
        _localVolume.profile = _runtimeProfile;

        if (!_runtimeProfile.TryGet(out _vignette))
            _vignette = _runtimeProfile.Add<Vignette>(true);

        if (!_runtimeProfile.TryGet(out _aberration))
            _aberration = _runtimeProfile.Add<ChromaticAberration>(true);

        _vignette.active = true;
        _vignette.intensity.Override(0f);
        _vignette.color.Override(_healthVignetteColor);
        _vignette.smoothness.Override(0.45f);

        _aberration.active = true;
        _aberration.intensity.Override(0f);
    }

    private void SetupAudio()
    {
        if (_heartbeatClip == null)
            return;

        _audioSource = gameObject.AddComponent<AudioSource>();
        _audioSource.clip = _heartbeatClip;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.volume = 0f;
        _audioSource.spatialBlend = 0f;
    }

    private void Update()
    {
        if (!_initialized || _attributes == null)
            return;

        float maxHealth = Mathf.Max(_attributes.MaxHealth.Value, 1f);
        float maxStamina = Mathf.Max(_attributes.MaxStamina.Value, 1f);

        float currentHealthPct = Mathf.Clamp01(_attributes.CurrentHealth.Value / maxHealth);
        float currentStaminaPct = Mathf.Clamp01(_attributes.CurrentStamina.Value / maxStamina);

        if (currentHealthPct < _healthThreshold)
        {
            ApplyHealthFx(currentHealthPct);
        }
        else if (currentStaminaPct < _staminaThreshold)
        {
            ApplyStaminaFx(currentStaminaPct);
        }
        else
        {
            RecoverFx();
        }
    }

    private void ApplyHealthFx(float currentHealthPct)
    {
        float severity = 1.0f - (currentHealthPct / Mathf.Max(0.001f, _healthThreshold));
        severity = Mathf.Clamp01(severity);

        float pulse = Mathf.Sin(Time.time * 10f) * 0.035f * severity;

        _vignette.color.value = _healthVignetteColor;
        _vignette.intensity.value = Mathf.Clamp01(Mathf.Lerp(0f, _maxVignetteIntensity, severity) + pulse);
        _aberration.intensity.value = Mathf.Lerp(0f, _maxAberration, severity);

        if (_audioSource != null)
        {
            if (!_audioSource.isPlaying)
                _audioSource.Play();

            _audioSource.volume = Mathf.Lerp(0f, 1f, severity);
            _audioSource.pitch = Mathf.Lerp(_basePitch, _maxPitch, severity);
        }
    }

    private void ApplyStaminaFx(float currentStaminaPct)
    {
        float severity = 1.0f - (currentStaminaPct / Mathf.Max(0.001f, _staminaThreshold));
        severity = Mathf.Clamp01(severity);

        _vignette.color.value = _staminaVignetteColor;
        _vignette.intensity.value = Mathf.Lerp(0f, _maxStaminaVignetteIntensity, severity);
        _aberration.intensity.value = 0f;

        FadeOutAudio();
    }

    private void RecoverFx()
    {
        _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value, 0f, Time.deltaTime * 5f);
        _aberration.intensity.value = Mathf.Lerp(_aberration.intensity.value, 0f, Time.deltaTime * 5f);

        FadeOutAudio();
    }

    private void FadeOutAudio()
    {
        if (_audioSource == null || !_audioSource.isPlaying)
            return;

        _audioSource.volume = Mathf.Lerp(_audioSource.volume, 0f, Time.deltaTime * 5f);

        if (_audioSource.volume < 0.01f)
        {
            _audioSource.Stop();
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