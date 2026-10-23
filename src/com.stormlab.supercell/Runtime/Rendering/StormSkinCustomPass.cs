// HDRP custom pass: ray-marches the active StormSkin for each camera (compute march and temporal resolve into the skin's
// own half-resolution textures), composites it over the camera colour and writes its depth where it is opaque.
// Skins marked drawAlongside are drawn too, farthest first: the farthest supplies the backdrop and the nearer storms are
// composited over it.
//
// Injection point: BeforeTransparent, after HDRP has drawn the sky, applied its fog to opaques and combined its
// volumetric clouds. Those passes treat any pixel without depth as open sky, so a storm drawn earlier gets HDRP's sky
// clouds and fog painted over it. Transparents drawn after the storm (rain, particles) are depth-tested against the
// depth it writes, so the ones behind it are hidden and the ones in front stay in front. The colour pyramid HDRP's
// refractive transparents sample was built before this point and does not contain the storm.
//
// StormSkin registers one of these with HDRP as a global custom pass (StormSkinCustomPass.EnsureInstalled); a scene can
// also hold its own CustomPassVolume with this pass in it, and only one of them draws each camera's frame.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace StormLab.Supercell.Rendering
{
    [System.Serializable]
    public class StormSkinCustomPass : CustomPass
    {
        [Tooltip("stop rays at scene geometry (reads the camera depth buffer)")]
        public bool useSceneDepth = true;
        [Tooltip("write the storm's depth where it is opaque, so transparents and post effects behind it are hidden")]
        public bool writeDepth = true;
        List<StormSkin> drawn;
        /// <summary>how many times any storm pass has run (diagnostics: 0 means HDRP never reached it)</summary>
        public static int Executions { get; private set; }
        /// <summary>how many storms it has composited (0 with Executions above 0: no skin was ready to draw)</summary>
        public static int Composites { get; private set; }

        protected override bool executeInSceneView => true;

        protected override void Execute(CustomPassContext ctx)
        {
            Executions++;
            var hd = ctx.hdCamera; var cam = hd.camera;
            if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
            if (this == global && SceneHasOwnPass()) return;
            drawn ??= new List<StormSkin>();
            drawn.Clear();
            var active = StormSkin.Active;
            if (active != null && active.isActiveAndEnabled && active.ReadyToRender) drawn.Add(active);
            foreach (var s in StormSkin.Enabled)
                if (s != null && s != active && s.drawAlongside && s.isActiveAndEnabled && s.ReadyToRender) drawn.Add(s);
            if (drawn.Count == 0) return;
            if (drawn.Count > 1)
            {
                var eye = cam.transform.position;
                drawn.Sort((a, b) => (b.VolToWorld(Vector3.zero) - eye).sqrMagnitude.CompareTo((a.VolToWorld(Vector3.zero) - eye).sqrMagnitude));
            }
            int w = hd.actualWidth, h = hd.actualHeight;
            for (int i = 0; i < drawn.Count; i++)
            {
                var skin = drawn[i];
                // the march reads the depth buffer as a texture, so nothing may have it bound as a target meanwhile
                CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ClearFlag.None);
                var storm = skin.RecordMarch(ctx.cmd, cam, useSceneDepth ? ctx.cameraDepthBuffer : null, w, h, i == 0, out var dist);
                if (storm == null) continue;
                CoreUtils.SetRenderTarget(ctx.cmd, ctx.cameraColorBuffer, ctx.cameraDepthBuffer, ClearFlag.None);
                skin.RecordComposite(ctx.cmd, cam, storm, dist, w, h, writeDepth);
                Composites++;
            }
        }

        static StormSkinCustomPass global;
        /// <summary>the pass StormSkin registers (null until a skin has been enabled under HDRP)</summary>
        public static StormSkinCustomPass Global => global;
        /// <summary>
        /// Make sure the storm is drawn: register one StormSkinCustomPass with HDRP as a global custom pass at
        /// BeforeTransparent (no GameObject). HDRP clears its global passes and volume lists when scripts reload, which
        /// includes entering Play mode, after objects have been enabled; StormSkin therefore calls this every frame (a
        /// lookup when it is already registered).
        /// </summary>
        public static void EnsureInstalled()
        {
            global ??= new StormSkinCustomPass { name = "Storm skin" };
            CustomPassVolume.RegisterUniqueGlobalCustomPass(CustomPassInjectionPoint.BeforeTransparent, global);
        }

        // A scene may also hold its own volume with this pass in it; then the global one stands down, so the storm is
        // drawn once. (HDRP has already worked out which volumes reach the camera being rendered.)
        static readonly List<CustomPassVolume> volumes = new List<CustomPassVolume>();
        static readonly CustomPassInjectionPoint[] points = (CustomPassInjectionPoint[])System.Enum.GetValues(typeof(CustomPassInjectionPoint));
        static bool SceneHasOwnPass()
        {
            foreach (var p in points)
            {
                CustomPassVolume.GetActivePassVolumes(p, volumes);
                foreach (var v in volumes)
                    foreach (var c in v.customPasses)
                        if (c is StormSkinCustomPass && c.enabled) return true;
            }
            return false;
        }
    }
}
