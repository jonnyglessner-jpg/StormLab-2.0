// The near-field volume (DESIGN.md 2.5 item 2, 3): a box 9.6 x 9.6 x 2.9 km that follows the low-level mesocyclone,
// 320 x 320 x 96 at 30 m (average tier, 39 MB), 480 x 480 x 144 at 20 m (high tier). It holds the cloud base, the
// stacked plates and the wall cloud, tail cloud, scud, horseshoe lip and inflow tail at full detail
// (Resources/StormSkinNear.compute), plus their own light on a grid of half its resolution. Repainted in slabs over a
// few frames like the storm volume; repainted whole when the box moves.
using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rendering
{
    public sealed class NearVolume : IDisposable
    {
        public readonly int NV, NZ, LN, LZ;
        /// <summary>cell size, half-width and height of the box, km</summary>
        public readonly float Dx, HalfW, H;
        public RenderTexture Field => field;
        public RenderTexture LightTex => light;
        /// <summary>box centre in grid metres, and its bottom above ground in km</summary>
        public Vector2 CentreGrid { get; private set; }
        public float BottomKm { get; private set; }
        public bool Valid { get; private set; }

        /// <summary>how far the low-level meso may drift from the box centre before the box follows it, km</summary>
        public float followKm = 0.6f;
        /// <summary>the box bottom sits this far below the cloud base, km (room for the wall cloud and scud)</summary>
        public float belowBaseKm = 0.85f;

        readonly RenderTexture field, light;
        readonly ComputeShader cs;
        readonly int kBuild, kLight;
        int buildZ, lightZ;

        public NearVolume(SkinTier tier, ComputeShader shader)
        {
            cs = shader;
            kBuild = cs.FindKernel("BuildNear"); kLight = cs.FindKernel("LightNear");
            switch (tier)
            {
                case SkinTier.High: NV = 480; NZ = 144; break;
                case SkinTier.Low: NV = 240; NZ = 72; break;
                default: NV = 320; NZ = 96; break;
            }
            HalfW = 4.8f; Dx = 2f * HalfW / NV; H = NZ * Dx;
            LN = NV / 2; LZ = NZ / 2;
            field = Vol(NV, NZ, "storm near field");
            light = Vol(LN, LZ, "storm near light");
        }

        static RenderTexture Vol(int n, int nz, string name)
        {
            var rt = new RenderTexture(n, n, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = nz, enableRandomWrite = true, useMipMap = false,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name, hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        public long VramBytes => 4L * ((long)NV * NV * NZ + (long)LN * LN * LZ);

        /// <summary>
        /// Place the box for this frame: follows the target (grid m) once it drifts more than followKm, and keeps its
        /// bottom belowBaseKm under the cloud base. Returns true when the box moved (the volume must be repainted whole).
        /// </summary>
        public bool Place(Vector2 targetGrid, float baseKm)
        {
            float bottom = Mathf.Max(0f, baseKm - belowBaseKm);
            bool moved = !Valid || (targetGrid - CentreGrid).magnitude > followKm * 1000f || Mathf.Abs(bottom - BottomKm) > 0.02f;
            if (moved) { CentreGrid = targetGrid; BottomKm = bottom; }
            return moved;
        }

        /// <summary>the Near slot: box centre in the volume frame (km), bottom km, half-width km</summary>
        public Vector4 Slot(Vector2 volumeCentreGrid) { var c = (CentreGrid - volumeCentreGrid) / 1000f; return new Vector4(c.x, c.y, BottomKm, HalfW); }

        static int G(int n, int t) => (n + t - 1) / t;

        public void BindWind(CommandBuffer cmd, ComputeShader shader, int kernel, IWindSource wind, bool useWind, Vector2 volumeCentreGrid)
        {
            bool w = useWind && wind != null;
            cmd.SetComputeTextureParam(shader, kernel, "_WindA", w ? (Texture)wind.A : SkinVolume.Calm);
            cmd.SetComputeTextureParam(shader, kernel, "_WindB", w ? (Texture)wind.Bt : SkinVolume.Calm);
            cmd.SetComputeVectorParam(shader, "_WindP", w ? new Vector4(wind.Blend, wind.Lx, wind.Ly, wind.NL) : new Vector4(0, 1, 1, 1));
            cmd.SetComputeVectorParam(shader, "_VolC", new Vector4(volumeCentreGrid.x, volumeCentreGrid.y, 0, w ? 1 : 0));
        }

        /// <summary>Paint levels z0 .. z0+nz-1 of the near field. sp: the parameter block with Grid and Near filled in.</summary>
        public void Build(CommandBuffer cmd, Vector4[] sp, Texture noise, SkinVolume storm, IWindSource wind, bool useWind, Vector2 volumeCentreGrid, int z0, int nz)
        {
            cmd.SetComputeVectorArrayParam(cs, "_SP", sp);
            cmd.SetComputeVectorParam(cs, "_NDims", new Vector4(NV, NZ, Dx, H));
            cmd.SetComputeVectorParam(cs, "_NSlab", new Vector4(z0, 0, 0, 0));
            cmd.SetComputeTextureParam(cs, kBuild, "_Noise", noise);
            cmd.SetComputeTextureParam(cs, kBuild, "_Dens", storm.Dens);
            cmd.SetComputeTextureParam(cs, kBuild, "_OutN", field);
            BindWind(cmd, cs, kBuild, wind, useWind, volumeCentreGrid);
            cmd.DispatchCompute(cs, kBuild, G(NV, 4), G(NV, 4), G(nz, 4));
            Valid = true;
        }

        /// <summary>Light levels z0 .. z0+nz-1 of the near light grid.</summary>
        public void Light(CommandBuffer cmd, Vector4[] sp, SkinVolume storm, Vector3 sunVol, int z0, int nz)
        {
            cmd.SetComputeVectorArrayParam(cs, "_SP", sp);
            cmd.SetComputeVectorParam(cs, "_NDims", new Vector4(NV, NZ, Dx, H));
            cmd.SetComputeVectorParam(cs, "_NLDims", new Vector4(LN, LZ, 0, 0));
            cmd.SetComputeVectorParam(cs, "_NSlab", new Vector4(z0, 0, 0, 0));
            cmd.SetComputeVectorParam(cs, "_LSun", sunVol);
            cmd.SetComputeVectorParam(cs, "_LP", new Vector4(storm.cloudMax, 0, 0, 0));
            cmd.SetComputeTextureParam(cs, kLight, "_NearIn", field);
            cmd.SetComputeTextureParam(cs, kLight, "_Light", storm.LightTex);
            cmd.SetComputeTextureParam(cs, kLight, "_OutNL", light);
            cmd.DispatchCompute(cs, kLight, G(LN, 4), G(LN, 4), G(nz, 4));
        }

        /// <summary>One frame of near-field work: 1/buildSlabs of the field and 1/lightSlabs of its light, or all of both when full.</summary>
        public void Frame(CommandBuffer cmd, Vector4[] sp, Texture noise, SkinVolume storm, IWindSource wind, bool useWind, Vector2 volumeCentreGrid,
                          Vector3 sunVol, bool full, int buildSlabs, int lightSlabs)
        {
            if (full)
            {
                Build(cmd, sp, noise, storm, wind, useWind, volumeCentreGrid, 0, NZ);
                Light(cmd, sp, storm, sunVol, 0, LZ);
                buildZ = lightZ = 0;
                return;
            }
            int bz = Mathf.CeilToInt(NZ / (float)Mathf.Max(1, buildSlabs) / 4f) * 4;
            Build(cmd, sp, noise, storm, wind, useWind, volumeCentreGrid, buildZ, Mathf.Min(bz, NZ - buildZ));
            buildZ += bz; if (buildZ >= NZ) buildZ = 0;
            int lz = Mathf.CeilToInt(LZ / (float)Mathf.Max(1, lightSlabs) / 4f) * 4;
            Light(cmd, sp, storm, sunVol, lightZ, Mathf.Min(lz, LZ - lightZ));
            lightZ += lz; if (lightZ >= LZ) lightZ = 0;
        }

        public void Invalidate() { Valid = false; }

        public void Dispose()
        {
            if (field) { field.Release(); UnityEngine.Object.DestroyImmediate(field); }
            if (light) { light.Release(); UnityEngine.Object.DestroyImmediate(light); }
        }
    }
}
