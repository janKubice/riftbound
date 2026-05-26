using UnityEngine;
using UnityEngine.Rendering;

public class LaserBeamVFX : MonoBehaviour
{
    [Header("Core Beam")]
    [SerializeField] private Transform _coreRoot;
    [SerializeField] private Renderer _coreRenderer;

    [Header("Glow Beam")]
    [SerializeField] private Transform _glowRoot;
    [SerializeField] private Renderer _glowRenderer;

    [Header("Width")]
    [SerializeField] private float _coreWidth = 0.16f;
    [SerializeField] private float _glowWidth = 0.55f;

    [Header("Texture")]
    [Tooltip("Opakování energie/textury na metr délky.")]
    [SerializeField] private float _textureTilingPerMeter = 0.45f;

    [Header("Runtime Intensity")]
    [SerializeField] private float _coreOpacity = 1.15f;
    [SerializeField] private float _glowOpacity = 0.55f;

    [Header("Options")]
    [SerializeField] private bool _disableShadows = true;
    [SerializeField] private bool _hideOnAwake = true;

    private MaterialPropertyBlock _coreBlock;
    private MaterialPropertyBlock _glowBlock;

    private static readonly int TextureTilingID = Shader.PropertyToID("_TextureTiling");
    private static readonly int BeamLengthID = Shader.PropertyToID("_BeamLength");
    private static readonly int OpacityID = Shader.PropertyToID("_Opacity");

    private void Awake()
    {
        _coreBlock = new MaterialPropertyBlock();
        _glowBlock = new MaterialPropertyBlock();

        ConfigureRenderer(_coreRenderer);
        ConfigureRenderer(_glowRenderer);

        if (_hideOnAwake)
            StopBeam();
    }

    private void Reset()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

        if (renderers.Length > 0)
        {
            _coreRenderer = renderers[0];
            _coreRoot = _coreRenderer.transform;
        }

        if (renderers.Length > 1)
        {
            _glowRenderer = renderers[1];
            _glowRoot = _glowRenderer.transform;
        }
    }

    private void ConfigureRenderer(Renderer renderer)
    {
        if (renderer == null || !_disableShadows)
            return;

        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    public void UpdateBeam(Vector3 start, Vector3 end, Vector3 hitNormal = default)
    {
        float distance = Vector3.Distance(start, end);

        if (distance <= 0.001f)
        {
            StopBeam();
            return;
        }

        UpdateBeamPart(
            _coreRoot,
            _coreRenderer,
            _coreBlock,
            start,
            end,
            distance,
            _coreWidth,
            _coreOpacity
        );

        UpdateBeamPart(
            _glowRoot,
            _glowRenderer,
            _glowBlock,
            start,
            end,
            distance,
            _glowWidth,
            _glowOpacity
        );
    }

    private void UpdateBeamPart(
        Transform root,
        Renderer renderer,
        MaterialPropertyBlock block,
        Vector3 start,
        Vector3 end,
        float distance,
        float width,
        float opacity)
    {
        if (root == null || renderer == null)
            return;

        if (!root.gameObject.activeSelf)
            root.gameObject.SetActive(true);

        Vector3 direction = end - start;

        root.position = (start + end) * 0.5f;

        // Unity cylinder má výšku v ose Y.
        root.up = direction.normalized;

        // Unity cylinder má default výšku 2, proto distance * 0.5.
        root.localScale = new Vector3(
            width,
            distance * 0.5f,
            width
        );

        float tiling = Mathf.Max(0.01f, distance * _textureTilingPerMeter);

        renderer.GetPropertyBlock(block);
        block.SetFloat(TextureTilingID, tiling);
        block.SetFloat(BeamLengthID, distance);
        block.SetFloat(OpacityID, opacity);
        renderer.SetPropertyBlock(block);
    }

    public void StopBeam()
    {
        if (_coreRoot != null)
            _coreRoot.gameObject.SetActive(false);

        if (_glowRoot != null)
            _glowRoot.gameObject.SetActive(false);
    }
}