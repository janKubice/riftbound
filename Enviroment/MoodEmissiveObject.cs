using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class MoodEmissiveObject : MonoBehaviour
{
    [Header("Renderers")]
    [SerializeField] private Renderer[] _renderers;

    [Header("Base Emission")]
    [ColorUsage(true, true)]
    [SerializeField] private Color _baseEmissionColor = new Color(0.35f, 0.85f, 1.0f, 1f);

    [Min(0f)]
    [SerializeField] private float _baseIntensity = 1.0f;

    [Header("Time Of Day")]
    [SerializeField]
    private AnimationCurve _intensityByTime = new AnimationCurve(
        new Keyframe(0.00f, 1.75f), // půlnoc
        new Keyframe(0.22f, 1.35f), // před svítáním
        new Keyframe(0.33f, 0.75f), // ráno
        new Keyframe(0.50f, 0.25f), // poledne
        new Keyframe(0.72f, 1.20f), // západ
        new Keyframe(0.82f, 1.80f), // noc
        new Keyframe(1.00f, 1.75f)
    );

    [Header("Location Influence")]
    [SerializeField] private bool _useAtmosphereLocationInfluence = true;

    [Tooltip("Jak moc lokace ovlivňuje intenzitu glow.")]
    [Range(0f, 1f)]
    [SerializeField] private float _locationInfluenceStrength = 1.0f;

    [Header("Pulse")]
    [SerializeField] private bool _usePulse = true;

    [Min(0f)]
    [SerializeField] private float _pulseSpeed = 1.2f;

    [Range(0f, 1f)]
    [SerializeField] private float _pulseAmount = 0.18f;

    [SerializeField] private bool _randomizePulseOffset = true;

    [SerializeField] private float _manualPulseOffset = 0f;

    [Header("Material Properties")]
    [SerializeField] private bool _useMaterialPropertyBlocks = true;

    [Tooltip("Zapne _EMISSION keyword na materiálech. Nutné hlavně pro URP Lit.")]
    [SerializeField] private bool _enableEmissionKeyword = true;

    [Tooltip("Běžné názvy emissive properties. Nechte více možností, pokud používáte různé shadery.")]
    [SerializeField]
    private string[] _emissionColorPropertyNames =
    {
        "_EmissionColor",
        "_EmissiveColor",
        "_GlowColor",
        "_Emission"
    };

    [Tooltip("Volitelné intensity property pro custom shadery.")]
    [SerializeField]
    private string[] _emissionIntensityPropertyNames =
    {
        "_EmissionIntensity",
        "_GlowIntensity",
        "_EmissiveIntensity"
    };

    [Header("Debug")]
    [SerializeField] private bool _forceFullGlow = false;
    [SerializeField] private bool _disableGlow = false;

    private MaterialPropertyBlock _block;
    private int[] _emissionColorPropertyIDs;
    private int[] _emissionIntensityPropertyIDs;
    private float _pulseOffset;

    private void Reset()
    {
        _renderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable()
    {
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);

        CachePropertyIDs();

        if (_block == null)
            _block = new MaterialPropertyBlock();

        if (_randomizePulseOffset)
            _pulseOffset = Random.Range(0f, 100f);
        else
            _pulseOffset = _manualPulseOffset;

        if (_enableEmissionKeyword)
            EnableEmissionKeywords();

        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void OnValidate()
    {
        if (_renderers == null || _renderers.Length == 0)
            _renderers = GetComponentsInChildren<Renderer>(true);

        CachePropertyIDs();

        if (_block == null)
            _block = new MaterialPropertyBlock();

        if (_enableEmissionKeyword)
            EnableEmissionKeywords();

        // V editoru nepouštějte Apply na prefab assetech z Project okna.
        // Jinak Unity může házet chyby při práci s renderer property blockem.
        if (IsPrefabAssetContext())
            return;

        Apply();
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

    private void CachePropertyIDs()
    {
        if (_emissionColorPropertyNames != null)
        {
            _emissionColorPropertyIDs = new int[_emissionColorPropertyNames.Length];

            for (int i = 0; i < _emissionColorPropertyNames.Length; i++)
                _emissionColorPropertyIDs[i] = Shader.PropertyToID(_emissionColorPropertyNames[i]);
        }

        if (_emissionIntensityPropertyNames != null)
        {
            _emissionIntensityPropertyIDs = new int[_emissionIntensityPropertyNames.Length];

            for (int i = 0; i < _emissionIntensityPropertyNames.Length; i++)
                _emissionIntensityPropertyIDs[i] = Shader.PropertyToID(_emissionIntensityPropertyNames[i]);
        }
    }

    private void EnableEmissionKeywords()
    {
        if (_renderers == null)
            return;

        for (int r = 0; r < _renderers.Length; r++)
        {
            Renderer renderer = _renderers[r];

            if (renderer == null)
                continue;

            // Na prefab assetech nesaháme na renderer.materials.
            // Použijeme sharedMaterials, protože jen potřebujeme zapnout keyword na materiálu.
            Material[] materials = renderer.sharedMaterials;

            if (materials == null)
                continue;

            for (int m = 0; m < materials.Length; m++)
            {
                Material material = materials[m];

                if (material == null)
                    continue;

                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
        }
    }

    private void Apply()
    {
        if (_renderers == null || _renderers.Length == 0)
            return;

        float timePercent = DayNightCycle.Instance != null
            ? DayNightCycle.Instance.TimePercent
            : 0.5f;

        float timeIntensity = Mathf.Max(0f, _intensityByTime.Evaluate(timePercent));
        float finalIntensity = _baseIntensity * timeIntensity;

        Color finalColor = _baseEmissionColor;

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

            float pulseMultiplier = 1.0f + pulse * _pulseAmount;
            finalIntensity *= pulseMultiplier;
        }

        if (_forceFullGlow)
            finalIntensity = Mathf.Max(finalIntensity, 4f);

        if (_disableGlow)
            finalIntensity = 0f;

        Color emissiveColor = finalColor * finalIntensity;
        emissiveColor.a = 1f;

        ApplyToRenderers(emissiveColor, finalIntensity);
    }

    private void ApplyToRenderers(Color emissiveColor, float intensity)
    {
        if (_block == null)
            _block = new MaterialPropertyBlock();

        for (int r = 0; r < _renderers.Length; r++)
        {
            Renderer renderer = _renderers[r];

            if (renderer == null)
                continue;

            if (_useMaterialPropertyBlocks)
            {
                renderer.GetPropertyBlock(_block);

                if (_emissionColorPropertyIDs != null)
                {
                    for (int i = 0; i < _emissionColorPropertyIDs.Length; i++)
                        _block.SetColor(_emissionColorPropertyIDs[i], emissiveColor);
                }

                if (_emissionIntensityPropertyIDs != null)
                {
                    for (int i = 0; i < _emissionIntensityPropertyIDs.Length; i++)
                        _block.SetFloat(_emissionIntensityPropertyIDs[i], intensity);
                }

                renderer.SetPropertyBlock(_block);
            }
            else
            {
                Material[] materials = Application.isPlaying
                    ? renderer.materials
                    : renderer.sharedMaterials;

                for (int m = 0; m < materials.Length; m++)
                {
                    Material material = materials[m];

                    if (material == null)
                        continue;

                    if (_emissionColorPropertyIDs != null)
                    {
                        for (int i = 0; i < _emissionColorPropertyIDs.Length; i++)
                        {
                            if (material.HasProperty(_emissionColorPropertyIDs[i]))
                                material.SetColor(_emissionColorPropertyIDs[i], emissiveColor);
                        }
                    }

                    if (_emissionIntensityPropertyIDs != null)
                    {
                        for (int i = 0; i < _emissionIntensityPropertyIDs.Length; i++)
                        {
                            if (material.HasProperty(_emissionIntensityPropertyIDs[i]))
                                material.SetFloat(_emissionIntensityPropertyIDs[i], intensity);
                        }
                    }
                }
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
}