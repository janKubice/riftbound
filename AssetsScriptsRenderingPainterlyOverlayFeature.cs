using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class PainterlyOverlayFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class PainterlySettings
    {
        public Material material;

        [Tooltip("AfterRenderingPostProcessing znamená finální sjednocení obrazu. Pokud chcete, aby color grading ovlivnil overlay, použijte BeforeRenderingPostProcessing.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;

        public bool showInSceneView = true;
        public bool ignorePreviewCameras = true;
    }

    public PainterlySettings settings = new PainterlySettings();

    private PainterlyPass _pass;

    private class PainterlyPass : ScriptableRenderPass
    {
        private Material _material;
        private bool _showInSceneView;
        private bool _ignorePreviewCameras;

        private class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public PainterlyPass(Material material)
        {
            _material = material;
        }

        public void Setup(Material material, bool showInSceneView, bool ignorePreviewCameras)
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
                "_PainterlyOverlay_TemporaryColor",
                false
            );

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "Painterly Overlay Pass",
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
                "Painterly Overlay Copy Back",
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
        _pass = new PainterlyPass(settings.material)
        {
            renderPassEvent = settings.renderPassEvent
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.material == null)
            return;

        if (_pass == null)
            Create();

        _pass.renderPassEvent = settings.renderPassEvent;

        _pass.Setup(
            settings.material,
            settings.showInSceneView,
            settings.ignorePreviewCameras
        );

        renderer.EnqueuePass(_pass);
    }
}