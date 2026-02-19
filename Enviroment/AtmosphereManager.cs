using UnityEngine;
using UnityEngine.Rendering;

public class AtmosphereManager : MonoBehaviour
{
    public static AtmosphereManager Instance { get; private set; }

    [Header("Global Components")]
    [SerializeField] private Volume _globalVolume;
    [SerializeField] private float _transitionSpeed = 1.0f; // Rychlost lerpu

    // Aktuální stav "síly" lokace (0 = čistý Day/Night, 1 = plná Lokace)
    private float _locationInfluence = 0f;
    private LocationProfile _currentProfile;

    // Cílové hodnoty
    private float _targetInfluence = 0f;

    private GameObject _currentParticleInstance;

    private void Awake()
    {
        Instance = this;
    }

    private void Update()
    {
        // Plynulý přechod influence (síly efektu lokace)
        _locationInfluence = Mathf.MoveTowards(_locationInfluence, _targetInfluence, Time.deltaTime * _transitionSpeed);

        // Získání základních dat z DayNightCycle
        Color baseFog = Color.grey;
        Color baseAmbient = Color.grey;

        if (DayNightCycle.Instance != null)
        {
            baseFog = DayNightCycle.Instance.CurrentFogColor;
            baseAmbient = DayNightCycle.Instance.CurrentAmbientColor;
        }

        // Pokud jsme v lokaci, mícháme barvy
        if (_currentProfile != null && _locationInfluence > 0.01f)
        {
            // LOGIKA MÍCHÁNÍ:
            // 1. Zjistíme světlost (Luminance) aktuálního dne/noci.
            // 2. Aplikujeme barvu lokace na tuto světlost.
            
            // Jednodušší varianta: Lerp mezi BaseFog a (BaseFog * ProfileFog * Multiplier)
            // Násobení (Multiply) zajistí, že v noci bude mlha tmavá, i když je profil zelený.
            
            // "Tint" efekt:
            Color locationFogTinted = baseFog * _currentProfile.FogColor * 2.0f; 
            Color locationAmbientTinted = baseAmbient * _currentProfile.AmbientColor * 2.0f;
            
            // Finální Lerp podle toho, jak hluboko v lokaci jsme (_locationInfluence)
            RenderSettings.fogColor = Color.Lerp(baseFog, locationFogTinted, _locationInfluence);
            RenderSettings.ambientLight = Color.Lerp(baseAmbient, locationAmbientTinted, _locationInfluence);
            
            // Hustotu mlhy (Density) obvykle chceme přepsat úplně, ne míchat
            // Ale musíme mít nějakou "default" density z DayNightCycle, pokud ji nemáš, použijeme default scény.
            float defaultDensity = 0.01f; // Nebo si to vytáhni z DayNightCycle
            RenderSettings.fogDensity = Mathf.Lerp(defaultDensity, _currentProfile.FogDensity, _locationInfluence);
        }
        else
        {
            // Čistý Day/Night cyklus
            RenderSettings.fogColor = baseFog;
            RenderSettings.ambientLight = baseAmbient;
            // RenderSettings.fogDensity = defaultDensity;
        }
    }

    public void EnterLocation(LocationProfile profile)
    {
        if (profile == null) return;
        
        _currentProfile = profile;
        _targetInfluence = 1.0f; // Jdeme do plného efektu

        HandleParticleSwap(profile.AmbientParticlesPrefab);
        
        // Post-Process volume řešíme zvlášť (tady se hodí Weight)
        if (_globalVolume != null && profile.PostProcessProfile != null)
        {
             _globalVolume.profile = profile.PostProcessProfile;
             // Tip: Pro profi přechod PP bys potřeboval 2 Volumes a měnit jim Weight.
             // Pro teď stačí výměna.
        }
    }

    public void ExitLocation()
    {
        // Začneme se vracet k čistému Day/Night
        _targetInfluence = 0.0f; 
        
        // Částice a PP necháme doběhnout nebo vypneme
        if (_currentParticleInstance != null) Destroy(_currentParticleInstance, 2.0f);
        _currentParticleInstance = null;
    }

    private void HandleParticleSwap(GameObject newPrefab)
    {
        if (_currentParticleInstance != null) Destroy(_currentParticleInstance);
        if (newPrefab != null)
        {
            Transform cam = Camera.main ? Camera.main.transform : transform;
            _currentParticleInstance = Instantiate(newPrefab, cam);
            _currentParticleInstance.transform.localPosition = Vector3.zero;
        }
    }
}