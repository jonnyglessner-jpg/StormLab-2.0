// URP renderer feature: ray-marches the active StormSkin for each camera (RenderGraph unsafe pass: compute march and
// temporal resolve into the skin's own half-resolution textures, then a full-screen composite over the camera colour).
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace StormLab.Supercell.Rendering
{
    public class StormSkinFeature : ScriptableRendererFeature
    {
        [Tooltip("after opaques and the skybox, before transparents (the storm hides behind nearby transparent effects)")]
        public RenderPassEvent passEvent = RenderPassEvent.BeforeRenderingTransparents;
        [Tooltip("stop rays at scene geometry (needs the camera depth texture)")]
        public bool useSceneDepth = true;
        StormSkinPass pass;

        public override void Create() { pass = new StormSkinPass(); }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var cam = renderingData.cameraData.camera;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            var skin = StormSkin.Active;
            if (skin == null || !skin.ReadyToRender) return;
            pass.renderPassEvent = passEvent;
            pass.skin = skin; pass.useDepth = useSceneDepth;
            pass.ConfigureInput(useSceneDepth ? ScriptableRenderPassInput.Depth : ScriptableRenderPassInput.None);
            pass.requiresIntermediateTexture = true;
            renderer.EnqueuePass(pass);
        }

        sealed class StormSkinPass : ScriptableRenderPass
        {
            public StormSkin skin; public bool useDepth;
            sealed class PassData { public StormSkin skin; public Camera cam; public TextureHandle color, depth; public bool hasDepth; public int w, h; }

            public StormSkinPass() { profilingSampler = new ProfilingSampler("Storm skin"); }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                var res = frameData.Get<UniversalResourceData>();
                var camData = frameData.Get<UniversalCameraData>();
                using (var builder = renderGraph.AddUnsafePass<PassData>("Storm skin", out var data, profilingSampler))
                {
                    data.skin = skin; data.cam = camData.camera;
                    data.w = camData.cameraTargetDescriptor.width; data.h = camData.cameraTargetDescriptor.height;
                    data.color = res.activeColorTexture;
                    builder.UseTexture(data.color, AccessFlags.ReadWrite);
                    data.hasDepth = useDepth && res.cameraDepthTexture.IsValid();
                    if (data.hasDepth) { data.depth = res.cameraDepthTexture; builder.UseTexture(data.depth, AccessFlags.Read); }
                    builder.AllowPassCulling(false);
                    builder.SetRenderFunc((PassData d, UnsafeGraphContext ctx) =>
                    {
                        var cmd = CommandBufferHelpers.GetNativeCommandBuffer(ctx.cmd);
                        RenderTargetIdentifier? depth = d.hasDepth ? (RenderTargetIdentifier)d.depth : (RenderTargetIdentifier?)null;
                        var storm = d.skin.RecordMarch(cmd, d.cam, depth, d.w, d.h);
                        if (storm == null) return;
                        cmd.SetRenderTarget(d.color);
                        d.skin.RecordComposite(cmd, storm);
                    });
                }
            }
        }
    }
}
