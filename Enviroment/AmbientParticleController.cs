using UnityEngine;

[DisallowMultipleComponent]
public class AmbientParticleController : MonoBehaviour
{
    [Header("Follow")]
    [Tooltip("Pokud je zapnuto, objekt sleduje Camera.main. Vhodné pro jemné částice kolem hráče.")]
    [SerializeField] private bool _followMainCamera = false;

    [SerializeField] private Transform _followTarget;
    [SerializeField] private Vector3 _localOffset = Vector3.zero;

    [Tooltip("U ambient částic doporučuji true. Objekt sleduje pozici, ale neotáčí se s kamerou.")]
    [SerializeField] private bool _followPositionOnly = true;

    [Header("Intensity")]
    [Tooltip("Základní intenzita částic.")]
    [Min(0f)]
    [SerializeField] private float _baseIntensity = 1f;

    [Tooltip("Externí intenzita nastavená AtmosphereManagerem. Typicky LocationInfluence * AmbientParticleIntensity.")]
    [Min(0f)]
    [SerializeField] private float _externalIntensity = 1f;

    [SerializeField] private AnimationCurve _intensityByTime = new AnimationCurve(
        new Keyframe(0.00f, 1.10f), // noc
        new Keyframe(0.23f, 1.20f), // před svítáním
        new Keyframe(0.33f, 0.85f), // ráno
        new Keyframe(0.50f, 0.45f), // den
        new Keyframe(0.72f, 1.00f), // západ
        new Keyframe(0.82f, 1.25f), // noc
        new Keyframe(1.00f, 1.10f)
    );

    [Header("Color")]
    [SerializeField] private Gradient _colorByTime = DefaultColorGradient();

    [Range(0f, 1f)]
    [SerializeField] private float _fogColorInfluence = 0.25f;

    [Range(0f, 1f)]
    [SerializeField] private float _locationColorInfluence = 0.45f;

    [Range(0f, 2f)]
    [SerializeField] private float _alphaMultiplier = 1f;

    [Header("Size & Speed")]
    [SerializeField] private AnimationCurve _sizeByTime = new AnimationCurve(
        new Keyframe(0.00f, 0.90f),
        new Keyframe(0.50f, 0.70f),
        new Keyframe(0.75f, 1.00f),
        new Keyframe(1.00f, 0.90f)
    );

    [SerializeField] private AnimationCurve _speedByTime = new AnimationCurve(
        new Keyframe(0.00f, 0.65f),
        new Keyframe(0.50f, 0.45f),
        new Keyframe(0.75f, 0.80f),
        new Keyframe(1.00f, 0.65f)
    );

    [Header("Wind")]
    [SerializeField] private bool _useGlobalWind = true;

    [Tooltip("Jak moc vítr tlačí částice. Většinou stačí velmi malá hodnota.")]
    [SerializeField] private float _windVelocityMultiplier = 0.35f;

    [Tooltip("Vertikální lehké kolísání. Hodí se pro magické motes.")]
    [SerializeField] private float _verticalDrift = 0.04f;

    [Header("Runtime")]
    [SerializeField] private bool _playOnEnable = true;

    private ParticleSystem[] _particleSystems;
    private float[] _baseEmissionRates;
    private float[] _baseStartSizes;
    private float[] _baseStartSpeeds;

    private bool _cached;

    private void OnEnable()
    {
        CacheParticleSystems();

        if (_playOnEnable)
            PlayAll();

        Apply();
    }

    private void Update()
    {
        FollowTarget();
        Apply();
    }

    public void SetExternalIntensity(float intensity)
    {
        _externalIntensity = Mathf.Max(0f, intensity);
        Apply();
    }

    public void SetFollowTarget(Transform target, Vector3 localOffset, bool positionOnly = true)
    {
        _followTarget = target;
        _localOffset = localOffset;
        _followPositionOnly = positionOnly;
    }

    public void PlayAll()
    {
        CacheParticleSystems();

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            if (_particleSystems[i] != null && !_particleSystems[i].isPlaying)
                _particleSystems[i].Play(true);
        }
    }

    public void StopAll(bool clear = false)
    {
        CacheParticleSystems();

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            if (_particleSystems[i] != null)
                _particleSystems[i].Stop(true, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void CacheParticleSystems()
    {
        if (_cached && _particleSystems != null)
            return;

        _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
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

    private void FollowTarget()
    {
        if (!_followMainCamera && _followTarget == null)
            return;

        Transform target = _followTarget;

        if (target == null && Camera.main != null)
            target = Camera.main.transform;

        if (target == null)
            return;

        transform.position = target.TransformPoint(_localOffset);

        if (!_followPositionOnly)
            transform.rotation = target.rotation;
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
        float finalIntensity = _baseIntensity * _externalIntensity * timeIntensity;

        float sizeMultiplier = Mathf.Max(0.01f, _sizeByTime.Evaluate(timePercent));
        float speedMultiplier = Mathf.Max(0.01f, _speedByTime.Evaluate(timePercent));

        Color finalColor = _colorByTime.Evaluate(timePercent);

        if (DayNightCycle.Instance != null)
        {
            finalColor = Color.Lerp(
                finalColor,
                DayNightCycle.Instance.CurrentFogColor,
                _fogColorInfluence
            );
        }

        if (AtmosphereManager.Instance != null)
        {
            LocationProfile profile = AtmosphereManager.Instance.CurrentProfile;
            float influence = AtmosphereManager.Instance.LocationInfluence;

            if (profile != null && influence > 0.001f)
            {
                finalColor = Color.Lerp(
                    finalColor,
                    profile.FogColor,
                    _locationColorInfluence * influence
                );
            }
        }

        finalColor.a = Mathf.Clamp01(finalColor.a * _alphaMultiplier);

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

            emission.rateOverTimeMultiplier = _baseEmissionRates[i] * finalIntensity;

            main.startColor = finalColor;
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

    private static Gradient DefaultColorGradient()
    {
        Gradient gradient = new Gradient();

        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Hex("#7E91D8"), 0.00f), // noc
                new GradientColorKey(Hex("#8FB7C2"), 0.25f), // svítání
                new GradientColorKey(Hex("#E9D6A2"), 0.50f), // denní prach/pyl
                new GradientColorKey(Hex("#F6A66F"), 0.72f), // západ
                new GradientColorKey(Hex("#9B7CDA"), 1.00f)  // noc
            },
            new[]
            {
                new GradientAlphaKey(0.45f, 0.00f),
                new GradientAlphaKey(0.35f, 0.50f),
                new GradientAlphaKey(0.55f, 0.75f),
                new GradientAlphaKey(0.45f, 1.00f)
            }
        );

        return gradient;
    }

    private static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out Color color))
            return color;

        return Color.white;
    }
}