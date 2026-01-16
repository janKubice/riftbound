using UnityEngine;
using UnityEngine.Rendering; // Potřeba pro AmbientMode

[ExecuteAlways]
public class DayNightCycle : MonoBehaviour
{
    [Header("Čas")]
    [Range(0, 24)] public float TimeOfDay = 12.0f;
    public float DayDurationInSeconds = 120.0f;

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
    public Gradient AmbientColor; // Nový gradient pro stíny
    public AnimationCurve LightIntensity; 
    public AnimationCurve StarVisibility;

    private void Start()
    {
        // 1. Důležité: Přepneme Unity z "Skybox Lighting" na "Color Lighting"
        // Abychom nad tím měli 100% kontrolu skriptem
        RenderSettings.ambientMode = AmbientMode.Flat;
    }

    private void Update()
    {
        if (_skyboxMaterial == null) 
        {
            _skyboxMaterial = RenderSettings.skybox;
            if (_skyboxMaterial == null) return;
        }
        if (_directionalLight == null) return;

        if (Application.isPlaying)
        {
            TimeOfDay += (Time.deltaTime / DayDurationInSeconds) * 24.0f;
            if (TimeOfDay >= 24.0f) TimeOfDay = 0.0f;
        }

        UpdateVisuals();
    }

    private void UpdateVisuals()
    {
        float timePercent = TimeOfDay / 24.0f;

        // Rotace Slunce
        float sunAngle = (timePercent * 360.0f) - 90.0f; 
        _directionalLight.transform.localRotation = Quaternion.Euler(sunAngle, 170.0f, 0);

        // Barvy Oblohy
        _skyboxMaterial.SetColor("_TopColor", TopColor.Evaluate(timePercent));
        _skyboxMaterial.SetColor("_HorizonColor", HorizonColor.Evaluate(timePercent));
        _skyboxMaterial.SetColor("_BottomColor", BottomColor.Evaluate(timePercent));
        _skyboxMaterial.SetColor("_SunColor", SunColor.Evaluate(timePercent));
        _skyboxMaterial.SetColor("_MoonColor", MoonColor.Evaluate(timePercent));

        // Mlha
        RenderSettings.fogColor = FogColor.Evaluate(timePercent);
        
        // Hvězdy
        _skyboxMaterial.SetFloat("_StarIntensity", StarVisibility.Evaluate(timePercent));

        // Intenzita slunce/měsíce
        _directionalLight.intensity = LightIntensity.Evaluate(timePercent);

        // --- NOVÉ: AMBIENT LIGHT (TMA) ---
        // Toto barví všechno, co je ve stínu.
        RenderSettings.ambientLight = AmbientColor.Evaluate(timePercent);
        
        Shader.SetGlobalVector("_SunDirection", -_directionalLight.transform.forward);
    }
    
    public string GetFormattedTime()
    {
        int hours = Mathf.FloorToInt(TimeOfDay);
        int minutes = Mathf.FloorToInt((TimeOfDay - hours) * 60);
        return $"{hours:00}:{minutes:00}";
    }
}