using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class MoodLightObject : MonoBehaviour
{
    [Header("Lights")]
    [SerializeField] private Light[] _lights;

    [Header("Base Light")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _baseColor = new Color(0.35f, 0.85f, 1.0f, 1f);

    [Min(0f)]
    [SerializeField] private float _baseIntensity = 1.0f;

    [Min(0f)]
    [SerializeField] private float _baseRange = 5.0f;

    [Header("Time Of Day")]
    [SerializeField]
    private AnimationCurve _intensityByTime = new AnimationCurve(
        new Keyframe(0.00f, 1.35f), // půlnoc
        new Keyframe(0.22f, 1.10f), // před svítáním
        new Keyframe(0.33f, 0.55f), // ráno
        new Keyframe(0.50f, 0.15f), // poledne
        new Keyframe(0.72f, 0.90f), // západ
        new Keyframe(0.82f, 1.40f), // noc
        new Keyframe(1.00f, 1.35f)
    );

    [SerializeField]
    private AnimationCurve _rangeByTime = new AnimationCurve(
        new Keyframe(0.00f, 1.10f),
        new Keyframe(0.50f, 0.65f),
        new Keyframe(0.75f, 1.00f),
        new Keyframe(1.00f, 1.10f)
    );

    [Header("Location Influence")]
    [SerializeField] private bool _useAtmosphereLocationInfluence = true;

    [Range(0f, 1f)]
    [SerializeField] private float _locationInfluenceStrength = 1.0f;

    [Header("Pulse")]
    [SerializeField] private bool _usePulse = true;

    [Min(0f)]
    [SerializeField] private float _pulseSpeed = 0.85f;

    [Range(0f, 1f)]
    [SerializeField] private float _pulseAmount = 0.12f;

    [SerializeField] private bool _randomizePulseOffset = true;
    [SerializeField] private float _manualPulseOffset = 0f;

    [Header("Flicker")]
    [SerializeField] private bool _useSubtleFlicker = true;

    [Range(0f, 1f)]
    [SerializeField] private float _flickerAmount = 0.05f;

    [Min(0.01f)]
    [SerializeField] private float _flickerSpeed = 1.75f;

    [Header("Light Setup")]
    [SerializeField] private bool _forceNoShadows = true;

    [Tooltip("Pro malé magické světlo většinou stačí Important nebo Auto. Pokud máte hodně světel, dejte Auto.")]
    [SerializeField] private LightRenderMode _renderMode = LightRenderMode.Auto;

    [Header("Runtime Override")]
    [SerializeField] private bool _allowRuntimeOverride = true;

    private bool _hasRuntimeColorOverride;
    private Color _runtimeColorOverride = Color.white;
    private float _externalIntensity = 1f;

    [Header("Debug")]
    [SerializeField] private bool _forceFullLight = false;
    [SerializeField] private bool _disableLight = false;

    private float _pulseOffset;

    private void Reset()
    {
        _lights = GetComponentsInChildren<Light>(true);

        if (_lights == null || _lights.Length == 0)
        {
            Light ownLight = GetComponent<Light>();

            if (ownLight != null)
                _lights = new[] { ownLight };
        }
    }

    private void OnEnable()
    {
        if (_lights == null || _lights.Length == 0)
            _lights = GetComponentsInChildren<Light>(true);

        if (_randomizePulseOffset)
            _pulseOffset = Random.Range(0f, 100f);
        else
            _pulseOffset = _manualPulseOffset;

        ConfigureLights();
        Apply();
    }

    private void OnValidate()
    {
        if (_lights == null || _lights.Length == 0)
            _lights = GetComponentsInChildren<Light>(true);

        ConfigureLights();
        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void ConfigureLights()
    {
        if (_lights == null)
            return;

        for (int i = 0; i < _lights.Length; i++)
        {
            Light light = _lights[i];

            if (light == null)
                continue;

            light.renderMode = _renderMode;

            if (_forceNoShadows)
                light.shadows = LightShadows.None;
        }
    }

    private void Apply()
    {
        if (_lights == null || _lights.Length == 0)
            return;

        float timePercent = DayNightCycle.Instance != null
            ? DayNightCycle.Instance.TimePercent
            : 0.5f;

        float finalIntensity =
            _baseIntensity *
            _externalIntensity *
            Mathf.Max(0f, _intensityByTime.Evaluate(timePercent));

        float finalRange = _baseRange * Mathf.Max(0.01f, _rangeByTime.Evaluate(timePercent));

        Color finalColor = _hasRuntimeColorOverride
            ? _runtimeColorOverride
            : _baseColor;

        if (_useAtmosphereLocationInfluence && AtmosphereManager.Instance != null)
        {
            LocationProfile profile = AtmosphereManager.Instance.CurrentProfile;
            float influence = AtmosphereManager.Instance.LocationInfluence;

            if (profile != null && influence > 0.001f)
            {
                float locationStrength = influence * _locationInfluenceStrength;

                finalIntensity *= Mathf.Lerp(
                    1f,
                    Mathf.Max(0f, profile.EmissiveIntensityMultiplier),
                    locationStrength
                );

                finalIntensity += profile.EmissiveIntensityAdd * locationStrength;

                finalColor = Color.Lerp(
                    finalColor,
                    profile.EmissiveTint,
                    profile.EmissiveTintStrength * locationStrength
                );
            }
        }

        if (_usePulse)
        {
            float pulse = Mathf.Sin((GetTime() + _pulseOffset) * _pulseSpeed);
            pulse = pulse * 0.5f + 0.5f;

            finalIntensity *= 1.0f + pulse * _pulseAmount;
        }

        if (_useSubtleFlicker)
        {
            float noise = Mathf.PerlinNoise(
                _pulseOffset,
                GetTime() * _flickerSpeed
            );

            float flicker = Mathf.Lerp(
                1.0f - _flickerAmount,
                1.0f + _flickerAmount,
                noise
            );

            finalIntensity *= flicker;
        }

        if (_forceFullLight)
        {
            finalIntensity = Mathf.Max(finalIntensity, _baseIntensity * 2.5f);
            finalRange = Mathf.Max(finalRange, _baseRange * 1.2f);
        }

        if (_disableLight)
        {
            finalIntensity = 0f;
        }

        for (int i = 0; i < _lights.Length; i++)
        {
            Light light = _lights[i];

            if (light == null)
                continue;

            light.color = finalColor;
            light.intensity = Mathf.Max(0f, finalIntensity);
            light.range = Mathf.Max(0.01f, finalRange);
        }
    }

    public void SetRuntimeColor(Color color)
    {
        if (!_allowRuntimeOverride)
            return;

        _runtimeColorOverride = color;
        _hasRuntimeColorOverride = true;
        Apply();
    }

    public void ClearRuntimeColor()
    {
        _hasRuntimeColorOverride = false;
        Apply();
    }

    public void SetExternalIntensity(float intensity)
    {
        _externalIntensity = Mathf.Max(0f, intensity);
        Apply();
    }

    private float GetTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif

        return Time.time;
    }
}