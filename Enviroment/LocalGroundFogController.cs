using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public class LocalGroundFogController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Renderer[] _fogRenderers;

    [Header("Base Look")]
    [SerializeField] private Color _baseColor = new Color(0.45f, 0.55f, 0.70f, 1f);
    [Range(0f, 1f)]
    [SerializeField] private float _baseOpacity = 0.35f;

    [Header("Time Of Day Opacity")]
    [SerializeField] private AnimationCurve _opacityByTime = new AnimationCurve(
        new Keyframe(0.00f, 0.85f),
        new Keyframe(0.20f, 1.00f),
        new Keyframe(0.33f, 0.65f),
        new Keyframe(0.50f, 0.30f),
        new Keyframe(0.72f, 0.75f),
        new Keyframe(0.82f, 0.95f),
        new Keyframe(1.00f, 0.85f)
    );

    [Header("Location Influence")]
    [SerializeField] private bool _useAtmosphereManagerInfluence = true;

    [Tooltip("Kolik přidat opacity, když je hráč uvnitř lokace.")]
    [Range(0f, 1f)]
    [SerializeField] private float _locationOpacityAdd = 0.25f;

    [Tooltip("Jak moc se barva lokace propíše do lokální mlhy.")]
    [Range(0f, 1f)]
    [SerializeField] private float _locationColorInfluence = 0.55f;

    [Header("Material Properties")]
    [SerializeField] private float _noiseScale = 3.0f;
    [SerializeField] private float _secondaryNoiseScale = 8.0f;
    [SerializeField] private float _flowSpeed = 0.35f;
    [SerializeField] private float _edgeFade = 0.18f;
    [SerializeField] private float _depthFadeDistance = 1.5f;

    [Header("Height Fade")]
    [SerializeField] private bool _autoHeightFadeFromTransform = true;
    [SerializeField] private float _heightFadeStartOffset = -0.25f;
    [SerializeField] private float _heightFadeEndOffset = 2.0f;

    [Header("Runtime")]
    [SerializeField] private bool _useMaterialPropertyBlocks = true;

    private MaterialPropertyBlock _block;

    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int OpacityID = Shader.PropertyToID("_Opacity");
    private static readonly int NoiseScaleID = Shader.PropertyToID("_NoiseScale");
    private static readonly int SecondaryNoiseScaleID = Shader.PropertyToID("_SecondaryNoiseScale");
    private static readonly int FlowSpeedID = Shader.PropertyToID("_FlowSpeed");
    private static readonly int EdgeFadeID = Shader.PropertyToID("_EdgeFade");
    private static readonly int DepthFadeDistanceID = Shader.PropertyToID("_DepthFadeDistance");
    private static readonly int HeightFadeStartID = Shader.PropertyToID("_HeightFadeStart");
    private static readonly int HeightFadeEndID = Shader.PropertyToID("_HeightFadeEnd");

    private void Reset()
    {
        _fogRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnEnable()
    {
        if (_fogRenderers == null || _fogRenderers.Length == 0)
            _fogRenderers = GetComponentsInChildren<Renderer>(true);

        if (_block == null)
            _block = new MaterialPropertyBlock();

        Apply();
    }

    private void Update()
    {
        Apply();
    }

    private void Apply()
    {
        if (_fogRenderers == null || _fogRenderers.Length == 0)
            return;

        float timePercent = 0.5f;
        Color dayNightFogColor = _baseColor;

        if (DayNightCycle.Instance != null)
        {
            timePercent = DayNightCycle.Instance.TimePercent;
            dayNightFogColor = DayNightCycle.Instance.CurrentFogColor;
        }

        float opacity = _baseOpacity * Mathf.Clamp01(_opacityByTime.Evaluate(timePercent));
        Color finalColor = Color.Lerp(dayNightFogColor, _baseColor, 0.35f);

        if (_useAtmosphereManagerInfluence && AtmosphereManager.Instance != null)
        {
            LocationProfile profile = AtmosphereManager.Instance.CurrentProfile;
            float influence = AtmosphereManager.Instance.LocationInfluence;

            if (profile != null && influence > 0.001f)
            {
                opacity += _locationOpacityAdd * influence;

                Color locationColor = profile.FogColor;
                finalColor = Color.Lerp(
                    finalColor,
                    locationColor,
                    _locationColorInfluence * influence
                );
            }
        }

        opacity = Mathf.Clamp01(opacity);

        float heightStart = transform.position.y + _heightFadeStartOffset;
        float heightEnd = transform.position.y + _heightFadeEndOffset;

        for (int i = 0; i < _fogRenderers.Length; i++)
        {
            Renderer renderer = _fogRenderers[i];

            if (renderer == null)
                continue;

            if (_useMaterialPropertyBlocks)
            {
                renderer.GetPropertyBlock(_block);

                _block.SetColor(BaseColorID, finalColor);
                _block.SetFloat(OpacityID, opacity);
                _block.SetFloat(NoiseScaleID, _noiseScale);
                _block.SetFloat(SecondaryNoiseScaleID, _secondaryNoiseScale);
                _block.SetFloat(FlowSpeedID, _flowSpeed);
                _block.SetFloat(EdgeFadeID, _edgeFade);
                _block.SetFloat(DepthFadeDistanceID, _depthFadeDistance);

                if (_autoHeightFadeFromTransform)
                {
                    _block.SetFloat(HeightFadeStartID, heightStart);
                    _block.SetFloat(HeightFadeEndID, heightEnd);
                }

                renderer.SetPropertyBlock(_block);
            }
            else
            {
                Material material = renderer.sharedMaterial;

                if (material == null)
                    continue;

                material.SetColor(BaseColorID, finalColor);
                material.SetFloat(OpacityID, opacity);
                material.SetFloat(NoiseScaleID, _noiseScale);
                material.SetFloat(SecondaryNoiseScaleID, _secondaryNoiseScale);
                material.SetFloat(FlowSpeedID, _flowSpeed);
                material.SetFloat(EdgeFadeID, _edgeFade);
                material.SetFloat(DepthFadeDistanceID, _depthFadeDistance);

                if (_autoHeightFadeFromTransform)
                {
                    material.SetFloat(HeightFadeStartID, heightStart);
                    material.SetFloat(HeightFadeEndID, heightEnd);
                }
            }
        }
    }
}