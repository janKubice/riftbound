using UnityEngine;
using UnityEngine.Rendering;
using System.Collections;

public class AtmosphereManager : MonoBehaviour
{
    public static AtmosphereManager Instance { get; private set; }

    [Header("Global Components")]
    [SerializeField] private Volume _globalVolume; // Odkaz na Global Volume v scéně
    [SerializeField] private float _transitionDuration = 2.0f;

    [Header("Default Settings")]
    [SerializeField] private LocationProfile _defaultProfile;

    private Coroutine _transitionRoutine;
    private GameObject _currentParticleInstance;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // Nastavit výchozí stav
        if (_defaultProfile != null)
            SetAtmosphereImmediate(_defaultProfile);
    }

    /// <summary>
    /// Zavolá trigger, když do něj hráč vstoupí.
    /// </summary>
    public void EnterLocation(LocationProfile profile)
    {
        if (profile == null) return;

        if (_transitionRoutine != null) StopCoroutine(_transitionRoutine);
        _transitionRoutine = StartCoroutine(TransitionRoutine(profile));
    }

    private IEnumerator TransitionRoutine(LocationProfile target)
    {
        // 1. Particle Swap (pokud je jiný)
        HandleParticleSwap(target.AmbientParticlesPrefab);

        // 2. Post-Process Volume Blending
        // Unity Volumes umí blendovat, pokud máš jeden globální a měníš mu profil.
        // Pro plynulost zde vyměníme profil, ale Unity neumí nativně lerpovat dva profily skriptem snadno.
        // Trik: Můžeme měnit váhu (Weight), ale pro jednoduchost zde přepneme profil
        // a budeme manuálně lerpovat Fog a Ambient, což dělá 80% atmosféry.
        
        if (target.PostProcessProfile != null && _globalVolume != null)
        {
            _globalVolume.profile = target.PostProcessProfile;
        }

        // 3. Lerp Environment Values (Fog & Ambient)
        float timer = 0f;
        
        Color startFogColor = RenderSettings.fogColor;
        float startFogDensity = RenderSettings.fogDensity;
        Color startAmbient = RenderSettings.ambientLight;

        while (timer < _transitionDuration)
        {
            timer += Time.deltaTime;
            float t = timer / _transitionDuration;
            // SmoothStep pro hezčí přechod
            t = t * t * (3f - 2f * t);

            RenderSettings.fogColor = Color.Lerp(startFogColor, target.FogColor, t);
            RenderSettings.fogDensity = Mathf.Lerp(startFogDensity, target.FogDensity, t);
            RenderSettings.ambientLight = Color.Lerp(startAmbient, target.AmbientColor, t);

            yield return null;
        }
    }

    private void SetAtmosphereImmediate(LocationProfile profile)
    {
        RenderSettings.fogColor = profile.FogColor;
        RenderSettings.fogDensity = profile.FogDensity;
        RenderSettings.ambientLight = profile.AmbientColor;
        
        if (_globalVolume != null && profile.PostProcessProfile != null)
            _globalVolume.profile = profile.PostProcessProfile;

        HandleParticleSwap(profile.AmbientParticlesPrefab);
    }

    private void HandleParticleSwap(GameObject newPrefab)
    {
        // Pokud už máme instanci a je to ta samá, nic neděláme
        // Pokud je to něco jiného, starou zničíme
        if (_currentParticleInstance != null)
        {
            // Zde bychom mohli udělat FadeOut starých částic, pro teď Destroy
            Destroy(_currentParticleInstance);
        }

        if (newPrefab != null)
        {
            // Instancujeme jako child kamery nebo hráče, aby to "cestovalo" s ním
            // Ale POZOR: Particle System musí být nastaven na Simulation Space: World,
            // jinak se budou částice točit s kamerou, což vypadá divně.
            Transform cameraTransform = Camera.main != null ? Camera.main.transform : transform;
            _currentParticleInstance = Instantiate(newPrefab, cameraTransform);
            _currentParticleInstance.transform.localPosition = Vector3.zero; 
        }
    }
}