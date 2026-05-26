using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "NewLocationProfile", menuName = "Environment/Location Profile")]
public class LocationProfile : ScriptableObject
{
    [Header("Identifikace")]
    public string LocationName;

    [Header("Environment Lighting & Fog")]
    [Tooltip("Barevný tint mlhy pro tuto lokaci. Nemá úplně přepsat den/noc, ale obarvit ji.")]
    public Color FogColor = new Color(0.5f, 0.55f, 0.65f, 1f);

    [Range(0f, 1f)]
    [Tooltip("Jak silně se barva lokace promítne do aktuální denní/noční mlhy.")]
    public float FogTintStrength = 0.65f;

    [Min(0f)]
    [Tooltip("Násobič tintu mlhy. Vyšší hodnota dělá lokaci barevnější.")]
    public float FogTintMultiplier = 1.75f;

    [Tooltip("Cílová hustota mlhy v lokaci.")]
    public float FogDensity = 0.02f;

    [Range(0f, 1f)]
    [Tooltip("0 = použije se hlavně day/night fog density, 1 = lokace silně přepíše fog density.")]
    public float FogDensityOverride = 0.75f;

    [Min(0f)]
    [Tooltip("Násobič základní day/night hustoty mlhy.")]
    public float FogDensityMultiplier = 1.0f;

    [ColorUsage(false, true)]
    [Tooltip("Barevný tint ambientního světla pro lokaci.")]
    public Color AmbientColor = new Color(0.2f, 0.22f, 0.28f, 1f);

    [Range(0f, 1f)]
    [Tooltip("Jak silně lokace tintuje ambient.")]
    public float AmbientTintStrength = 0.55f;

    [Min(0f)]
    [Tooltip("Násobič ambient tintu.")]
    public float AmbientTintMultiplier = 1.6f;

    [Header("Post Processing - Legacy Optional")]
    [Tooltip("Volitelný starší Volume Profile. Nový AtmosphereManager ho defaultně nevyměňuje, protože hodnoty řídí plynule sám.")]
    public VolumeProfile PostProcessProfile;

    [Header("Post Processing Mood Offsets")]
    [Tooltip("Posun expozice v lokaci. Záporné hodnoty dělají lokaci temnější.")]
    public float PostExposureOffset = 0.0f;

    [Tooltip("Posun kontrastu v lokaci. Rozumné hodnoty jsou třeba -10 až +25.")]
    public float ContrastOffset = 0.0f;

    [Tooltip("Posun saturace v lokaci. Rozumné hodnoty jsou třeba -20 až +20.")]
    public float SaturationOffset = 0.0f;

    [Tooltip("Násobič bloom intenzity v lokaci.")]
    public float BloomMultiplier = 1.0f;

    [Tooltip("Přídavek bloom intenzity v lokaci.")]
    public float BloomAdd = 0.0f;

    [Tooltip("Posun bloom thresholdu. Nižší threshold = víc věcí začne svítit.")]
    public float BloomThresholdOffset = 0.0f;

    [Tooltip("Přídavek viněty v lokaci.")]
    public float VignetteAdd = 0.0f;

    [Tooltip("Posun White Balance temperature. Záporné = chladnější, kladné = teplejší.")]
    public float TemperatureOffset = 0.0f;

    [Tooltip("Posun White Balance tint. Záporné = zelenější, kladné = fialovější.")]
    public float TintOffset = 0.0f;

    [Header("Emissive Mood")]
    [Tooltip("Násobič emissive intenzity pro magické objekty v této lokaci.")]
    [Min(0f)]
    public float EmissiveIntensityMultiplier = 1.0f;

    [Tooltip("Přídavek emissive intenzity v této lokaci.")]
    [Min(0f)]
    public float EmissiveIntensityAdd = 0.0f;

    [ColorUsage(true, true)]
    [Tooltip("Barevný tint glow objektů v této lokaci.")]
    public Color EmissiveTint = Color.white;

    [Range(0f, 1f)]
    [Tooltip("Jak moc lokace přebarví emissive objekty.")]
    public float EmissiveTintStrength = 0.0f;

    [Header("Particles - Ambient Juice")]
    [Tooltip("Prefab s částicemi, například pyl, prach, popílek, magické motes.")]
    public GameObject AmbientParticlesPrefab;

    [Min(0f)]
    [Tooltip("Intenzita ambient particles v lokaci.")]
    public float AmbientParticleIntensity = 1.0f;

    [Tooltip("Pokud je zapnuto, particles se přichytí na hlavní kameru. Dobré pro prach/motes kolem hráče.")]
    public bool ParentParticlesToCamera = true;

    [Tooltip("Lokální offset particle prefabu vůči kameře nebo AtmosphereManageru.")]
    public Vector3 AmbientParticlesLocalOffset = Vector3.zero;

    [Header("Audio - Music Layer")]
    [Tooltip("Hudba hrající přes den. Vybere se náhodně.")]
    public List<AudioClip> DayTracks;

    [Tooltip("Hudba hrající v noci. Vybere se náhodně.")]
    public List<AudioClip> NightTracks;
}