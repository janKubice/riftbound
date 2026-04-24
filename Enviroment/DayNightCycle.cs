using UnityEngine;

[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    public static DayNightCycle Instance { get; private set; }

    [Header("Čas")]
    [Range(0, 24)] public float TimeOfDay = 12.0f;
    public float DayDurationInSeconds = 120.0f;
    public float TimePercent { get; private set; }

    [Header("Odkazy")]
    [SerializeField] private Light _directionalLight;
    [SerializeField] private Material _skyboxMaterial;

    [Header("Barvy Oblohy")]
    public Gradient TopColor;
    public Gradient HorizonColor;
    public Gradient BottomColor;
    public Gradient FogColor;

    [Header("Nebeská Tělesa")]
    public Gradient SunColor;
    public Gradient MoonColor;

    [Header("Osvětlení Scény")]
    public Gradient AmbientColor;
    public AnimationCurve LightIntensity;
    public AnimationCurve StarVisibility;

    public Color CurrentFogColor { get; private set; }
    public Color CurrentAmbientColor { get; private set; }

    // --- OPTIMALIZACE: Cachované ID shader properties ---
    private static readonly int _TopColorID = Shader.PropertyToID("_TopColor");
    private static readonly int _HorizonColorID = Shader.PropertyToID("_HorizonColor");
    private static readonly int _BottomColorID = Shader.PropertyToID("_BottomColor");
    private static readonly int _SunColorID = Shader.PropertyToID("_SunColor");
    private static readonly int _MoonColorID = Shader.PropertyToID("_MoonColor");
    private static readonly int _StarIntensityID = Shader.PropertyToID("_StarIntensity");
    private static readonly int _SunDirectionID = Shader.PropertyToID("_SunDirection");

    public System.Action<float> OnAtmosphereUpdated;

    private void Awake()
    {
        if (Application.isPlaying) Instance = this;
    }

    private void Start()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
    }

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        // Validace materiálu (editor safe)
        if (_skyboxMaterial == null)
        {
            _skyboxMaterial = RenderSettings.skybox;
            if (_skyboxMaterial == null) return;
        }

        // Posun času
        if (Application.isPlaying)
        {
            TimeOfDay += (Time.deltaTime / DayDurationInSeconds) * 24.0f;
            if (TimeOfDay >= 24.0f) TimeOfDay %= 24.0f;
        }

        UpdateCalculations();
    }

    private void UpdateCalculations()
    {
        TimePercent = TimeOfDay / 24.0f;

        // 1. Rotace Slunce
        // -90 je východ, 90 západ (přibližně), 270 půlnoc
        float sunAngle = (TimePercent * 360.0f) - 90.0f;

        if (_directionalLight != null)
        {
            _directionalLight.transform.localRotation = Quaternion.Euler(sunAngle, 170.0f, 0);
            _directionalLight.intensity = LightIntensity.Evaluate(TimePercent);

            // Nastavení globálního vektoru pro shader
            // Důležité: Vector musí mířit KE slunci (proto -forward)
            Shader.SetGlobalVector(_SunDirectionID, -_directionalLight.transform.forward);
        }

        // 2. Barvy Skyboxu (použití ID místo stringů)
        _skyboxMaterial.SetColor(_TopColorID, TopColor.Evaluate(TimePercent));
        _skyboxMaterial.SetColor(_HorizonColorID, HorizonColor.Evaluate(TimePercent));
        _skyboxMaterial.SetColor(_BottomColorID, BottomColor.Evaluate(TimePercent));
        _skyboxMaterial.SetColor(_SunColorID, SunColor.Evaluate(TimePercent));
        _skyboxMaterial.SetColor(_MoonColorID, MoonColor.Evaluate(TimePercent));
        _skyboxMaterial.SetFloat(_StarIntensityID, StarVisibility.Evaluate(TimePercent));

        // 3. Okolní prostředí
        CurrentFogColor = FogColor.Evaluate(TimePercent);
        CurrentAmbientColor = AmbientColor.Evaluate(TimePercent);

        RenderSettings.fogColor = CurrentFogColor;
        RenderSettings.ambientLight = CurrentAmbientColor;
        OnAtmosphereUpdated?.Invoke(TimePercent);
    }

    public string GetFormattedTime()
    {
        int hours = Mathf.FloorToInt(TimeOfDay);
        int minutes = Mathf.FloorToInt((TimeOfDay - hours) * 60);
        return $"{hours:00}:{minutes:00}";
    }
}