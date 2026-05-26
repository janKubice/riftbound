using System;
using UnityEngine;

[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Čas")]
    [Range(0f, 24f)] public float TimeOfDay = 12.0f;

    [Min(1f)]
    public float DayDurationInSeconds = 120.0f;

    [Tooltip("Pokud je zapnuto, čas běží v Play Mode.")]
    public bool ProgressTimeInPlayMode = true;

    [Tooltip("Pokud je zapnuto, atmosféra se aktualizuje i v Edit Mode.")]
    public bool UpdateInEditMode = true;

    [Header("Odkazy")]
    [SerializeField] private Light _directionalLight;
    [SerializeField] private Material _skyboxMaterial;

    [Header("Fog")]
    [SerializeField] private bool _controlRenderSettingsFog = true;
    [SerializeField] private FogMode _fogMode = FogMode.ExponentialSquared;

    [Tooltip("Hustota mlhy podle času dne. X = TimePercent 0-1.")]
    public AnimationCurve FogDensity = new AnimationCurve(
        new Keyframe(0.00f, 0.018f), // půlnoc
        new Keyframe(0.23f, 0.024f), // před svítáním
        new Keyframe(0.33f, 0.014f), // ráno
        new Keyframe(0.50f, 0.006f), // poledne
        new Keyframe(0.72f, 0.018f), // západ
        new Keyframe(1.00f, 0.018f)  // půlnoc
    );

    [Header("Barvy Oblohy")]
    public Gradient TopColor;
    public Gradient HorizonColor;
    public Gradient BottomColor;
    public Gradient FogColor;

    [Header("Skybox Mood")]
    public Gradient HorizonGlowColor;
    public AnimationCurve HorizonGlowIntensity = new AnimationCurve(
        new Keyframe(0.00f, 0.15f),
        new Keyframe(0.23f, 0.85f),
        new Keyframe(0.50f, 0.20f),
        new Keyframe(0.72f, 1.15f),
        new Keyframe(1.00f, 0.15f)
    );

    public Gradient SunGlowColor;
    public AnimationCurve SunGlowIntensity = new AnimationCurve(
        new Keyframe(0.00f, 0.00f),
        new Keyframe(0.23f, 0.90f),
        new Keyframe(0.50f, 0.45f),
        new Keyframe(0.72f, 1.25f),
        new Keyframe(1.00f, 0.00f)
    );

    public Gradient MoonGlowColor;
    public AnimationCurve MoonGlowIntensity = new AnimationCurve(
        new Keyframe(0.00f, 0.70f),
        new Keyframe(0.25f, 0.10f),
        new Keyframe(0.50f, 0.00f),
        new Keyframe(0.75f, 0.25f),
        new Keyframe(1.00f, 0.70f)
    );

    public Gradient CloudTint;
    public AnimationCurve CloudOpacity = new AnimationCurve(
        new Keyframe(0.00f, 0.08f),
        new Keyframe(0.25f, 0.12f),
        new Keyframe(0.50f, 0.16f),
        new Keyframe(0.72f, 0.20f),
        new Keyframe(1.00f, 0.08f)
    );

    public Gradient NebulaColor;
    public AnimationCurve NebulaIntensity = new AnimationCurve(
        new Keyframe(0.00f, 0.45f),
        new Keyframe(0.25f, 0.05f),
        new Keyframe(0.50f, 0.00f),
        new Keyframe(0.75f, 0.18f),
        new Keyframe(1.00f, 0.45f)
    );

    [Header("Nebeská Tělesa")]
    public Gradient SunColor;
    public Gradient MoonColor;

    [Header("Osvětlení Scény")]
    public Gradient AmbientColor;
    public AnimationCurve LightIntensity = new AnimationCurve(
        new Keyframe(0.00f, 0.05f),
        new Keyframe(0.25f, 0.15f),
        new Keyframe(0.50f, 1.00f),
        new Keyframe(0.75f, 0.15f),
        new Keyframe(1.00f, 0.05f)
    );

    public AnimationCurve StarVisibility = new AnimationCurve(
        new Keyframe(0.00f, 1.00f),
        new Keyframe(0.22f, 0.80f),
        new Keyframe(0.30f, 0.00f),
        new Keyframe(0.70f, 0.00f),
        new Keyframe(0.80f, 0.80f),
        new Keyframe(1.00f, 1.00f)
    );

    [Header("Rotace slunce")]
    [SerializeField] private float _sunYaw = 170.0f;
    [SerializeField] private float _sunPitchOffset = -90.0f;

    [Header("Debug")]
    [SerializeField] private bool _logMissingReferences = false;

    public float TimePercent { get; private set; }
    public Color CurrentFogColor { get; private set; }
    public float CurrentFogDensity { get; private set; }
    public Color CurrentAmbientColor { get; private set; }
    public Color CurrentSunColor { get; private set; }
    public Color CurrentMoonColor { get; private set; }
    public float CurrentLightIntensity { get; private set; }
    public float CurrentStarVisibility { get; private set; }
    public Vector3 CurrentSunDirection { get; private set; }

    public event Action<DayNightCycle> OnAtmosphereUpdated;

    private static readonly int TopColorID = Shader.PropertyToID("_TopColor");
    private static readonly int HorizonColorID = Shader.PropertyToID("_HorizonColor");
    private static readonly int BottomColorID = Shader.PropertyToID("_BottomColor");
    private static readonly int SunColorID = Shader.PropertyToID("_SunColor");
    private static readonly int MoonColorID = Shader.PropertyToID("_MoonColor");
    private static readonly int StarIntensityID = Shader.PropertyToID("_StarIntensity");
    private static readonly int SunDirectionID = Shader.PropertyToID("_SunDirection");

    private static readonly int HorizonGlowColorID = Shader.PropertyToID("_HorizonGlowColor");
    private static readonly int HorizonGlowIntensityID = Shader.PropertyToID("_HorizonGlowIntensity");

    private static readonly int SunGlowColorID = Shader.PropertyToID("_SunGlowColor");
    private static readonly int SunGlowIntensityID = Shader.PropertyToID("_SunGlowIntensity");

    private static readonly int MoonGlowColorID = Shader.PropertyToID("_MoonGlowColor");
    private static readonly int MoonGlowIntensityID = Shader.PropertyToID("_MoonGlowIntensity");

    private static readonly int CloudTintID = Shader.PropertyToID("_CloudTint");
    private static readonly int CloudOpacityID = Shader.PropertyToID("_CloudOpacity");

    private static readonly int NebulaColorID = Shader.PropertyToID("_NebulaColor");
    private static readonly int NebulaIntensityID = Shader.PropertyToID("_NebulaIntensity");

    private void OnEnable()
    {
        Instance = this;

        if (_skyboxMaterial == null)
            _skyboxMaterial = RenderSettings.skybox;

        ApplyStaticRenderSettings();
        UpdateCalculations();
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Reset()
    {
        if (_directionalLight == null)
            _directionalLight = FindFirstObjectByType<Light>();

        if (_skyboxMaterial == null)
            _skyboxMaterial = RenderSettings.skybox;
    }

    private void OnValidate()
    {
        TimeOfDay = Mathf.Repeat(TimeOfDay, 24f);

        if (!Application.isPlaying && UpdateInEditMode)
        {
            if (_skyboxMaterial == null)
                _skyboxMaterial = RenderSettings.skybox;

            ApplyStaticRenderSettings();
            UpdateCalculations();
        }
    }

    private void Start()
    {
        ApplyStaticRenderSettings();
        UpdateCalculations();
    }

    private void Update()
    {
        if (!Application.isPlaying && !UpdateInEditMode)
            return;

        if (Application.isPlaying && ProgressTimeInPlayMode)
        {
            TimeOfDay += (Time.deltaTime / DayDurationInSeconds) * 24.0f;
            TimeOfDay = Mathf.Repeat(TimeOfDay, 24.0f);
        }

        UpdateCalculations();
    }

    private void ApplyStaticRenderSettings()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

        if (_controlRenderSettingsFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = _fogMode;
        }
    }

    private void UpdateCalculations()
    {
        TimePercent = Mathf.Clamp01(TimeOfDay / 24.0f);

        EvaluateCurrentValues(TimePercent);
        ApplyDirectionalLight(TimePercent);
        ApplySkybox();
        ApplyRenderSettings();

        OnAtmosphereUpdated?.Invoke(this);
    }

    private void EvaluateCurrentValues(float timePercent)
    {
        CurrentFogColor = EvaluateGradientSafe(FogColor, timePercent, Color.gray);
        CurrentFogDensity = Mathf.Max(0f, FogDensity.Evaluate(timePercent));

        CurrentAmbientColor = EvaluateGradientSafe(AmbientColor, timePercent, Color.gray);
        CurrentSunColor = EvaluateGradientSafe(SunColor, timePercent, Color.white);
        CurrentMoonColor = EvaluateGradientSafe(MoonColor, timePercent, new Color(0.75f, 0.8f, 1f, 1f));

        CurrentLightIntensity = Mathf.Max(0f, LightIntensity.Evaluate(timePercent));
        CurrentStarVisibility = Mathf.Max(0f, StarVisibility.Evaluate(timePercent));
    }

    private void ApplyDirectionalLight(float timePercent)
    {
        float sunAngle = (timePercent * 360.0f) + _sunPitchOffset;

        if (_directionalLight == null)
        {
            if (_logMissingReferences)
                Debug.LogWarning($"{nameof(DayNightCycle)}: Directional Light není přiřazený.", this);

            CurrentSunDirection = Vector3.down;
            Shader.SetGlobalVector(SunDirectionID, CurrentSunDirection);
            return;
        }

        _directionalLight.transform.localRotation = Quaternion.Euler(sunAngle, _sunYaw, 0f);
        _directionalLight.intensity = CurrentLightIntensity;
        _directionalLight.color = CurrentSunColor;

        CurrentSunDirection = -_directionalLight.transform.forward;

        Shader.SetGlobalVector(SunDirectionID, CurrentSunDirection);
    }

    private void ApplySkybox()
    {
        if (_skyboxMaterial == null)
        {
            _skyboxMaterial = RenderSettings.skybox;

            if (_skyboxMaterial == null)
            {
                if (_logMissingReferences)
                    Debug.LogWarning($"{nameof(DayNightCycle)}: Skybox material není přiřazený a RenderSettings.skybox je null.", this);

                return;
            }
        }

        _skyboxMaterial.SetColor(TopColorID, EvaluateGradientSafe(TopColor, TimePercent, Color.blue));
        _skyboxMaterial.SetColor(HorizonColorID, EvaluateGradientSafe(HorizonColor, TimePercent, Color.cyan));
        _skyboxMaterial.SetColor(BottomColorID, EvaluateGradientSafe(BottomColor, TimePercent, Color.black));
        _skyboxMaterial.SetColor(SunColorID, CurrentSunColor);
        _skyboxMaterial.SetColor(MoonColorID, CurrentMoonColor);
        _skyboxMaterial.SetFloat(StarIntensityID, CurrentStarVisibility);
        SetSkyboxColorIfExists(
            HorizonGlowColorID,
            EvaluateGradientSafe(HorizonGlowColor, TimePercent, new Color(1f, 0.45f, 0.25f, 1f))
        );

        SetSkyboxFloatIfExists(
            HorizonGlowIntensityID,
            Mathf.Max(0f, HorizonGlowIntensity.Evaluate(TimePercent))
        );

        SetSkyboxColorIfExists(
            SunGlowColorID,
            EvaluateGradientSafe(SunGlowColor, TimePercent, CurrentSunColor)
        );

        SetSkyboxFloatIfExists(
            SunGlowIntensityID,
            Mathf.Max(0f, SunGlowIntensity.Evaluate(TimePercent))
        );

        SetSkyboxColorIfExists(
            MoonGlowColorID,
            EvaluateGradientSafe(MoonGlowColor, TimePercent, CurrentMoonColor)
        );

        SetSkyboxFloatIfExists(
            MoonGlowIntensityID,
            Mathf.Max(0f, MoonGlowIntensity.Evaluate(TimePercent))
        );

        SetSkyboxColorIfExists(
            CloudTintID,
            EvaluateGradientSafe(CloudTint, TimePercent, Color.white)
        );

        SetSkyboxFloatIfExists(
            CloudOpacityID,
            Mathf.Clamp01(CloudOpacity.Evaluate(TimePercent))
        );

        SetSkyboxColorIfExists(
            NebulaColorID,
            EvaluateGradientSafe(NebulaColor, TimePercent, new Color(0.35f, 0.25f, 0.75f, 1f))
        );

        SetSkyboxFloatIfExists(
            NebulaIntensityID,
            Mathf.Max(0f, NebulaIntensity.Evaluate(TimePercent))
        );
    }

    private void ApplyRenderSettings()
    {
        RenderSettings.ambientLight = CurrentAmbientColor;

        if (!_controlRenderSettingsFog)
            return;

        RenderSettings.fog = true;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogColor = CurrentFogColor;
        RenderSettings.fogDensity = CurrentFogDensity;
    }

    private static Color EvaluateGradientSafe(Gradient gradient, float time, Color fallback)
    {
        if (gradient == null)
            return fallback;

        return gradient.Evaluate(time);
    }

    public string GetFormattedTime()
    {
        int hours = Mathf.FloorToInt(TimeOfDay);
        int minutes = Mathf.FloorToInt((TimeOfDay - hours) * 60f);
        return $"{hours:00}:{minutes:00}";
    }

    public bool IsNight()
    {
        return TimeOfDay < 6f || TimeOfDay >= 20f;
    }

    public bool IsDay()
    {
        return TimeOfDay >= 7f && TimeOfDay < 18f;
    }

    public bool IsDawnOrDusk()
    {
        return !IsNight() && !IsDay();
    }

    public void ForceUpdateAtmosphere()
    {
        ApplyStaticRenderSettings();
        UpdateCalculations();
    }

    private void SetSkyboxColorIfExists(int propertyId, Color color)
    {
        if (_skyboxMaterial == null)
            return;

        if (_skyboxMaterial.HasProperty(propertyId))
            _skyboxMaterial.SetColor(propertyId, color);
    }

    private void SetSkyboxFloatIfExists(int propertyId, float value)
    {
        if (_skyboxMaterial == null)
            return;

        if (_skyboxMaterial.HasProperty(propertyId))
            _skyboxMaterial.SetFloat(propertyId, value);
    }
}