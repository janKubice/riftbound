using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
public class AtmosphereManager : MonoBehaviour
{
    public static AtmosphereManager Instance { get; private set; }

    [Header("Global Components")]
    [SerializeField] private Volume _atmosphereVolume;
    private AmbientParticleController _currentAmbientParticleController;

    [Tooltip("Atmosférický volume by měl mít nižší prioritu než PlayerScreenFX. Doporučeno: 10.")]
    [SerializeField] private float _volumePriority = 10f;

    [Tooltip("Vytvoří runtime kopii VolumeProfile, aby script neměnil asset v projektu.")]
    [SerializeField] private bool _createRuntimeProfileInstance = true;

    [Header("Transition")]
    [SerializeField] private float _transitionSpeed = 1.0f;

    [Header("Control")]
    [SerializeField] private bool _controlFogAndAmbient = true;
    [SerializeField] private bool _controlPostProcessing = true;

    [Header("Base Post Processing By Time Of Day")]
    [SerializeField]
    private AnimationCurve _basePostExposure = new AnimationCurve(
        new Keyframe(0.00f, -0.35f),
        new Keyframe(0.25f, -0.10f),
        new Keyframe(0.50f, 0.00f),
        new Keyframe(0.75f, -0.05f),
        new Keyframe(1.00f, -0.35f)
    );

    [SerializeField]
    private AnimationCurve _baseContrast = new AnimationCurve(
        new Keyframe(0.00f, 18f),
        new Keyframe(0.25f, 12f),
        new Keyframe(0.50f, 8f),
        new Keyframe(0.75f, 22f),
        new Keyframe(1.00f, 18f)
    );

    [SerializeField]
    private AnimationCurve _baseSaturation = new AnimationCurve(
        new Keyframe(0.00f, -6f),
        new Keyframe(0.25f, 4f),
        new Keyframe(0.50f, 5f),
        new Keyframe(0.75f, 10f),
        new Keyframe(1.00f, -6f)
    );

    [SerializeField]
    private AnimationCurve _baseBloomIntensity = new AnimationCurve(
        new Keyframe(0.00f, 0.45f),
        new Keyframe(0.25f, 0.25f),
        new Keyframe(0.50f, 0.12f),
        new Keyframe(0.75f, 0.38f),
        new Keyframe(1.00f, 0.45f)
    );

    [SerializeField]
    private AnimationCurve _baseBloomThreshold = new AnimationCurve(
        new Keyframe(0.00f, 1.15f),
        new Keyframe(0.50f, 1.35f),
        new Keyframe(0.75f, 1.05f),
        new Keyframe(1.00f, 1.15f)
    );

    [SerializeField]
    private AnimationCurve _baseVignette = new AnimationCurve(
        new Keyframe(0.00f, 0.24f),
        new Keyframe(0.25f, 0.16f),
        new Keyframe(0.50f, 0.10f),
        new Keyframe(0.75f, 0.18f),
        new Keyframe(1.00f, 0.24f)
    );

    [SerializeField]
    private AnimationCurve _baseTemperature = new AnimationCurve(
        new Keyframe(0.00f, -15f),
        new Keyframe(0.25f, 5f),
        new Keyframe(0.50f, 0f),
        new Keyframe(0.75f, 12f),
        new Keyframe(1.00f, -15f)
    );

    [SerializeField]
    private AnimationCurve _baseTint = new AnimationCurve(
        new Keyframe(0.00f, 8f),
        new Keyframe(0.25f, -4f),
        new Keyframe(0.50f, 0f),
        new Keyframe(0.75f, 10f),
        new Keyframe(1.00f, 8f)
    );

    [Header("Debug")]
    [SerializeField] private bool _debugLogTransitions = false;

    private float _locationInfluence = 0f;
    private float _targetInfluence = 0f;
    private bool _isExitingLocation = false;

    private LocationProfile _currentProfile;

    private VolumeProfile _runtimeProfile;
    private ColorAdjustments _colorAdjustments;
    private Bloom _bloom;
    private Vignette _vignette;
    private WhiteBalance _whiteBalance;

