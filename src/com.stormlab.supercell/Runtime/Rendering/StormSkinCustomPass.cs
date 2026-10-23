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
// StormSkin installs one of these in a global CustomPassVolume when it is enabled (StormSkinCustomPass.EnsureInstalled);
// a scene can instead hold its own volume with this pass in it.
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
        readonly List<StormSkin> drawn = new List<StormSkin>();
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

        static GameObject installed;
        /// <summary>
        /// Make sure some global CustomPassVolume draws the storm: if the scene holds none with a StormSkinCustomPass,
        /// add a hidden one (not saved with the scene) at BeforeTransparent.
        /// </summary>
        public static void EnsureInstalled()
        {
            if (installed) return;
            foreach (var v in Object.FindObjectsByType<CustomPassVolume>(FindObjectsInactive.Exclude))
                if (v.isActiveAndEnabled && v.customPasses.Exists(p => p is StormSkinCustomPass)) return;
            installed = new GameObject("Storm skin (HDRP custom pass)") { hideFlags = HideFlags.HideAndDontSave };
            var vol = installed.AddComponent<CustomPassVolume>();
            vol.isGlobal = true;
            vol.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
            vol.customPasses.Add(new StormSkinCustomPass { name = "Storm skin" });
        }
    }
}
