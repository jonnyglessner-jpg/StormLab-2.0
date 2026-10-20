// The simulated wind for advecting the cloud volume (DESIGN.md 2.5 "the volume is advected"). The brain runs ahead of
// playback, so its live wind is from the future; instead, every 10 sim-seconds the model's C-grid wind is reduced on the
// GPU to 2 km blocks x 1 km layers (u, v, w byte-quantised like Map2D: -51..51 m/s in 0.4 m/s steps, grid-relative),
// read back and kept in system RAM by model time, about 120 KB per snapshot (87 MB for a 2-hour storm at the average
// tier). Playback uploads the two snapshots around its time into two small 3D textures and blends them on the GPU.
//
// Nothing in Runtime/Dynamics changes: Poll() reads run.model.U/V/W between frames. The A buffers hold the
// start-of-step wind until segment 4 of a step, so a capture mid-step is still a consistent, physical wind field.
using System;
using System.Collections.Generic;
using StormLab.Supercell.Dynamics;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rendering
{
    public sealed class WindHistory : IWindSource
    {
        public const float BlockKm = 2f;
        public readonly int NBX, NBY, NL, B;
        /// <summary>domain size, m</summary>
        public readonly float Lx, Ly;
        /// <summary>sim-seconds between snapshots</summary>
        public float interval = 10f;

        readonly SimGrid g;
        readonly ComputeShader cs; readonly int kWind;
        readonly ComputeBuffer outBuf, layerK;
        readonly SortedList<float, byte[]> snaps = new SortedList<float, byte[]>();
        readonly Queue<(float t, AsyncGPUReadbackRequest req)> pending = new Queue<(float, AsyncGPUReadbackRequest)>();
        float lastRequested = -1e9f;

        public Texture3D A { get; private set; }
        public Texture3D Bt { get; private set; }
        /// <summary>blend weight from A to B for the last Upload</summary>
        public float Blend { get; private set; }
        float upA = float.NaN, upB = float.NaN;

        public int Count => snaps.Count;
        float IWindSource.Lx => Lx; float IWindSource.Ly => Ly; int IWindSource.NL => NL;
        public long Bytes => (long)snaps.Count * NBX * NBY * NL * 4;
        public float Latest => snaps.Count > 0 ? snaps.Keys[snaps.Count - 1] : -1f;

        public WindHistory(SimGrid grid, ComputeShader shader)
        {
            g = grid; cs = shader; kWind = cs.FindKernel("Wind");
            B = Mathf.Max(1, Mathf.RoundToInt(BlockKm * 1000f / g.dx));
            NBX = Mathf.CeilToInt(g.NX / (float)B); NBY = Mathf.CeilToInt(g.NY / (float)B);
            NL = Mathf.FloorToInt(g.depth / 1000f) + 1;
            Lx = NBX * B * g.dx; Ly = NBY * B * g.dy;
            var kr = new int[NL * 2];
            for (int L = 0; L < NL; L++)
            {
                float z0 = Mathf.Max(0, (L - 0.5f) * 1000f), z1 = (L + 0.5f) * 1000f;
                int lo = -1, hi = -2;
                for (int k = 0; k < g.NZ; k++) if (g.zc[k] >= z0 && g.zc[k] < z1) { if (lo < 0) lo = k; hi = k; }
                if (lo < 0) { lo = hi = g.KcNearest(L * 1000f); }
                kr[2 * L] = lo; kr[2 * L + 1] = hi;
            }
            layerK = new ComputeBuffer(NL, 8); layerK.SetData(kr);
            outBuf = new ComputeBuffer(NBX * NBY * NL, 4);
            A = NewTex("wind A"); Bt = NewTex("wind B");
            var calm = new byte[NBX * NBY * NL * 4];
            for (int i = 0; i < calm.Length; i += 4) { calm[i] = calm[i + 1] = calm[i + 2] = 128; calm[i + 3] = 255; }
            A.SetPixelData(calm, 0); A.Apply(false); Bt.SetPixelData(calm, 0); Bt.Apply(false);
        }

        Texture3D NewTex(string n) => new Texture3D(NBX, NBY, NL, TextureFormat.RGBA32, false)
        { name = n, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };

        /// <summary>Capture the model's wind if it has advanced a full interval since the last capture. Call once per frame.</summary>
        public void Poll(StormRun run)
        {
            Pump(false);
            float t = run.Time;
            if (t >= lastRequested + interval - 1e-3f) Capture(run, t);
        }

        /// <summary>Queue a reduction of the model's current wind, stamped with model time t (s).</summary>
        public void Capture(StormRun run, float t)
        {
            var m = run.model;
            cs.SetBuffer(kWind, "_MU", m.U); cs.SetBuffer(kWind, "_MV", m.V); cs.SetBuffer(kWind, "_MW", m.W);
            cs.SetBuffer(kWind, "_LayerK", layerK); cs.SetBuffer(kWind, "_WindOut", outBuf);
            cs.SetInts("_WN", g.NX, g.NY, g.NZ, B);
            cs.SetInts("_WB", NBX, NBY, NL, 0);
            cs.Dispatch(kWind, Mathf.CeilToInt(NBX / 8f), Mathf.CeilToInt(NBY / 8f), NL);
            pending.Enqueue((t, AsyncGPUReadback.Request(outBuf)));
            lastRequested = t;
        }

        /// <summary>Collect finished readbacks (wait = block until all are done).</summary>
        public void Pump(bool wait)
        {
            if (wait && pending.Count > 0) AsyncGPUReadback.WaitAllRequests();
            while (pending.Count > 0 && pending.Peek().req.done)
            {
                var (t, req) = pending.Dequeue();
                if (req.hasError) continue;
                snaps[t] = req.GetData<byte>().ToArray();
            }
        }

        /// <summary>Upload the two snapshots around playback time t. Returns false (calm wind) when none are available.</summary>
        public bool Upload(float t)
        {
            if (snaps.Count == 0) return false;
            var keys = snaps.Keys; int hi = 0;
            int lo = 0, h = keys.Count - 1;
            if (t <= keys[0]) { lo = hi = 0; }
            else if (t >= keys[h]) { lo = hi = h; }
            else
            {
                while (h - lo > 1) { int mid = (lo + h) / 2; if (keys[mid] <= t) lo = mid; else h = mid; }
                hi = h;
            }
            float ta = keys[lo], tb = keys[hi];
            if (ta != upA) { A.SetPixelData(snaps[ta], 0); A.Apply(false); upA = ta; }
            if (tb != upB) { Bt.SetPixelData(snaps[tb], 0); Bt.Apply(false); upB = tb; }
            Blend = tb > ta ? Mathf.Clamp01((t - ta) / (tb - ta)) : 0f;
            // a playback far from any snapshot (long gap) gets no wind rather than a stale one
            return Mathf.Min(Mathf.Abs(t - ta), Mathf.Abs(t - tb)) < 4f * interval;
        }

        /// <summary>Decoded wind (grid-relative, m/s) at grid position (m) and height (m), from the snapshot nearest t (tests, tools).</summary>
        public Vector3 Sample(float t, Vector2 gridPos, float z)
        {
            if (snaps.Count == 0) return Vector3.zero;
            float best = snaps.Keys[0]; foreach (var k in snaps.Keys) if (Mathf.Abs(k - t) < Mathf.Abs(best - t)) best = k;
            var d = snaps[best];
            int i = Mathf.Clamp(Mathf.FloorToInt(gridPos.x / (B * g.dx)), 0, NBX - 1), j = Mathf.Clamp(Mathf.FloorToInt(gridPos.y / (B * g.dy)), 0, NBY - 1);
            int L = Mathf.Clamp(Mathf.RoundToInt(z / 1000f), 0, NL - 1);
            int o = ((L * NBY + j) * NBX + i) * 4;
            return new Vector3(-51f + d[o] * 0.4f, -51f + d[o + 1] * 0.4f, -51f + d[o + 2] * 0.4f);
        }

        public void Dispose()
        {
            outBuf?.Release(); layerK?.Release();
            if (A) UnityEngine.Object.DestroyImmediate(A);
            if (Bt) UnityEngine.Object.DestroyImmediate(Bt);
        }
    }
}
