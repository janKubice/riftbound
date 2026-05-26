using System.Collections.Generic;
using UnityEngine;


[ExecuteAlways]
[DisallowMultipleComponent]
public class MoodParticleObject : MonoBehaviour
{
    public enum ParticleLoopMode
    {
        KeepOriginal,
        ForceLoop,
        ForceNonLoop
    }

    [Header("Effect Objects")]
    [Tooltip("Může obsahovat buď objekty ze scény, child objekty, nebo prefab assety z Project okna.")]
    [SerializeField] private GameObject[] _effectObjects;

    [Header("Prefab Handling")]
    [Tooltip("Pokud je v Effect Objects prefab asset, script ho v Play Mode automaticky instancuje.")]
    [SerializeField] private bool _instantiatePrefabEffects = true;

    [Tooltip("Instancované prefab efekty budou child objekty tohoto MoodParticleObject.")]
    [SerializeField] private bool _parentInstantiatedEffectsToThis = true;

    [Tooltip("Volitelný parent pro instancované efekty. Pokud je null, použije se transform tohoto objektu.")]
    [SerializeField] private Transform _effectSpawnParent;

    [Tooltip("Pokud je zapnuto, prefab se vytvoří jednou a potom se znovu používá.")]
    [SerializeField] private bool _reuseInstantiatedPrefabEffects = true;

    [Tooltip("Nedoporučuji zapínat běžně. V edit módu by script mohl vytvářet objekty ve scéně.")]
    [SerializeField] private bool _instantiatePrefabsInEditMode = false;

    [Header("Base Intensity")]
    [Min(0f)]
    [SerializeField] private float _baseIntensity = 1.0f;

    [Min(0f)]
    [SerializeField] private float _externalIntensity = 1.0f;

    [Tooltip("Pokud je false, částice se úplně vypnou.")]
    [SerializeField] private bool _effectEnabled = true;

    [Header("Activation")]
    [Tooltip("0 = jen ambient idle, 1 = plně aktivní magický objekt.")]
    [Range(0f, 1f)]
    [SerializeField] private float _initialActivation = 1.0f;

    [Header("Loop")]
    [Tooltip("Keep Original = nechá loop nastavení z Particle Systemu. Force Loop = všechny systémy budou loop. Force Non Loop = všechny systémy budou one-shot.")]
    [SerializeField] private ParticleLoopMode _loopMode = ParticleLoopMode.KeepOriginal;

    [SerializeField] private float _activationFadeSpeed = 2.0f;

    [Header("Time Of Day")]
    [SerializeField]
    private AnimationCurve _intensityByTime = new AnimationCurve(
        new Keyframe(0.00f, 1.45f),
        new Keyframe(0.22f, 1.15f),
        new Keyframe(0.33f, 0.75f),
        new Keyframe(0.50f, 0.35f),
        new Keyframe(0.72f, 1.10f),
        new Keyframe(0.82f, 1.55f),
        new Keyframe(1.00f, 1.45f)
    );

    [SerializeField] private Gradient _colorByTime = DefaultMagicColorGradient();

    [Header("Location Influence")]
    [SerializeField] private bool _useAtmosphereLocationInfluence = true;

    [Range(0f, 1f)]
    [SerializeField] private float _locationColorInfluence = 0.45f;

    [Range(0f, 1f)]
    [SerializeField] private float _locationIntensityInfluence = 1.0f;

    [Header("Color And Shape")]
    [Range(0f, 3f)]
    [SerializeField] private float _alphaMultiplier = 1.0f;

    [SerializeField]
    private AnimationCurve _sizeByActivation = new AnimationCurve(
        new Keyframe(0.00f, 0.65f),
        new Keyframe(1.00f, 1.00f)
    );

    [SerializeField]
    private AnimationCurve _speedByActivation = new AnimationCurve(
        new Keyframe(0.00f, 0.60f),
        new Keyframe(1.00f, 1.00f)
    );

    [Header("Wind / Drift")]
    [SerializeField] private bool _useGlobalWind = true;

    [Tooltip("Velmi malé hodnoty jsou nejlepší. Lokální magický objekt nemá odlétat pryč.")]
    [SerializeField] private float _windVelocityMultiplier = 0.08f;

    [SerializeField] private float _verticalDrift = 0.05f;

    [Header("Pulse")]
    [SerializeField] private bool _usePulse = true;
    [SerializeField] private float _pulseSpeed = 0.75f;

    [Range(0f, 1f)]
    [SerializeField] private float _pulseAmount = 0.18f;

    [SerializeField] private bool _randomizePulseOffset = true;
    [SerializeField] private float _manualPulseOffset = 0f;

