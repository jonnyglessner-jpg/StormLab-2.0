// Recorded hydrometeors for the brain-driven skin. The simulation already transports,
// grows and sediments these fields; do not replace their vertical extent with an LCL cap.
using System;
using StormLab.Supercell.Dynamics;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rendering
{
    public sealed class HydrometeorHistory : IDisposable
    {
        readonly Timeline timeline;
        readonly Snapshot a = new Snapshot(), b = new Snapshot();
        static readonly float[] Decode = MakeDecode();
        static float[] MakeDecode()
        {
            var values = new float[256];
            for (int i = 0; i < values.Length; i++) values[i] = Q3D.Decode((byte)i);
            return values;
        }

        sealed class Snapshot
        {
            public TimelineFrame frame;
            public Texture3D texture;
            public Vector4 origin, size;
            Color[] pixels;

            public bool Upload(TimelineFrame next, Timeline tl)
            {
                if (next == null || !next.valid || next.q3D == null || next.winNX <= 0 || next.winNY <= 0 ||
                    next.q3D.Length != Q3D.Count * tl.NL * next.winNX * next.winNY) return false;
                if (ReferenceEquals(frame, next)) return true;
                int nx = next.winNX, ny = next.winNY, nz = tl.NL;
                int width = nx + 2, height = ny + 2, depth = nz + 2;
                if (!texture || texture.width != width || texture.height != height || texture.depth != depth)
                {
                    Dispose();
                    texture = new Texture3D(width, height, depth, TextureFormat.RGBAHalf, false)
                    {
                        name = "recorded precipitation", filterMode = FilterMode.Bilinear,
                        wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave
                    };
                    pixels = new Color[width * height * depth];
                }
                Array.Clear(pixels, 0, pixels.Length);
                int plane = nx * ny, field = plane * nz;
                for (int z = 0; z < nz; z++)
                    for (int y = 0; y < ny; y++)
                        for (int x = 0; x < nx; x++)
                        {
                            int i = (z * ny + y) * nx + x;
                            var value = new Color(Decode[next.q3D[field + i]], Decode[next.q3D[3 * field + i]],
                                Decode[next.q3D[i]], Decode[next.q3D[2 * field + i]]);
                            int at = ((z + 1) * height + y + 1) * width + x + 1;
                            pixels[at] = value;
                            // Layer zero is centred at the surface. Extend it below the
                            // ground instead of fading surface rain into a padded zero.
                            if (z == 0) pixels[(y + 1) * width + x + 1] = value;
                        }
                texture.SetPixels(pixels); texture.Apply(false);
                float block = tl.blockSize / 1000f;
                origin = new Vector4((next.winI0 - .5f) * block, (next.winJ0 - .5f) * block, -1, block);
                size = new Vector4(width, height, depth, 0);
                frame = next;
                return true;
            }

            public void Dispose()
            {
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
                texture = null; frame = null;
            }
        }

        public HydrometeorHistory(Timeline timeline) { this.timeline = timeline; }

        public static void BindDisabled(CommandBuffer cmd, ComputeShader shader, string kernelName = "Build", bool missingBrainRecord = false)
        {
            int kernel = shader.FindKernel(kernelName);
            cmd.SetComputeFloatParam(shader, "_HydroEnabled", missingBrainRecord ? -1 : 0);
            cmd.SetComputeFloatParam(shader, "_HydroWindEnabled", 0);
            cmd.SetComputeTextureParam(shader, kernel, "_HydroA", SkinVolume.Calm);
            cmd.SetComputeTextureParam(shader, kernel, "_HydroB", SkinVolume.Calm);
            cmd.SetComputeTextureParam(shader, kernel, "_WindA", SkinVolume.Calm);
            cmd.SetComputeTextureParam(shader, kernel, "_WindB", SkinVolume.Calm);
            cmd.SetComputeVectorParam(shader, "_VolC", Vector4.zero);
        }

        public void Bind(CommandBuffer cmd, ComputeShader shader, float time, Vector2 centreGrid, IWindSource wind, float fallSpeed, string kernelName = "Build")
        {
            int index = Mathf.Clamp(Mathf.FloorToInt(time / timeline.interval), timeline.Oldest, timeline.Latest);
            var lo = timeline.Get(index); var hi = timeline.Get(Mathf.Min(index + 1, timeline.Latest));
            if (!a.Upload(lo, timeline) || !b.Upload(hi, timeline)) { BindDisabled(cmd, shader, kernelName, true); return; }
            int kernel = shader.FindKernel(kernelName);
            cmd.SetComputeTextureParam(shader, kernel, "_HydroA", a.texture);
            cmd.SetComputeTextureParam(shader, kernel, "_HydroB", b.texture);
            cmd.SetComputeVectorParam(shader, "_HydroOriginA", a.origin);
            cmd.SetComputeVectorParam(shader, "_HydroOriginB", b.origin);
            cmd.SetComputeVectorParam(shader, "_HydroSizeA", a.size);
            cmd.SetComputeVectorParam(shader, "_HydroSizeB", b.size);
            cmd.SetComputeVectorParam(shader, "_HydroCentre", new Vector4(centreGrid.x / 1000f, centreGrid.y / 1000f, 0, 0));
            cmd.SetComputeFloatParam(shader, "_HydroBlend", hi.t > lo.t ? Mathf.Clamp01((time - lo.t) / (hi.t - lo.t)) : 0);
            cmd.SetComputeVectorParam(shader, "_HydroTimes", new Vector4(Mathf.Max(0, time - lo.t), Mathf.Max(0, hi.t - time), 0, 0));
            cmd.SetComputeTextureParam(shader, kernel, "_WindA", wind != null ? (Texture)wind.A : SkinVolume.Calm);
            cmd.SetComputeTextureParam(shader, kernel, "_WindB", wind != null ? (Texture)wind.Bt : SkinVolume.Calm);
            cmd.SetComputeVectorParam(shader, "_WindP", wind != null ? new Vector4(wind.Blend, wind.Lx, wind.Ly, wind.NL) : new Vector4(0, 1, 1, 1));
            cmd.SetComputeVectorParam(shader, "_VolC", new Vector4(centreGrid.x, centreGrid.y, fallSpeed, wind != null ? 1 : 0));
            cmd.SetComputeFloatParam(shader, "_HydroEnabled", 1);
            cmd.SetComputeFloatParam(shader, "_HydroWindEnabled", wind != null ? 1 : 0);
        }

        public void Dispose() { a.Dispose(); b.Dispose(); }
    }
}
