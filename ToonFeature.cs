using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class ToonFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class ToonSettings
    {
        public Material material;

        [Tooltip(
            "AfterRenderingOpaques je nejbezpečnější proti prosvítání outline přes transparentní objekty. " +
            "BeforeRenderingPostProcessing použij, pokud chceš toon efekt aplikovat později na větší část obrazu."
        )]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingOpaques;

        [Tooltip("Povolit efekt i ve Scene View.")]
        public bool showInSceneView = true;

        [Tooltip("Přeskočit Preview kamery v editoru.")]
        public bool ignorePreviewCameras = true;
    }

    public ToonSettings settings = new ToonSettings();

    private ToonPass _toonPass;

    private class ToonPass : ScriptableRenderPass
    {
        private Material _material;
        private bool _showInSceneView;
        private bool _ignorePreviewCameras;

        private class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public ToonPass(Material material)
        {
            _material = material;

            // Depth + Normal jsou základ pro čistý outline.
            ConfigureInput(ScriptableRenderPassInput.Depth | ScriptableRenderPassInput.Normal);
        }

        public void Setup(
            Material material,
            bool showInSceneView,
            bool ignorePreviewCameras)
        {
            _material = material;
            _showInSceneView = showInSceneView;
            _ignorePreviewCameras = ignorePreviewCameras;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (_material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            Camera camera = cameraData.camera;

            if (camera == null)
                return;

            if (_ignorePreviewCameras && camera.cameraType == CameraType.Preview)
                return;

            bool isGameCamera = camera.cameraType == CameraType.Game;
            bool isSceneCamera = camera.cameraType == CameraType.SceneView;

            if (!isGameCamera && !(_showInSceneView && isSceneCamera))
                return;

            TextureHandle source = resourceData.activeColorTexture;

            RenderTextureDescriptor descriptor = cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;

            TextureHandle temporaryColor = UniversalRenderer.CreateRenderGraphTexture(
                renderGraph,
                descriptor,
                "_ToonPostProcess_TemporaryColor",
                false
            );

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Toon PostProcess Pass",
                out PassData passData))
            {
                passData.source = source;
                passData.material = _material;

                builder.UseTexture(passData.source);
                builder.SetRenderAttachment(temporaryColor, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1, 1, 0, 0),
                        data.material,
                        0
                    );
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Toon Copy Back Pass",
                out PassData passData))
            {
                passData.source = temporaryColor;
                passData.material = null;

                builder.UseTexture(passData.source);
                builder.SetRenderAttachment(source, 0);
                builder.AllowPassCulling(false);

                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    Blitter.BlitTexture(
                        context.cmd,
                        data.source,
                        new Vector4(1, 1, 0, 0),
                        0,
                        false
                    );
                });
            }
        }
    }

    public override void Create()
    {
        _toonPass = new ToonPass(settings.material)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null)
            return;

        if (_toonPass == null)
            Create();

        _toonPass.renderPassEvent = settings.renderPassEvent;

        _toonPass.Setup(
            settings.material,
            settings.showInSceneView,
            settings.ignorePreviewCameras
        );

        renderer.EnqueuePass(_toonPass);
    }
}