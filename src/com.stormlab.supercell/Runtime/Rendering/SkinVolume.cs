// The storm volume on the GPU (DESIGN.md 2.5, 3): a target volume painted from the parameter block (1.0's BUILD,
// spread over a few frames), the live volume advected by the simulated wind and relaxed toward the target, and the
// light grid (1.0's LIGHT, spread over frames). Average tier: 400 x 400 x 120 at 150 m (60 x 60 x 18 km).
using System;
using StormLab.Supercell.Designer;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rendering
{
    public enum SkinTier { Average, High, Low }

    public sealed class SkinVolume : IDisposable
    {
        public readonly int NV, NZ, LN, LZ;
        /// <summary>sky visibility grid size (half the light grid's)</summary>
        public readonly int SN, SZ;
        /// <summary>half-width and height of the volume, km</summary>
        public readonly float L, ZT;
        public float VoxelKm => 2f * L / NV;

        public RenderTexture Dens => dens[cur];
        public RenderTexture LightTex => light;
        /// <summary>sky visibility on the light grid (zenith cap, mid ring, horizon, horizon toward the key light)</summary>
        public RenderTexture SkyVisTex => skyVis;
        /// <summary>2D, light-grid resolution: how lit the ground is (r key light, g sky), for light bounced onto cloud</summary>
        public RenderTexture GroundTex => ground;
        public RenderTexture Target => target;
        readonly RenderTexture[] dens = new RenderTexture[2];
        readonly RenderTexture target, light, skyVis, ground;
        int cur;
        readonly ComputeShader cs;
        readonly int kBuild, kAdvect, kLight, kSky, kGround;
        int buildZ, lightZ, skyZ, frameIx;
        float pendDt; Vector2 pendShift;
        /// <summary>advect every n-th frame (the time since is carried; a re-centre advects at once)</summary>
        public int advectEvery = 4;
        /// <summary>the sky visibility grid is redone over this many frames (it changes slowly, and costs 19 cone traces a point)</summary>
        public int skySlabs = 8;
        readonly Vector4[] sp = new Vector4[SkinParams.NSLOT];

        /// <summary>extinction at full density, 1/km (1.0's CMAX, PMAX)</summary>
        public float cloudMax = 26f, rainMax = 2.6f;
        /// <summary>rain fall speed relative to the air, m/s</summary>
        public float rainFall = 7f;

        public SkinVolume(SkinTier tier, ComputeShader shader)
        {
            cs = shader;
            kBuild = cs.FindKernel("Build"); kAdvect = cs.FindKernel("Advect"); kLight = cs.FindKernel("Light");
            kSky = cs.FindKernel("SkyVis"); kGround = cs.FindKernel("GroundLight");
            switch (tier)
            {
                case SkinTier.High: NV = 512; NZ = 160; break;
                case SkinTier.Low: NV = 288; NZ = 96; break;
                default: NV = 400; NZ = 120; break;
            }
            L = 30f; ZT = 18f;
            LN = Mathf.Max(8, NV / 3); LZ = Mathf.Max(4, NZ / 3);
            for (int i = 0; i < 2; i++) dens[i] = Vol(NV, NZ, "storm volume " + i);
            target = Vol(NV, NZ, "storm target");
            light = Vol(LN, LZ, "storm light", RenderTextureFormat.ARGBHalf);   // r: sun optical depth
            SN = Mathf.Max(8, LN / 2); SZ = Mathf.Max(4, LZ / 2);
            skyVis = Vol(SN, SZ, "storm sky visibility", RenderTextureFormat.ARGBHalf);   // half the light grid: sky visibility is smooth
            ground = new RenderTexture(LN, LN, 0, RenderTextureFormat.RGHalf, RenderTextureReadWrite.Linear)
            {
                enableRandomWrite = true, useMipMap = false, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
                name = "storm ground light", hideFlags = HideFlags.DontSave
            };
            ground.Create();
        }

        static RenderTexture Vol(int n, int nz, string name, RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            var rt = new RenderTexture(n, n, 0, format, RenderTextureReadWrite.Linear)
            {
                dimension = TextureDimension.Tex3D, volumeDepth = nz, enableRandomWrite = true, useMipMap = false,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = name, hideFlags = HideFlags.DontSave
            };
            rt.Create();
            return rt;
        }

        public long VramBytes => 4L * 3L * NV * NV * NZ + 8L * LN * LN * LZ + 8L * SN * SN * SZ + 4L * LN * LN;

        void Params(CommandBuffer cmd, SkinParams P, int slabZ)
        {
            Array.Copy(P.v, sp, SkinParams.NSLOT);
            sp[(int)Slot.Grid] = new Vector4(NV, NZ, L, ZT);
            sp[(int)Slot.Slab] = new Vector4(slabZ, 0, 0, 0);
            cmd.SetComputeVectorArrayParam(cs, "_SP", sp);
        }

        static Texture3D calm;
        /// <summary>a 1-voxel wind texture with no wind</summary>
        public static Texture3D Calm
        {
            get
            {
                if (calm) return calm;
                calm = new Texture3D(1, 1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                calm.SetPixelData(new byte[] { 128, 128, 128, 255 }, 0); calm.Apply(false);
                return calm;
            }
        }

        static int G(int n, int t) => (n + t - 1) / t;

        /// <summary>Paint the target volume, levels z0 .. z0+nz-1.</summary>
        Texture lastNoise;
        public void Build(CommandBuffer cmd, SkinParams P, Texture noise, int z0, int nz)
        {
            lastNoise = noise;
            Params(cmd, P, z0);
            cmd.SetComputeTextureParam(cs, kBuild, "_Noise", noise);
            cmd.SetComputeTextureParam(cs, kBuild, "_OutD", target);
            cmd.DispatchCompute(cs, kBuild, G(NV, 4), G(NV, 4), G(nz, 4));
        }

        /// <summary>Carry the live volume dt seconds with the wind and relax it toward the target (relax = 1: copy the target).</summary>
        public void Advect(CommandBuffer cmd, SkinParams P, IWindSource wind, bool useWind, Vector2 centreGrid, Vector2 shiftKm, float dt, float relax)
        {
            Params(cmd, P, 0);
            int nxt = 1 - cur;
            cmd.SetComputeTextureParam(cs, kAdvect, "_DensIn", dens[cur]);
            cmd.SetComputeTextureParam(cs, kAdvect, "_Target", target);
            cmd.SetComputeTextureParam(cs, kAdvect, "_OutD", dens[nxt]);
            cmd.SetComputeTextureParam(cs, kAdvect, "_WindA", wind != null ? (Texture)wind.A : Calm);
            cmd.SetComputeTextureParam(cs, kAdvect, "_WindB", wind != null ? (Texture)wind.Bt : Calm);
            cmd.SetComputeVectorParam(cs, "_WindP", wind != null ? new Vector4(wind.Blend, wind.Lx, wind.Ly, wind.NL) : new Vector4(0, 1, 1, 1));
            cmd.SetComputeVectorParam(cs, "_AdvP", new Vector4(dt, relax, shiftKm.x, shiftKm.y));
            cmd.SetComputeVectorParam(cs, "_VolC", new Vector4(centreGrid.x, centreGrid.y, rainFall, useWind && wind != null ? 1 : 0));
            cmd.DispatchCompute(cs, kAdvect, G(NV, 4), G(NV, 4), G(NZ, 4));
            cur = nxt;
        }

        /// <summary>Light levels z0 .. z0+nz-1 of the light grid from the live volume.</summary>
        public void Light(CommandBuffer cmd, Vector3 sunVol, float zbKm, int z0, int nz)
        {
            cmd.SetComputeTextureParam(cs, kLight, "_Dens", dens[cur]);
            if (lastNoise) cmd.SetComputeTextureParam(cs, kLight, "_Noise", lastNoise);   // the far anvil (anvilAt) beyond the volume
            cmd.SetComputeTextureParam(cs, kLight, "_OutL", light);
            cmd.SetComputeVectorParam(cs, "_LSun", sunVol);
            cmd.SetComputeVectorParam(cs, "_LDims", new Vector4(LN, LZ, L, ZT));
            cmd.SetComputeVectorParam(cs, "_LP", new Vector4(cloudMax, rainMax, 0.8f, zbKm));
            cmd.SetComputeVectorParam(cs, "_LSlab", new Vector4(z0, 0, 0, 0));
            cmd.DispatchCompute(cs, kLight, G(LN, 4), G(LN, 4), G(nz, 4));
        }

        /// <summary>Sky visibility levels z0 .. z0+nz-1 from the live volume; keyVol is the sun's or moon's direction (its bearing weights a).</summary>
        public void SkyVis(CommandBuffer cmd, Vector3 keyVol, int z0, int nz)
        {
            cmd.SetComputeTextureParam(cs, kSky, "_Dens", dens[cur]);
            cmd.SetComputeTextureParam(cs, kSky, "_OutS", skyVis);
            cmd.SetComputeVectorParam(cs, "_LSun", keyVol);
            cmd.SetComputeVectorParam(cs, "_LDims", new Vector4(SN, SZ, L, ZT));
            cmd.SetComputeVectorParam(cs, "_LP", new Vector4(cloudMax, rainMax, 0.8f, 0f));
            cmd.SetComputeVectorParam(cs, "_LSlab", new Vector4(z0, 0, 0, 0));
            cmd.DispatchCompute(cs, kSky, G(SN, 4), G(SN, 4), G(nz, 4));
        }

        /// <summary>The whole light grid, sky visibility and ground light at once (the key light switched between sun and moon).</summary>
        public void Relight(CommandBuffer cmd, Vector3 keyVol, float zbKm)
        {
            Light(cmd, keyVol, zbKm, 0, LZ); SkyVis(cmd, keyVol, 0, SZ); GroundLight(cmd);
            lightZ = skyZ = 0;
        }

        /// <summary>The ground light map from the light grid (cheap: every frame).</summary>
        public void GroundLight(CommandBuffer cmd)
        {
            cmd.SetComputeTextureParam(cs, kGround, "_LightIn", light);
            cmd.SetComputeTextureParam(cs, kGround, "_OutG", ground);
            cmd.SetComputeVectorParam(cs, "_LDims", new Vector4(LN, LZ, L, ZT));
            cmd.DispatchCompute(cs, kGround, G(LN, 8), G(LN, 8), 1);
        }

        /// <summary>
        /// One frame of volume work. reset = repaint everything now (first frame, a jump in time, a scrub backwards).
        /// Otherwise 1/buildSlabs of the target and 1/lightSlabs of the light grid are redone, and the whole live volume
        /// is advected by dt.
        /// </summary>
        public void Frame(CommandBuffer cmd, SkinParams P, Texture noise, IWindSource wind, bool useWind, Vector2 centreGrid, Vector2 shiftKm,
                          float dt, float relaxSeconds, Vector3 sunVol, bool reset, int buildSlabs, int lightSlabs)
        {
            if (reset)
            {
                Build(cmd, P, noise, 0, NZ);
                Advect(cmd, P, wind, false, centreGrid, Vector2.zero, 0f, 1f);
                Light(cmd, sunVol, P[Slot.Base].x, 0, LZ);
                SkyVis(cmd, sunVol, 0, SZ);
                GroundLight(cmd);
                buildZ = lightZ = skyZ = 0; pendDt = 0f; pendShift = Vector2.zero;
                return;
            }
            bool recentered = shiftKm != Vector2.zero;
            if (recentered)
            {
                // Every target slice belongs to the old origin. Rebuild them together
                // before relaxing the shifted live volume toward the new target.
                Build(cmd, P, noise, 0, NZ);
                buildZ = 0;
            }
            else
            {
                int bz = Mathf.CeilToInt(NZ / (float)Mathf.Max(1, buildSlabs) / 4f) * 4;
                Build(cmd, P, noise, buildZ, Mathf.Min(bz, NZ - buildZ));
                buildZ += bz; if (buildZ >= NZ) buildZ = 0;
            }
            // advection every advectEvery-th frame with the time gathered since (at once when the volume re-centres):
            // at 150 m voxels a few frames of drift are invisible, and it is the biggest per-frame cost
            pendDt += dt; pendShift += shiftKm; frameIx++;
            if (frameIx % Mathf.Max(1, advectEvery) == 0 || pendShift != Vector2.zero)
            {
                float relax = relaxSeconds > 0 ? 1f - Mathf.Exp(-Mathf.Abs(pendDt) / relaxSeconds) : 1f;
                Advect(cmd, P, wind, useWind, centreGrid, pendShift, pendDt, relax);
                pendDt = 0f; pendShift = Vector2.zero;
            }
            if (recentered)
            {
                Relight(cmd, sunVol, P[Slot.Base].x);
                lightZ = skyZ = 0;
                return;
            }
            int lz = Mathf.CeilToInt(LZ / (float)Mathf.Max(1, lightSlabs) / 4f) * 4;
            Light(cmd, sunVol, P[Slot.Base].x, lightZ, Mathf.Min(lz, LZ - lightZ));
            lightZ += lz; if (lightZ >= LZ) lightZ = 0;
            int sz = Mathf.CeilToInt(SZ / (float)Mathf.Max(1, skySlabs) / 4f) * 4;
            SkyVis(cmd, sunVol, skyZ, Mathf.Min(sz, SZ - skyZ));
            skyZ += sz; if (skyZ >= SZ) skyZ = 0;
            GroundLight(cmd);
        }

        public void Dispose()
        {
            foreach (var r in dens) if (r) { r.Release(); UnityEngine.Object.DestroyImmediate(r); }
            if (target) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            foreach (var r in new[] { light, skyVis, ground }) if (r) { r.Release(); UnityEngine.Object.DestroyImmediate(r); }
        }
    }
}