    private GameObject _currentParticleInstance;
    private ParticleSystem[] _currentParticleSystems;
    private float[] _baseParticleRateMultipliers;
    private DayNightCycle _dayNightCache;
    public LocationProfile CurrentProfile => _currentProfile;
    public float LocationInfluence => _locationInfluence;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Duplicate AtmosphereManager found. Destroying duplicate.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
        SetupVolume();
    }

    private void Start()
    {
        SetupVolume();
        // Kešování reference zabrání volání property getteru každý snímek
        _dayNightCache = DayNightCycle.Instance;
    }

    private void Update()
    {
        float deltaTime = Application.isPlaying ? Time.deltaTime : 0f;

        if (!Mathf.Approximately(_locationInfluence, _targetInfluence))
        {
            _locationInfluence = Mathf.MoveTowards(
                _locationInfluence,
                _targetInfluence,
                deltaTime * Mathf.Max(0.01f, _transitionSpeed)
            );
        }

        // Plynulé vizuální systémy musí běžet každý snímek
        ApplyAtmosphere();
        UpdateParticleIntensity();

        if (_isExitingLocation && _locationInfluence <= 0.001f)
        {
            CompleteLocationExit();
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        if (_createRuntimeProfileInstance && _runtimeProfile != null)
        {
            Destroy(_runtimeProfile);
        }
    }



    private void SetupVolume()
    {
        if (_atmosphereVolume == null)
            _atmosphereVolume = GetComponent<Volume>();

        if (_atmosphereVolume == null)
            _atmosphereVolume = gameObject.AddComponent<Volume>();

        _atmosphereVolume.isGlobal = true;
        _atmosphereVolume.priority = _volumePriority;

        if (_createRuntimeProfileInstance)
        {
            if (_runtimeProfile == null)
            {
                VolumeProfile sourceProfile = _atmosphereVolume.profile;

                if (sourceProfile != null)
                {
                    _runtimeProfile = Instantiate(sourceProfile);
                    _runtimeProfile.name = $"{sourceProfile.name}_RuntimeAtmosphere";
                }
                else
                {
                    _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
                    _runtimeProfile.name = "RuntimeAtmosphereProfile";
                }

                _atmosphereVolume.profile = _runtimeProfile;
            }
        }
        else
        {
            if (_atmosphereVolume.profile == null)
            {
                _atmosphereVolume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            }

            _runtimeProfile = _atmosphereVolume.profile;
        }

        CacheVolumeOverrides();
    }

    private void CacheVolumeOverrides()
    {
        if (_runtimeProfile == null)
            return;

        if (!_runtimeProfile.TryGet(out _colorAdjustments))
            _colorAdjustments = _runtimeProfile.Add<ColorAdjustments>(true);

        if (!_runtimeProfile.TryGet(out _bloom))
            _bloom = _runtimeProfile.Add<Bloom>(true);

        if (!_runtimeProfile.TryGet(out _vignette))
            _vignette = _runtimeProfile.Add<Vignette>(true);

        if (!_runtimeProfile.TryGet(out _whiteBalance))
            _whiteBalance = _runtimeProfile.Add<WhiteBalance>(true);

        _colorAdjustments.active = true;
        _colorAdjustments.postExposure.overrideState = true;
        _colorAdjustments.contrast.overrideState = true;
        _colorAdjustments.saturation.overrideState = true;

        _bloom.active = true;
        _bloom.intensity.overrideState = true;
        _bloom.threshold.overrideState = true;

        _vignette.active = true;
        _vignette.intensity.overrideState = true;
        _vignette.smoothness.overrideState = true;
        _vignette.color.overrideState = true;

        _whiteBalance.active = true;
        _whiteBalance.temperature.overrideState = true;
        _whiteBalance.tint.overrideState = true;
    }

    private void ApplyAtmosphere()
    {
        // Použití nakešované reference
        float timePercent = _dayNightCache != null ? _dayNightCache.TimePercent : 0.5f;

        Color baseFog = _dayNightCache != null ? _dayNightCache.CurrentFogColor : RenderSettings.fogColor;
        float baseFogDensity = _dayNightCache != null ? _dayNightCache.CurrentFogDensity : RenderSettings.fogDensity;
        Color baseAmbient = _dayNightCache != null ? _dayNightCache.CurrentAmbientColor : RenderSettings.ambientLight;

        if (_controlFogAndAmbient)
        {
            ApplyFogAndAmbient(baseFog, baseFogDensity, baseAmbient);
        }

        if (_controlPostProcessing)
        {
            ApplyPostProcessing(timePercent);
        }
    }

    private void ApplyFogAndAmbient(Color baseFog, float baseFogDensity, Color baseAmbient)
    {
        Color finalFog = baseFog;
        float finalFogDensity = baseFogDensity;
        Color finalAmbient = baseAmbient;

        if (_currentProfile != null && _locationInfluence > 0.001f)
        {
            Color locationFog = TintColor(
                baseFog,
                _currentProfile.FogColor,
                _currentProfile.FogTintStrength,
                _currentProfile.FogTintMultiplier
            );

            float multipliedBaseDensity = baseFogDensity * Mathf.Max(0f, _currentProfile.FogDensityMultiplier);
            float locationDensityTarget = Mathf.Lerp(
                multipliedBaseDensity,
                Mathf.Max(0f, _currentProfile.FogDensity),
                _currentProfile.FogDensityOverride
            );

            Color locationAmbient = TintColor(
                baseAmbient,
                _currentProfile.AmbientColor,
                _currentProfile.AmbientTintStrength,
                _currentProfile.AmbientTintMultiplier
            );

            finalFog = Color.Lerp(baseFog, locationFog, _locationInfluence);
            finalFogDensity = Mathf.Lerp(baseFogDensity, locationDensityTarget, _locationInfluence);
            finalAmbient = Color.Lerp(baseAmbient, locationAmbient, _locationInfluence);
        }

        RenderSettings.fog = true;
        RenderSettings.fogColor = finalFog;
        RenderSettings.fogDensity = finalFogDensity;

        ApplyAmbient(finalAmbient);
    }

    private void ApplyPostProcessing(float timePercent)
    {
        if (_runtimeProfile == null)
            SetupVolume();

        if (_colorAdjustments == null || _bloom == null || _vignette == null || _whiteBalance == null)
            CacheVolumeOverrides();

        float exposure = _basePostExposure.Evaluate(timePercent);
        float contrast = _baseContrast.Evaluate(timePercent);
        float saturation = _baseSaturation.Evaluate(timePercent);

        float bloomIntensity = _baseBloomIntensity.Evaluate(timePercent);
        float bloomThreshold = _baseBloomThreshold.Evaluate(timePercent);

        float vignetteIntensity = _baseVignette.Evaluate(timePercent);

        float temperature = _baseTemperature.Evaluate(timePercent);
        float tint = _baseTint.Evaluate(timePercent);

        if (_currentProfile != null && _locationInfluence > 0.001f)
        {
            exposure += _currentProfile.PostExposureOffset * _locationInfluence;
            contrast += _currentProfile.ContrastOffset * _locationInfluence;
            saturation += _currentProfile.SaturationOffset * _locationInfluence;

            bloomIntensity =
                bloomIntensity * Mathf.Lerp(1f, Mathf.Max(0f, _currentProfile.BloomMultiplier), _locationInfluence)
                + (_currentProfile.BloomAdd * _locationInfluence);

            bloomThreshold += _currentProfile.BloomThresholdOffset * _locationInfluence;
            vignetteIntensity += _currentProfile.VignetteAdd * _locationInfluence;

            temperature += _currentProfile.TemperatureOffset * _locationInfluence;
            tint += _currentProfile.TintOffset * _locationInfluence;
        }

        _colorAdjustments.postExposure.value = exposure;
        _colorAdjustments.contrast.value = Mathf.Clamp(contrast, -100f, 100f);
        _colorAdjustments.saturation.value = Mathf.Clamp(saturation, -100f, 100f);

        _bloom.intensity.value = Mathf.Max(0f, bloomIntensity);
        _bloom.threshold.value = Mathf.Max(0.01f, bloomThreshold);

        _vignette.intensity.value = Mathf.Clamp01(vignetteIntensity);
        _vignette.smoothness.value = 0.45f;
        _vignette.color.value = Color.black;

        _whiteBalance.temperature.value = Mathf.Clamp(temperature, -100f, 100f);
        _whiteBalance.tint.value = Mathf.Clamp(tint, -100f, 100f);
    }

    public void EnterLocation(LocationProfile profile)
    {
        if (profile == null)
            return;

        bool profileChanged = _currentProfile != profile;

        _currentProfile = profile;
        _targetInfluence = 1.0f;
        _isExitingLocation = false;

        if (profileChanged)
            HandleParticleSwap(profile);

        if (_debugLogTransitions)
            Debug.Log($"Entering location atmosphere: {profile.LocationName}", this);
    }

    public void ExitLocation()
    {
        _targetInfluence = 0.0f;
        _isExitingLocation = true;

        if (_debugLogTransitions)
        {
            string name = _currentProfile != null ? _currentProfile.LocationName : "None";
            Debug.Log($"Exiting location atmosphere: {name}", this);
        }
    }

    private void CompleteLocationExit()
    {
        _isExitingLocation = false;
        _currentProfile = null;

        if (_currentParticleInstance != null)
        {
            Destroy(_currentParticleInstance);
            _currentParticleInstance = null;
        }

        _currentAmbientParticleController = null;
        _currentParticleSystems = null;
        _baseParticleRateMultipliers = null;
    }

    private void HandleParticleSwap(LocationProfile profile)
    {
        if (_currentParticleInstance != null)
        {
            Destroy(_currentParticleInstance);
            _currentParticleInstance = null;
        }

        _currentParticleSystems = null;
        _baseParticleRateMultipliers = null;

        if (profile == null || profile.AmbientParticlesPrefab == null)
            return;

        Transform parent = transform;

        if (profile.ParentParticlesToCamera && Camera.main != null)
            parent = Camera.main.transform;

        _currentParticleInstance = Instantiate(profile.AmbientParticlesPrefab, parent);
        _currentParticleInstance.transform.localPosition = profile.AmbientParticlesLocalOffset;
        _currentParticleInstance.transform.localRotation = Quaternion.identity;

        _currentAmbientParticleController =
            _currentParticleInstance.GetComponentInChildren<AmbientParticleController>(true);

        if (_currentAmbientParticleController != null)
        {
            Transform followTarget = null;

            if (profile.ParentParticlesToCamera && Camera.main != null)
                followTarget = Camera.main.transform;

            if (followTarget != null)
            {
                _currentAmbientParticleController.SetFollowTarget(
                    followTarget,
                    profile.AmbientParticlesLocalOffset,
                    true
                );
            }
        }

        CacheParticleSystems();

        SetParticleIntensity(0f);
    }

    private void CacheParticleSystems()
    {
        if (_currentParticleInstance == null)
            return;

        _currentParticleSystems = _currentParticleInstance.GetComponentsInChildren<ParticleSystem>(true);
        _baseParticleRateMultipliers = new float[_currentParticleSystems.Length];

        for (int i = 0; i < _currentParticleSystems.Length; i++)
        {
            ParticleSystem ps = _currentParticleSystems[i];
            ParticleSystem.EmissionModule emission = ps.emission;

            _baseParticleRateMultipliers[i] = emission.rateOverTimeMultiplier;

            if (!ps.isPlaying)
                ps.Play(true);
        }
    }

    private void UpdateParticleIntensity()
    {
        if (_currentProfile == null || _currentParticleSystems == null)
            return;

        float targetIntensity = _locationInfluence * Mathf.Max(0f, _currentProfile.AmbientParticleIntensity);
        SetParticleIntensity(targetIntensity);
    }

    private void SetParticleIntensity(float intensity)
    {
        if (_currentAmbientParticleController != null)
        {
            _currentAmbientParticleController.SetExternalIntensity(intensity);
            return;
        }

        if (_currentParticleSystems == null || _baseParticleRateMultipliers == null)
            return;

        for (int i = 0; i < _currentParticleSystems.Length; i++)
        {
            ParticleSystem ps = _currentParticleSystems[i];

            if (ps == null)
                continue;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTimeMultiplier = _baseParticleRateMultipliers[i] * intensity;
        }
    }

    private static Color TintColor(Color baseColor, Color tintColor, float strength, float multiplier)
    {
        strength = Mathf.Clamp01(strength);
        multiplier = Mathf.Max(0f, multiplier);

        Color tinted = new Color(
            baseColor.r * tintColor.r * multiplier,
            baseColor.g * tintColor.g * multiplier,
            baseColor.b * tintColor.b * multiplier,
            1f
        );

        return Color.Lerp(baseColor, tinted, strength);
    }

    private static void ApplyAmbient(Color ambient)
    {
        RenderSettings.ambientLight = ambient;

        // Pokud používáte Trilight ambient mode, tyhle tři hodnoty jsou důležitější než samotné ambientLight.
        RenderSettings.ambientSkyColor = ambient * 1.15f;
        RenderSettings.ambientEquatorColor = ambient;
        RenderSettings.ambientGroundColor = ambient * 0.55f;
    }
}