    [Header("Interaction Burst")]
    [SerializeField] private bool _allowBurst = true;

    [Min(0)]
    [SerializeField] private int _burstCount = 18;

    [SerializeField] private float _burstActivationBoost = 1.0f;
    [SerializeField] private float _burstBoostDuration = 1.2f;

    [Header("Runtime")]
    [SerializeField] private bool _playOnEnable = true;

    [Header("Runtime Color Override")]
    [SerializeField] private bool _allowColorOverride = true;

    private bool _hasColorOverride;
    private Color _colorOverride = Color.white;

    [Header("Debug")]
    [SerializeField] private bool _logSkippedPrefabInEditMode = false;

    private readonly Dictionary<GameObject, GameObject> _runtimePrefabInstances = new();

    private ParticleSystem[] _particleSystems;
    private float[] _baseEmissionRates;
    private float[] _baseStartSizes;
    private float[] _baseStartSpeeds;

    private bool _cached;

    private float _currentActivation;
    private float _targetActivation;
    private float _temporaryBoost;
    private float _temporaryBoostTimer;
    private float _pulseOffset;

    private void Reset()
    {
        ParticleSystem[] foundSystems = GetComponentsInChildren<ParticleSystem>(true);

        _effectObjects = new GameObject[foundSystems.Length];

        for (int i = 0; i < foundSystems.Length; i++)
        {
            _effectObjects[i] = foundSystems[i].gameObject;
        }
    }

    private void OnEnable()
    {
        InvalidateCache();
        CacheParticleSystems();

        _currentActivation = _initialActivation;
        _targetActivation = _initialActivation;

        _pulseOffset = _randomizePulseOffset
            ? Random.Range(0f, 100f)
            : _manualPulseOffset;

        if (_playOnEnable)
            PlayAll();

        Apply();
    }

    private void OnValidate()
    {
        InvalidateCache();

        // Nikdy neinstancovat prefab efekty, pokud jsme na prefab assetu v Project okně.
        if (IsPrefabAssetContext())
            return;

        // V edit módu cache/apply jen pokud to výslovně povolíte.
        if (Application.isPlaying || _instantiatePrefabsInEditMode)
        {
            CacheParticleSystems();
            Apply();
        }
    }

    private bool IsPrefabAssetContext()
    {
#if UNITY_EDITOR
        return UnityEditor.EditorUtility.IsPersistent(this) ||
            UnityEditor.EditorUtility.IsPersistent(gameObject);
#else
            return false;
#endif
    }

    private void OnDestroy()
    {
        foreach (KeyValuePair<GameObject, GameObject> pair in _runtimePrefabInstances)
        {
            if (pair.Value != null)
                SafeDestroy(pair.Value);
        }

        _runtimePrefabInstances.Clear();
    }

    private void Update()
    {
        float deltaTime = Application.isPlaying ? Time.deltaTime : 1f;

        _currentActivation = Mathf.MoveTowards(
            _currentActivation,
            _targetActivation,
            deltaTime * Mathf.Max(0.01f, _activationFadeSpeed)
        );

        if (_temporaryBoostTimer > 0f)
        {
            _temporaryBoostTimer -= deltaTime;

            if (_temporaryBoostTimer <= 0f)
                _temporaryBoost = 0f;
        }

        Apply();
    }

    public void SetExternalIntensity(float intensity)
    {
        _externalIntensity = Mathf.Max(0f, intensity);
        Apply();
    }

    public void SetActivated(bool active)
    {
        _targetActivation = active ? 1f : 0f;
    }

    public void SetActivation(float activation)
    {
        _targetActivation = Mathf.Clamp01(activation);
    }

    public void EnableEffect()
    {
        _effectEnabled = true;
        PlayAll();
        Apply();
    }

    public void DisableEffect(bool clearParticles = false)
    {
        _effectEnabled = false;
        StopAll(clearParticles);
        Apply();
    }

    public void TriggerBurst()
    {
        if (!_allowBurst)
            return;

        CacheParticleSystems();

        if (_particleSystems == null || _particleSystems.Length == 0)
            return;

        int emitCount = Mathf.Max(0, _burstCount);

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem ps = _particleSystems[i];

            if (ps == null)
                continue;

            if (!ps.gameObject.activeInHierarchy)
                ps.gameObject.SetActive(true);

            if (!ps.isPlaying)
                ps.Play(true);

            ps.Emit(emitCount);
        }

