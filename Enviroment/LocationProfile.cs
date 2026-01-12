using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "NewLocationProfile", menuName = "Enviroment/Location Profile")]
public class LocationProfile : ScriptableObject
{
    [Header("Identifikace")]
    public string LocationName;

    [Header("Environment Lighting & Fog")]
    public Color FogColor = new Color(0.5f, 0.5f, 0.5f);
    public float FogDensity = 0.02f;
    [ColorUsage(false, true)] public Color AmbientColor = new Color(0.2f, 0.2f, 0.2f); // HDR barva

    [Header("Post Processing")]
    [Tooltip("Volume Profile specifický pro tuto oblast (Color Grading atd.)")]
    public VolumeProfile PostProcessProfile;

    [Header("Particles (Juice)")]
    [Tooltip("Prefab s částicemi (např. poletující listí). Musí mít ParticleSystem.")]
    public GameObject AmbientParticlesPrefab;
}