        _temporaryBoost = Mathf.Max(_temporaryBoost, _burstActivationBoost);
        _temporaryBoostTimer = Mathf.Max(_temporaryBoostTimer, _burstBoostDuration);
    }

    public void PlayAll()
    {
        CacheParticleSystems();

        if (_particleSystems == null)
            return;

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem ps = _particleSystems[i];

            if (ps == null)
                continue;

            if (!ps.gameObject.activeInHierarchy)
                ps.gameObject.SetActive(true);

            if (!ps.isPlaying)
                ps.Play(true);
        }
    }

    public void StopAll(bool clear)
    {
        CacheParticleSystems();

        if (_particleSystems == null)
            return;

        ParticleSystemStopBehavior behavior = clear
            ? ParticleSystemStopBehavior.StopEmittingAndClear
            : ParticleSystemStopBehavior.StopEmitting;

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem ps = _particleSystems[i];

            if (ps != null)
                ps.Stop(true, behavior);
        }
    }

    private void CacheParticleSystems()
    {
        if (_cached && _particleSystems != null)
            return;

        List<GameObject> resolvedObjects = ResolveEffectObjects();
        List<ParticleSystem> systems = new List<ParticleSystem>();

        if (resolvedObjects.Count > 0)
        {
            for (int i = 0; i < resolvedObjects.Count; i++)
            {
                GameObject effectObject = resolvedObjects[i];

                if (effectObject == null)
                    continue;

                ParticleSystem[] childSystems = effectObject.GetComponentsInChildren<ParticleSystem>(true);
                systems.AddRange(childSystems);
            }
        }
        else
        {
            ParticleSystem[] childSystems = GetComponentsInChildren<ParticleSystem>(true);
            systems.AddRange(childSystems);
        }

        _particleSystems = systems.ToArray();

        _baseEmissionRates = new float[_particleSystems.Length];
        _baseStartSizes = new float[_particleSystems.Length];
        _baseStartSpeeds = new float[_particleSystems.Length];

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem ps = _particleSystems[i];

            if (ps == null)
                continue;

            ParticleSystem.EmissionModule emission = ps.emission;
            ParticleSystem.MainModule main = ps.main;

            _baseEmissionRates[i] = emission.rateOverTimeMultiplier;
            _baseStartSizes[i] = main.startSizeMultiplier;
            _baseStartSpeeds[i] = main.startSpeedMultiplier;
        }

        _cached = true;
    }

    private List<GameObject> ResolveEffectObjects()
    {
        List<GameObject> resolvedObjects = new List<GameObject>();

        if (_effectObjects == null)
            return resolvedObjects;

        for (int i = 0; i < _effectObjects.Length; i++)
        {
            GameObject effectObject = _effectObjects[i];

            if (effectObject == null)
                continue;

            if (IsSceneInstance(effectObject))
            {
                resolvedObjects.Add(effectObject);
                continue;
            }

            bool canInstantiateNow = Application.isPlaying || _instantiatePrefabsInEditMode;

            if (!_instantiatePrefabEffects || !canInstantiateNow)
            {
                if (_logSkippedPrefabInEditMode && !Application.isPlaying)
                {
                    Debug.LogWarning(
                        $"{nameof(MoodParticleObject)} '{name}' má v Effect Objects prefab asset '{effectObject.name}', " +
                        "ale prefab efekty se v Edit Mode neinstancují. V Play Mode se vytvoří automaticky.",
                        this
                    );
                }

                continue;
            }

            GameObject runtimeInstance = GetOrCreateRuntimePrefabInstance(effectObject);

            if (runtimeInstance != null)
                resolvedObjects.Add(runtimeInstance);
        }

        return resolvedObjects;
    }

    private GameObject GetOrCreateRuntimePrefabInstance(GameObject prefab)
    {
        if (_reuseInstantiatedPrefabEffects &&
            _runtimePrefabInstances.TryGetValue(prefab, out GameObject existingInstance) &&
            existingInstance != null)
        {
            return existingInstance;
        }

        Transform parent = null;

        if (_parentInstantiatedEffectsToThis)
            parent = _effectSpawnParent != null ? _effectSpawnParent : transform;

        GameObject instance = Instantiate(prefab, parent);
        instance.name = $"{prefab.name}_RuntimeMoodParticles";

        if (parent != null)
        {
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
        else
        {
            instance.transform.SetPositionAndRotation(transform.position, transform.rotation);
        }

        instance.SetActive(true);

        if (_reuseInstantiatedPrefabEffects)
            _runtimePrefabInstances[prefab] = instance;

        return instance;
    }

    private static bool IsSceneInstance(GameObject obj)
    {
        return obj.scene.IsValid() && obj.scene.isLoaded;
    }

    private void InvalidateCache()
    {
        _cached = false;
        _particleSystems = null;
        _baseEmissionRates = null;
        _baseStartSizes = null;
        _baseStartSpeeds = null;
    }

    private void Apply()
    {
        CacheParticleSystems();

        if (_particleSystems == null || _particleSystems.Length == 0)
            return;

        float timePercent = DayNightCycle.Instance != null
            ? DayNightCycle.Instance.TimePercent
            : 0.5f;

        float timeIntensity = Mathf.Max(0f, _intensityByTime.Evaluate(timePercent));
        float activation = Mathf.Clamp01(_currentActivation + _temporaryBoost);

        float finalIntensity = _baseIntensity * _externalIntensity * timeIntensity * activation;

        if (!_effectEnabled)
            finalIntensity = 0f;

        Color finalColor = _hasColorOverride
            ? _colorOverride
            : _colorByTime.Evaluate(timePercent);

        if (_useAtmosphereLocationInfluence && AtmosphereManager.Instance != null)
        {
            LocationProfile profile = AtmosphereManager.Instance.CurrentProfile;
            float influence = AtmosphereManager.Instance.LocationInfluence;

            if (profile != null && influence > 0.001f)
            {
                float locationStrength = influence * _locationIntensityInfluence;

                finalIntensity *= Mathf.Lerp(
                    1f,
                    Mathf.Max(0f, profile.EmissiveIntensityMultiplier),
                    locationStrength
                );

                finalIntensity += profile.EmissiveIntensityAdd * locationStrength;

                finalColor = Color.Lerp(
                    finalColor,
                    profile.EmissiveTint,
                    profile.EmissiveTintStrength * _locationColorInfluence * influence
                );
            }
        }

        if (_usePulse)
        {
            float pulse = Mathf.Sin((GetTime() + _pulseOffset) * _pulseSpeed);
            pulse = pulse * 0.5f + 0.5f;

            finalIntensity *= 1f + pulse * _pulseAmount;
        }

        finalColor.a = Mathf.Clamp01(finalColor.a * _alphaMultiplier);

        float sizeMultiplier = Mathf.Max(0.01f, _sizeByActivation.Evaluate(activation));
        float speedMultiplier = Mathf.Max(0.01f, _speedByActivation.Evaluate(activation));

        Vector3 windVelocity = Vector3.zero;

        if (_useGlobalWind && GlobalWindManager.Instance != null)
        {
            windVelocity =
                GlobalWindManager.Instance.CurrentWindDirection *
                GlobalWindManager.Instance.CurrentWindStrength *
                _windVelocityMultiplier;
        }

        windVelocity.y += _verticalDrift;

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem ps = _particleSystems[i];

            if (ps == null)
                continue;

            ParticleSystem.EmissionModule emission = ps.emission;
            ParticleSystem.MainModule main = ps.main;
            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;

            if (_loopMode != ParticleLoopMode.KeepOriginal)
            {
                main.loop = _loopMode == ParticleLoopMode.ForceLoop;
            }

            emission.rateOverTimeMultiplier = _baseEmissionRates[i] * finalIntensity;

            main.startColor = new ParticleSystem.MinMaxGradient(finalColor);
            main.startSizeMultiplier = _baseStartSizes[i] * sizeMultiplier;
            main.startSpeedMultiplier = _baseStartSpeeds[i] * speedMultiplier;

            if (_useGlobalWind)
            {
                velocity.enabled = true;
                velocity.space = ParticleSystemSimulationSpace.World;
                velocity.x = new ParticleSystem.MinMaxCurve(windVelocity.x);
                velocity.y = new ParticleSystem.MinMaxCurve(windVelocity.y);
                velocity.z = new ParticleSystem.MinMaxCurve(windVelocity.z);
            }
        }
    }

    private float GetTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif

        return Time.time;
    }

    private static void SafeDestroy(GameObject obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private static Gradient DefaultMagicColorGradient()
    {
        Gradient gradient = new Gradient();

        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Hex("#8FA8FF"), 0.00f),
                new GradientColorKey(Hex("#8FB7C2"), 0.25f),
                new GradientColorKey(Hex("#BDEBFF"), 0.50f),
                new GradientColorKey(Hex("#FFB36A"), 0.72f),
                new GradientColorKey(Hex("#A77BFF"), 1.00f)
            },
            new[]
            {
                new GradientAlphaKey(0.55f, 0.00f),
                new GradientAlphaKey(0.35f, 0.50f),
                new GradientAlphaKey(0.65f, 0.75f),
                new GradientAlphaKey(0.55f, 1.00f)
            }
        );

        return gradient;
    }

    public void SetColorOverride(Color color)
    {
        if (!_allowColorOverride)
            return;

        _colorOverride = color;
        _hasColorOverride = true;
        Apply();
    }

    public void ClearColorOverride()
    {
        _hasColorOverride = false;
        Apply();
    }

    private static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
            return color;

        return Color.white;
    }
}