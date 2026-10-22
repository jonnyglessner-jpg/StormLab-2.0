// GPU driver for the storm-scale dynamics (DESIGN.md 2.2): owns the field buffers, uploads the base state and
// initial conditions, records one time step as a command buffer (same step order as StormLab 1.0's SimGPU:
// advect with the start-of-step wind, microphysics, forcing, nudging, pressure projection) and exposes the
// diagnostics and checkpoint kernels to the timeline recorder.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Dynamics
{
    public enum Layout { Centre, U, V, W }

    public sealed class SupercellModel : IDisposable
    {
        public readonly SimGrid g;
        public readonly BaseState b;
        public readonly SimParams p;
        public readonly ComputeShader cs;

        /// <summary>model time, s (mirrors the GPU clock)</summary>
        public float t { get; private set; }
        public long steps { get; private set; }
        public Nudge nudge = new Nudge();

        // field buffers (A = current state between steps), B = scratch copies for ping-pong
        public ComputeBuffer U, V, W, TH, QV, QC, QR, QI, QS, QH;
        ComputeBuffer uB, vB, wB, thB, qvB, qcB, qrB, qiB, qsB, qhB;
        ComputeBuffer khB, kvB;
        ComputeBuffer tmp, lap, dph, acc, clk, baseBuf, zTab, sx, sy, divB, specA, phi;
        // diagnostics
        public ComputeBuffer col, c3, c2, sum, stat, half;
        public readonly int NCX, NCY, NL, F;

        readonly Dictionary<string, int> kern = new Dictionary<string, int>();
        CommandBuffer[] segCbs;
        bool disposed;

        public const int NC = 20, NF3 = 10, NF2 = 20;
        /// <summary>profiler sample around one time step (the scheduler reads its GPU time)</summary>
        public const string StepSampler = "Supercell.Step";

        public SupercellModel(SimGrid grid, BaseState baseState, SimParams prms, ComputeShader shader = null)
        {
            g = grid; b = baseState; p = prms;
            cs = shader != null ? shader : Resources.Load<ComputeShader>("SupercellDynamics");
            if (cs == null) throw new InvalidOperationException("SupercellDynamics.compute not found in a Resources folder");
            foreach (var name in new[] { "Dep", "Adv1", "Adv2", "Micro", "Sed", "Buoy", "LapH", "DiffRelax", "Drag", "NudgeK", "Div", "XformX", "XformY", "TriZ", "Grad", "Clock", "PackHalf", "UnpackHalf", "ColDiag", "Coarse3D", "Coarse2D", "FrameReduce", "FieldStats", "MicroOnly", "Smag" })
                kern[name] = cs.FindKernel(name);
            if (g.NZ > 128) throw new ArgumentException("NZ must be <= 128 (MAXNZ in the shader)");

            F = Mathf.Max(1, Mathf.RoundToInt(1000f / g.dx));
            NCX = Mathf.CeilToInt(g.NX / (float)F); NCY = Mathf.CeilToInt(g.NY / (float)F); NL = Mathf.FloorToInt(g.depth / 1000f) + 1;   // layers centred on 0, 1, ... km

            Allocate();
            UploadConstants();
            Reset();
        }

        ComputeBuffer Buf(int n, int stride = 4) => new ComputeBuffer(Mathf.Max(n, 1), stride, ComputeBufferType.Structured);

        void Allocate()
        {
            int nC = g.Cells, nU = g.NU, nV = g.NV, nW = g.NW, big = Mathf.Max(Mathf.Max(nU, nV), nW);
            U = Buf(nU); V = Buf(nV); W = Buf(nW);
            TH = Buf(nC); QV = Buf(nC); QC = Buf(nC); QR = Buf(nC); QI = Buf(nC); QS = Buf(nC); QH = Buf(nC);
            uB = Buf(nU); vB = Buf(nV); wB = Buf(nW);
            thB = Buf(nC); qvB = Buf(nC); qcB = Buf(nC); qrB = Buf(nC); qiB = Buf(nC); qsB = Buf(nC); qhB = Buf(nC);
            tmp = Buf(big); lap = Buf(big);
            khB = Buf(nC); kvB = Buf(nC);
            dph = Buf(3 * nC);
            acc = Buf(2 * g.Plane); clk = Buf(4);
            baseBuf = Buf(8 * (g.NZ + 1));
            int nt = g.kOfZc.Length;
            zTab = Buf(g.NZ + (g.NZ + 1) + g.NZ + (g.NZ + 1) + 2 * nt);
            sx = Buf(g.NX * g.NX); sy = Buf(g.NY * g.NY);
            divB = Buf(nC); specA = Buf(nC); phi = Buf(nC);
            col = Buf(g.Plane * NC); c3 = Buf(NCX * NCY * NL * NF3); c2 = Buf(NCX * NCY * NF2); sum = Buf(8 + g.NZ + 1);
            stat = Buf(4); half = Buf((big + 1) / 2);
        }

        // constant block, pushed with every command buffer and before every immediate dispatch, so several models
        // (tests, a seek replay) can share the one ComputeShader asset
        int[] cN, cZO, cZO2, cCG, cBC; Vector4 cD, cK, cS, cM, cFr, cNH, cMX, cSG, cMH; Vector4[] cTW = new Vector4[16];

        void UploadConstants()
        {
            int nt = g.kOfZc.Length;
            cN = new[] { g.NX, g.NY, g.NZ, nt };
            cD = new Vector4(g.dx, g.dy, p.dt, g.depth);
            cK = new Vector4(p.kdiff, p.k1, p.qcCrit, p.k2);
            cS = new Vector4(p.spongeCells, p.spongeTau, p.dampZ, p.dampTau);
            cM = new Vector4(p.cd, b.rho0[0], p.hyper, p.kh);
            cFr = new Vector4(b.cu, b.cv, 0, 0);
            cMX = new Vector4(p.n0rEvap, p.n0rFall, p.microDiag, (p.hailWetGrowth ? 1 : 0) | (p.hailMeltVapour ? 2 : 0));
            cMH = new Vector4(p.n0Hail, p.rhoHail, 0, 0);
            cSG = new Vector4(p.smagCs, 0.1f, 0.15f, p.smagorinsky ? 1 : 0);
            cCG = new[] { NCX, NCY, NL, F };
            cBC = new[] { p.closedLateral ? 1 : 0, 0, 0, 0 };
            // base state
            int BS = g.NZ + 1; var bd = new float[8 * BS];
            for (int k = 0; k < g.NZ; k++)
            {
                bd[0 * BS + k] = b.th0[k]; bd[1 * BS + k] = b.qv0[k]; bd[2 * BS + k] = b.pi0[k]; bd[3 * BS + k] = b.rho0[k];
                bd[4 * BS + k] = b.p0[k]; bd[5 * BS + k] = b.u0[k]; bd[6 * BS + k] = b.v0[k];
            }
            // extend the centre fields one level up so bs(f, NZ) is defined
            for (int f = 0; f < 7; f++) bd[f * BS + g.NZ] = bd[f * BS + g.NZ - 1];
            for (int k = 0; k <= g.NZ; k++) bd[7 * BS + k] = b.rhoW[k];
            baseBuf.SetData(bd);
            // z tables
            var zt = new List<float>();
            zt.AddRange(g.zc);
            int offZw = zt.Count; zt.AddRange(g.zw);
            int offDzc = zt.Count; zt.AddRange(g.dzc);
            int offDzw = zt.Count; zt.AddRange(g.dzw);
            int offKc = zt.Count; zt.AddRange(g.kOfZc);
            int offKw = zt.Count; zt.AddRange(g.kOfZw);
            zTab.SetData(zt.ToArray());
            cZO = new[] { offZw, offDzc, offDzw, offKc };
            cZO2 = new[] { offKw, 0, 0, 0 };
            // DST-II matrices (orthonormal): S[m, i] = c_m sin(pi (m+1)(i+0.5)/n)
            if (p.closedLateral) { sx.SetData(DctMatrix(g.NX)); sy.SetData(DctMatrix(g.NY)); }
            else { sx.SetData(DstMatrix(g.NX)); sy.SetData(DstMatrix(g.NY)); }
            SetNudge(nudge);
        }

        static float[] DstMatrix(int n)
        {
            var s = new float[n * n];
            for (int m = 0; m < n; m++)
            {
                double cm = m == n - 1 ? Math.Sqrt(1.0 / n) : Math.Sqrt(2.0 / n);
                for (int i = 0; i < n; i++) s[m * n + i] = (float)(cm * Math.Sin(Math.PI * (m + 1) * (i + 0.5) / n));
            }
            return s;
        }

        // DCT-II matrices (orthonormal): S[m, i] = c_m cos(pi m (i+0.5)/n), for closed walls (zero normal gradient of phi)
        static float[] DctMatrix(int n)
        {
            var s = new float[n * n];
            for (int m = 0; m < n; m++)
            {
                double cm = m == 0 ? Math.Sqrt(1.0 / n) : Math.Sqrt(2.0 / n);
                for (int i = 0; i < n; i++) s[m * n + i] = (float)(cm * Math.Cos(Math.PI * m * (i + 0.5) / n));
            }
            return s;
        }

        /// <summary>Upload the towers. They switch on and off by the GPU's own clock, so batches of steps stay exact.</summary>
        public void SetNudge(Nudge n)
        {
            nudge = n ?? new Nudge();
            int count = Mathf.Min(nudge.towers.Length, Nudge.MaxTowers);
            var tw = new Vector4[16];
            for (int q = 0; q < count; q++)
            {
                var tt = nudge.towers[q];
                tw[2 * q] = new Vector4(tt.x, tt.y, tt.z, tt.wmax);
                tw[2 * q + 1] = new Vector4(tt.rh, tt.rz, tt.t0, tt.t1);
            }
            cNH = new Vector4(count, Mathf.Min(1f, p.dt / Mathf.Max(nudge.tau, 1e-3f)), 0, 0);
            cTW = tw;
            if (segCbs != null) foreach (var c in segCbs) c.Release();
            segCbs = null;   // re-record with the new constants
        }

        void PushConstants(CommandBuffer cb)
        {
            cb.SetComputeIntParams(cs, "N", cN); cb.SetComputeVectorParam(cs, "D", cD); cb.SetComputeVectorParam(cs, "K", cK);
            cb.SetComputeVectorParam(cs, "S", cS); cb.SetComputeVectorParam(cs, "M", cM); cb.SetComputeVectorParam(cs, "Fr", cFr); cb.SetComputeVectorParam(cs, "MX", cMX); cb.SetComputeVectorParam(cs, "SG", cSG); cb.SetComputeVectorParam(cs, "MH", cMH);
            cb.SetComputeIntParams(cs, "ZO", cZO); cb.SetComputeIntParams(cs, "ZO2", cZO2); cb.SetComputeIntParams(cs, "CG", cCG); cb.SetComputeIntParams(cs, "BC", cBC);
            cb.SetComputeVectorParam(cs, "NH", cNH); cb.SetComputeVectorArrayParam(cs, "TW", cTW);
        }
        void PushConstants()
        {
            cs.SetInts("N", cN); cs.SetVector("D", cD); cs.SetVector("K", cK); cs.SetVector("S", cS); cs.SetVector("M", cM); cs.SetVector("Fr", cFr); cs.SetVector("MX", cMX); cs.SetVector("SG", cSG); cs.SetVector("MH", cMH); cs.SetVector("PRS", Vector4.one);
            cs.SetInts("ZO", cZO); cs.SetInts("ZO2", cZO2); cs.SetInts("CG", cCG); cs.SetInts("BC", cBC); cs.SetVector("NH", cNH); cs.SetVectorArray("TW", cTW);
        }

        // ------------------------------------------------------------ initial state (CPU side, deterministic)
        /// <summary>Fill every field with the base state and zero the clock and accumulations.</summary>
        public void Reset()
        {
            int NX = g.NX, NY = g.NY, NZ = g.NZ;
            var u = new float[g.NU]; var v = new float[g.NV]; var w = new float[g.NW];
            var th = new float[g.Cells]; var qv = new float[g.Cells]; var zero = new float[g.Cells];
            for (int k = 0; k < NZ; k++)
            {
                for (int j = 0; j < NY; j++) for (int i = 0; i <= NX; i++) u[(k * NY + j) * (NX + 1) + i] = b.u0[k];
                for (int j = 0; j <= NY; j++) for (int i = 0; i < NX; i++) v[(k * (NY + 1) + j) * NX + i] = b.v0[k];
                for (int c = k * g.Plane; c < (k + 1) * g.Plane; c++) { th[c] = b.th0[k]; qv[c] = b.qv0[k]; }
            }
            U.SetData(u); V.SetData(v); W.SetData(w); TH.SetData(th); QV.SetData(qv);
            QC.SetData(zero); QR.SetData(zero); QI.SetData(zero); QS.SetData(zero); QH.SetData(zero);
            acc.SetData(new float[2 * g.Plane]); clk.SetData(new float[4]);
            t = 0; steps = 0;
        }

        /// <summary>Apply CPU-side initial perturbations to theta and qv (bubble, noise, outflow boundary) in one upload.</summary>
        public void Initialise(RunSetup setup, Bubble? bubble)
        {
            int NX = g.NX, NY = g.NY, NZ = g.NZ;
            var th = new float[g.Cells]; var qv = new float[g.Cells];
            TH.GetData(th); QV.GetData(qv);
            if (bubble.HasValue) AddBubble(th, qv, bubble.Value);
            if (setup.outflow != null && setup.outflow.enabled) AddOutflow(th, qv, setup.outflow);
            if (setup.noiseAmp > 0) AddNoise(th, setup.noiseAmp, setup.noiseTop, setup.seed);
            TH.SetData(th); QV.SetData(qv);
        }

        void AddBubble(float[] th, float[] qv, Bubble bb)
        {
            for (int k = 0; k < g.NZ; k++)
            {
                float z = g.zc[k];
                for (int j = 0; j < g.NY; j++) for (int i = 0; i < g.NX; i++)
                {
                    float x = (i + 0.5f) * g.dx, y = (j + 0.5f) * g.dy;
                    double r = Math.Sqrt(Sq((x - bb.x) / bb.rh) + Sq((y - bb.y) / bb.rh) + Sq((z - bb.z) / bb.rz));
                    if (r >= 1) continue;
                    int c = (k * g.NY + j) * g.NX + i;
                    double d = bb.dth * Sq(Math.Cos(Math.PI / 2 * r));
                    double T0 = b.th0[k] * b.pi0[k], rh = b.qv0[k] / BaseState.QsatW(T0, b.p0[k]);
                    th[c] = (float)(b.th0[k] + d);
                    double qs = BaseState.QsatW((b.th0[k] + d) * b.pi0[k], b.p0[k]);
                    qv[c] = bb.moist ? (float)Math.Max(b.qv0[k], b.qv0[k] + (Math.Min(b.qv0[0], qs) - b.qv0[k]) * Sq(Math.Cos(Math.PI / 2 * r))) : (float)(rh * qs);
                }
            }
        }

        /// <summary>Outflow boundary: cold pool on the left of a line (see OutflowBoundary), rain-cooled air keeps its qv up to 98 % RH.</summary>
        void AddOutflow(float[] th, float[] qv, OutflowBoundary ob)
        {
            double a = ob.orientationDeg * Math.PI / 180;
            // unit vector along the line (compass: 0 = north) and its left normal
            double lx = Math.Sin(a), ly = Math.Cos(a), nx = -ly, ny = lx;
            for (int k = 0; k < g.NZ; k++)
            {
                float z = g.zc[k];
                if (z >= ob.depth) break;
                double fz = 1 - z / ob.depth;
                for (int j = 0; j < g.NY; j++) for (int i = 0; i < g.NX; i++)
                {
                    double x = (i + 0.5) * g.dx - ob.x, y = (j + 0.5) * g.dy - ob.y;
                    double s = x * nx + y * ny;                       // signed distance, positive on the cold side
                    double fx = Clamp01(s / Math.Max(ob.width, 1) + 0.5);
                    if (fx <= 0) continue;
                    double blend = fx * fx * (3 - 2 * fx);
                    int c = (k * g.NY + j) * g.NX + i;
                    double dth = ob.dTheta * fz * blend;
                    th[c] = (float)(th[c] + dth);
                    double qs = BaseState.QsatW(th[c] * b.pi0[k], b.p0[k]);
                    qv[c] = (float)Math.Min(qv[c], 0.98 * qs);
                }
            }
        }

        /// <summary>Small random theta perturbations below zTop, same generator as 1.0 (mulberry32) so seeds carry over.</summary>
        void AddNoise(float[] th, float amp, float zTop, int seed)
        {
            uint s = (uint)seed;
            Func<double> rnd = () =>
            {
                s = unchecked(s + 0x6D2B79F5u); uint tt = s;
                tt = unchecked((tt ^ (tt >> 15)) * (tt | 1u));
                tt ^= unchecked(tt + ((tt ^ (tt >> 7)) * (tt | 61u)));
                return ((tt ^ (tt >> 14)) & 0xffffffffu) / 4294967296.0;
            };
            for (int k = 0; k < g.NZ && g.zc[k] < zTop; k++)
                for (int c = k * g.Plane; c < (k + 1) * g.Plane; c++) th[c] += (float)(amp * (2 * rnd() - 1));
        }

        static double Sq(double v) => v * v;
        static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        // ------------------------------------------------------------ one time step as a command buffer
        void SetLayout(CommandBuffer cb, Layout L, int baseField)
        {
            int nx, ny, nz; float ox, oy, face, isW;
            switch (L)
            {
                case Layout.U: nx = g.NX + 1; ny = g.NY; nz = g.NZ; ox = 0; oy = 0.5f; face = 0; isW = 0; break;
                case Layout.V: nx = g.NX; ny = g.NY + 1; nz = g.NZ; ox = 0.5f; oy = 0; face = 0; isW = 0; break;
                case Layout.W: nx = g.NX; ny = g.NY; nz = g.NZ + 1; ox = 0.5f; oy = 0.5f; face = 1; isW = 1; break;
                default: nx = g.NX; ny = g.NY; nz = g.NZ; ox = 0.5f; oy = 0.5f; face = 0; isW = 0; break;
            }
            cb.SetComputeIntParams(cs, "LN", nx, ny, nz, baseField + 1);
            cb.SetComputeVectorParam(cs, "LO", new Vector4(ox, oy, face, isW));
            // scalars (cell centres) mix scalarMix times as hard as momentum (DiffRelax)
            cb.SetComputeVectorParam(cs, "PRS", new Vector4(L == Layout.Centre ? p.scalarMix : 1f, 0, 0, 0));
        }

        (int, int, int) LayoutDims(Layout L)
        {
            switch (L)
            {
                case Layout.U: return (g.NX + 1, g.NY, g.NZ);
                case Layout.V: return (g.NX, g.NY + 1, g.NZ);
                case Layout.W: return (g.NX, g.NY, g.NZ + 1);
                default: return (g.NX, g.NY, g.NZ);
            }
        }
        int LayoutCount(Layout L) { var (a, b2, c) = LayoutDims(L); return a * b2 * c; }

        static int Groups(int n, int size) => (n + size - 1) / size;

        void Dispatch3(CommandBuffer cb, int k, int nx, int ny, int nz) => cb.DispatchCompute(cs, k, Groups(nx, 8), Groups(ny, 8), Groups(nz, 4));

        void Advect(CommandBuffer cb, Layout L, ComputeBuffer src, ComputeBuffer dst, bool onTheFly)
        {
            var (nx, ny, nz) = LayoutDims(L);
            SetLayout(cb, L, -1);
            cb.SetComputeIntParams(cs, "XP", 0, onTheFly ? 1 : 0, 0, 0);
            int k1 = kern["Adv1"], k2 = kern["Adv2"];
            cb.SetComputeBufferParam(cs, k1, "U", U); cb.SetComputeBufferParam(cs, k1, "V", V); cb.SetComputeBufferParam(cs, k1, "W", W);
            cb.SetComputeBufferParam(cs, k1, "SRC", src); cb.SetComputeBufferParam(cs, k1, "TMP", tmp); cb.SetComputeBufferParam(cs, k1, "DPHR", dph);
            Dispatch3(cb, k1, nx, ny, nz);
            cb.SetComputeBufferParam(cs, k2, "U", U); cb.SetComputeBufferParam(cs, k2, "V", V); cb.SetComputeBufferParam(cs, k2, "W", W);
            cb.SetComputeBufferParam(cs, k2, "SRC", src); cb.SetComputeBufferParam(cs, k2, "TMPR", tmp); cb.SetComputeBufferParam(cs, k2, "DPHR", dph);
            cb.SetComputeBufferParam(cs, k2, "DST", dst);
            Dispatch3(cb, k2, nx, ny, nz);
        }

        void Diffuse(CommandBuffer cb, Layout L, ComputeBuffer src, ComputeBuffer dst, int baseField)
        {
            var (nx, ny, nz) = LayoutDims(L);
            SetLayout(cb, L, baseField);
            int k1 = kern["LapH"], k2 = kern["DiffRelax"];
            cb.SetComputeBufferParam(cs, k1, "SRC", src); cb.SetComputeBufferParam(cs, k1, "LAP", lap);
            Dispatch3(cb, k1, nx, ny, nz);
            cb.SetComputeBufferParam(cs, k2, "SRC", src); cb.SetComputeBufferParam(cs, k2, "LAPR", lap); cb.SetComputeBufferParam(cs, k2, "DST", dst);
            cb.SetComputeBufferParam(cs, k2, "Base", baseBuf); cb.SetComputeBufferParam(cs, k2, "ZTab", zTab);
            cb.SetComputeBufferParam(cs, k2, "KHR", khB); cb.SetComputeBufferParam(cs, k2, "KVR", kvB);
            Dispatch3(cb, k2, nx, ny, nz);
        }

        void BindCommon(CommandBuffer cb, int k)
        {
            cb.SetComputeBufferParam(cs, k, "Base", baseBuf);
            cb.SetComputeBufferParam(cs, k, "ZTab", zTab);
        }

        /// <summary>A time step is recorded as this many command buffers, so a step that costs more than a frame's GPU budget can be spread over frames.</summary>
        public const int Segments = 6;
        public static readonly string[] SegmentNames = { "advect theta qv qc qr", "advect qi qs qh u", "advect v w", "microphysics, fall, buoyancy", "mixing, sponge, damping", "drag, nudging, pressure" };
        public static string SegmentSampler(int i) => StepSampler + "." + i;
        /// <summary>next segment of the current step (0 = between steps)</summary>
        public int SegPhase { get; private set; }

        CommandBuffer NewSegment(int i)
        {
            var cb = new CommandBuffer { name = "Supercell " + SegmentNames[i] };
            cb.BeginSample(SegmentSampler(i));
            PushConstants(cb);
            foreach (var kv in kern) { cb.SetComputeBufferParam(cs, kv.Value, "Base", baseBuf); cb.SetComputeBufferParam(cs, kv.Value, "ZTab", zTab); }
            return cb;
        }
        void EndSegment(CommandBuffer cb, int i) => cb.EndSample(SegmentSampler(i));

        CommandBuffer[] RecordSegments()
        {
            int NX = g.NX, NY = g.NY, NZ = g.NZ;
            var segs = new CommandBuffer[Segments];
            CommandBuffer cb;

            // 0. departure points for the cell-centre layout (stored), then advect the scalars A -> B
            cb = segs[0] = NewSegment(0);
            int kd = kern["Dep"];
            SetLayout(cb, Layout.Centre, -1);
            cb.SetComputeBufferParam(cs, kd, "U", U); cb.SetComputeBufferParam(cs, kd, "V", V); cb.SetComputeBufferParam(cs, kd, "W", W);
            cb.SetComputeBufferParam(cs, kd, "DPH", dph);
            Dispatch3(cb, kd, NX, NY, NZ);
            Advect(cb, Layout.Centre, TH, thB, false); Advect(cb, Layout.Centre, QV, qvB, false);
            Advect(cb, Layout.Centre, QC, qcB, false); Advect(cb, Layout.Centre, QR, qrB, false);
            EndSegment(cb, 0);
            cb = segs[1] = NewSegment(1);
            Advect(cb, Layout.Centre, QI, qiB, false); Advect(cb, Layout.Centre, QS, qsB, false); Advect(cb, Layout.Centre, QH, qhB, false);
            // velocities (departures on the fly from the start-of-step wind, which stays in A until segment 4)
            Advect(cb, Layout.U, U, uB, true);
            EndSegment(cb, 1);
            cb = segs[2] = NewSegment(2);
            Advect(cb, Layout.V, V, vB, true); Advect(cb, Layout.W, W, wB, true);
            EndSegment(cb, 2);

            // 3. microphysics and sedimentation in place on B, then buoyancy on wB
            cb = segs[3] = NewSegment(3);
            int km = kern["Micro"];
            cb.SetComputeBufferParam(cs, km, "TH", thB); cb.SetComputeBufferParam(cs, km, "QV", qvB); cb.SetComputeBufferParam(cs, km, "QC", qcB);
            cb.SetComputeBufferParam(cs, km, "QR", qrB); cb.SetComputeBufferParam(cs, km, "QI", qiB); cb.SetComputeBufferParam(cs, km, "QS", qsB); cb.SetComputeBufferParam(cs, km, "QH", qhB);
            Dispatch3(cb, km, NX, NY, NZ);
            int ks = kern["Sed"];
            cb.SetComputeBufferParam(cs, ks, "QR", qrB); cb.SetComputeBufferParam(cs, ks, "QS", qsB); cb.SetComputeBufferParam(cs, ks, "QH", qhB); cb.SetComputeBufferParam(cs, ks, "ACC", acc);
            cb.DispatchCompute(cs, ks, Groups(NX, 8), Groups(NY, 8), 1);
            int kb = kern["Buoy"];
            cb.SetComputeBufferParam(cs, kb, "RWW", wB);
            cb.SetComputeBufferParam(cs, kb, "RTH", thB); cb.SetComputeBufferParam(cs, kb, "RQV", qvB); cb.SetComputeBufferParam(cs, kb, "RQC", qcB);
            cb.SetComputeBufferParam(cs, kb, "RQR", qrB); cb.SetComputeBufferParam(cs, kb, "RQI", qiB); cb.SetComputeBufferParam(cs, kb, "RQS", qsB); cb.SetComputeBufferParam(cs, kb, "RQH", qhB);
            Dispatch3(cb, kb, NX, NY, NZ - 1);
            EndSegment(cb, 3);

            // 4. filter, diffusion, sponge and damping B -> A
            cb = segs[4] = NewSegment(4);
            int ksg = kern["Smag"];
            cb.SetComputeBufferParam(cs, ksg, "U", uB); cb.SetComputeBufferParam(cs, ksg, "V", vB); cb.SetComputeBufferParam(cs, ksg, "W", wB);
            cb.SetComputeBufferParam(cs, ksg, "RTH", thB); cb.SetComputeBufferParam(cs, ksg, "RQV", qvB); cb.SetComputeBufferParam(cs, ksg, "RQC", qcB); cb.SetComputeBufferParam(cs, ksg, "RQI", qiB);
            cb.SetComputeBufferParam(cs, ksg, "KHB", khB); cb.SetComputeBufferParam(cs, ksg, "KVB", kvB);
            Dispatch3(cb, ksg, NX, NY, NZ);
            Diffuse(cb, Layout.U, uB, U, 5); Diffuse(cb, Layout.V, vB, V, 6); Diffuse(cb, Layout.W, wB, W, -1);
            Diffuse(cb, Layout.Centre, thB, TH, 0); Diffuse(cb, Layout.Centre, qvB, QV, 1);
            Diffuse(cb, Layout.Centre, qcB, QC, -1); Diffuse(cb, Layout.Centre, qrB, QR, -1); Diffuse(cb, Layout.Centre, qiB, QI, -1);
            Diffuse(cb, Layout.Centre, qsB, QS, -1); Diffuse(cb, Layout.Centre, qhB, QH, -1);
            EndSegment(cb, 4);

            // 5. surface drag and updraft nudging on A, pressure projection, clock
            cb = segs[5] = NewSegment(5);
            if (p.cd > 0)
            {
                int kdr = kern["Drag"];
                cb.SetComputeIntParams(cs, "GC", 0, 0, 0, 0);
                cb.SetComputeBufferParam(cs, kdr, "RWU", U); cb.SetComputeBufferParam(cs, kdr, "V", V); cb.SetComputeBufferParam(cs, kdr, "U", uB); cb.SetComputeBufferParam(cs, kdr, "RWV", vB);
                cb.DispatchCompute(cs, kdr, Groups(NX + 1, 8), Groups(NY, 8), 1);
                cb.SetComputeIntParams(cs, "GC", 1, 0, 0, 0);
                cb.SetComputeBufferParam(cs, kdr, "RWV", V); cb.SetComputeBufferParam(cs, kdr, "U", U); cb.SetComputeBufferParam(cs, kdr, "V", vB); cb.SetComputeBufferParam(cs, kdr, "RWU", uB);
                cb.DispatchCompute(cs, kdr, Groups(NX, 8), Groups(NY + 1, 8), 1);
            }
            if (nudge.towers.Length > 0)
            {
                int kn = kern["NudgeK"];
                cb.SetComputeBufferParam(cs, kn, "RWW", W); cb.SetComputeBufferParam(cs, kn, "RCLK", clk);
                Dispatch3(cb, kn, NX, NY, NZ - 1);
            }
            // divergence -> sine (open) or cosine (closed) transform in x, y -> tridiagonal in z -> inverse -> gradient
            int kdv = kern["Div"];
            cb.SetComputeBufferParam(cs, kdv, "U", U); cb.SetComputeBufferParam(cs, kdv, "V", V); cb.SetComputeBufferParam(cs, kdv, "W", W); cb.SetComputeBufferParam(cs, kdv, "XDST", divB);
            Dispatch3(cb, kdv, NX, NY, NZ);
            int kx = kern["XformX"], ky = kern["XformY"];
            cb.SetComputeBufferParam(cs, kx, "SX", sx); cb.SetComputeBufferParam(cs, ky, "SY", sy);
            cb.SetComputeIntParams(cs, "XP", 1, 0, 0, 0);
            cb.SetComputeBufferParam(cs, kx, "XSRC", divB); cb.SetComputeBufferParam(cs, kx, "XDST", specA);
            cb.DispatchCompute(cs, kx, Groups(NX, 16), Groups(NY, 16), NZ);
            cb.SetComputeBufferParam(cs, ky, "XSRC", specA); cb.SetComputeBufferParam(cs, ky, "XDST", phi);
            cb.DispatchCompute(cs, ky, Groups(NX, 16), Groups(NY, 16), NZ);
            int kt = kern["TriZ"];
            cb.SetComputeBufferParam(cs, kt, "PHIRW", phi);
            cb.DispatchCompute(cs, kt, Groups(NX, 4), Groups(NY, 4), 1);
            cb.SetComputeIntParams(cs, "XP", 0, 0, 0, 0);
            cb.SetComputeBufferParam(cs, ky, "XSRC", phi); cb.SetComputeBufferParam(cs, ky, "XDST", specA);
            cb.DispatchCompute(cs, ky, Groups(NX, 16), Groups(NY, 16), NZ);
            cb.SetComputeBufferParam(cs, kx, "XSRC", specA); cb.SetComputeBufferParam(cs, kx, "XDST", phi);
            cb.DispatchCompute(cs, kx, Groups(NX, 16), Groups(NY, 16), NZ);
            int kg = kern["Grad"];
            cb.SetComputeBufferParam(cs, kg, "PHI", phi);
            cb.SetComputeBufferParam(cs, kg, "RWU", U); cb.SetComputeBufferParam(cs, kg, "RWV", V); cb.SetComputeBufferParam(cs, kg, "RWW", W);
            cb.SetComputeIntParams(cs, "GC", 0, 0, 0, 0); Dispatch3(cb, kg, NX + 1, NY, NZ);
            cb.SetComputeIntParams(cs, "GC", 1, 0, 0, 0); Dispatch3(cb, kg, NX, NY + 1, NZ);
            cb.SetComputeIntParams(cs, "GC", 2, 0, 0, 0); Dispatch3(cb, kg, NX, NY, NZ);
            int kc = kern["Clock"];
            cb.SetComputeBufferParam(cs, kc, "CLK", clk);
            cb.DispatchCompute(cs, kc, 1, 1, 1);
            EndSegment(cb, 5);
            return segs;
        }

        /// <summary>Advance n whole time steps (queued on the GPU; returns at once). Finishes a partly run step first.</summary>
        public void Step(int n = 1)
        {
            if (disposed) return;
            if (segCbs == null) segCbs = RecordSegments();
            if (n > 0 && SegPhase != 0) { while (!StepSegment()) { } n--; }
            for (int i = 0; i < n; i++) foreach (var cb in segCbs) Graphics.ExecuteCommandBuffer(cb);
            t += n * p.dt; steps += n;
        }

        /// <summary>Queue the next segment of the current step. Returns true when that completed a step.</summary>
        public bool StepSegment()
        {
            if (disposed) return false;
            if (segCbs == null) segCbs = RecordSegments();
            Graphics.ExecuteCommandBuffer(segCbs[SegPhase]);
            SegPhase++;
            if (SegPhase < Segments) return false;
            SegPhase = 0; t += p.dt; steps++;
            return true;
        }

        /// <summary>Block until the GPU has finished everything queued so far (a tiny readback).</summary>
        public void Sync()
        {
            var tmp4 = new float[4];
            clk.GetData(tmp4);
            t = tmp4[0];
        }

        // ------------------------------------------------------------ diagnostics
        /// <summary>Run the per-column, 1 km block and frame-reduction kernels. Results in col, c3, c2, sum.</summary>
        public void RunDiagnostics()
        {
            int NX = g.NX, NY = g.NY;
            PushConstants();
            int kc = kern["ColDiag"];
            cs.SetBuffer(kc, "Base", baseBuf); cs.SetBuffer(kc, "ZTab", zTab);
            cs.SetBuffer(kc, "U", U); cs.SetBuffer(kc, "V", V); cs.SetBuffer(kc, "W", W);
            cs.SetBuffer(kc, "RTH", TH); cs.SetBuffer(kc, "RQV", QV); cs.SetBuffer(kc, "RQC", QC); cs.SetBuffer(kc, "RQR", QR); cs.SetBuffer(kc, "RQI", QI); cs.SetBuffer(kc, "RQS", QS); cs.SetBuffer(kc, "RQH", QH);
            cs.SetBuffer(kc, "RACC", acc); cs.SetBuffer(kc, "COL", col);
            cs.Dispatch(kc, Groups(NX, 8), Groups(NY, 8), 1);
            int k3 = kern["Coarse3D"];
            cs.SetBuffer(k3, "Base", baseBuf); cs.SetBuffer(k3, "ZTab", zTab);
            cs.SetBuffer(k3, "U", U); cs.SetBuffer(k3, "V", V); cs.SetBuffer(k3, "W", W);
            cs.SetBuffer(k3, "RTH", TH); cs.SetBuffer(k3, "RQC", QC); cs.SetBuffer(k3, "RQR", QR); cs.SetBuffer(k3, "RQI", QI); cs.SetBuffer(k3, "RQS", QS); cs.SetBuffer(k3, "RQH", QH);
            cs.SetBuffer(k3, "C3", c3);
            cs.Dispatch(k3, Groups(NCX, 8), Groups(NCY, 8), Groups(NL, 4));
            int k2 = kern["Coarse2D"];
            cs.SetBuffer(k2, "RCOL", col); cs.SetBuffer(k2, "C2", c2);
            cs.Dispatch(k2, Groups(NCX, 8), Groups(NCY, 8), 1);
            int kf = kern["FrameReduce"];
            cs.SetBuffer(kf, "RC2", c2); cs.SetBuffer(kf, "RCOL", col); cs.SetBuffer(kf, "W", W); cs.SetBuffer(kf, "SUM", sum);
            cs.Dispatch(kf, 1, 1, 1);
        }

        /// <summary>max |v|, max, min and NaN count of a field (synchronous; for tests).</summary>
        public (float maxAbs, float max, float min, int nan) FieldStats(ComputeBuffer f, Layout L)
        {
            int k = kern["FieldStats"];
            PushConstants();
            stat.SetData(new uint[] { 0, 0, 0xffffffffu, 0 });
            var (nx, ny, nz) = LayoutDims(L);
            cs.SetInts("LN", nx, ny, nz, 0);
            cs.SetBuffer(k, "SRC", f); cs.SetBuffer(k, "STAT", stat);
            cs.Dispatch(k, Groups(nx * ny * nz, 256), 1, 1);
            var o = new uint[4]; stat.GetData(o);
            return (FromOrdered(o[0]), FromOrdered(o[1]), FromOrdered(o[2]), (int)o[3]);
        }
        static float FromOrdered(uint b)
        {
            uint bits = (b & 0x80000000u) != 0 ? (b & 0x7fffffffu) : ~b;
            return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
        }

        /// <summary>Download a field (synchronous).</summary>
        public float[] Download(ComputeBuffer f, Layout L) { var a = new float[LayoutCount(L)]; f.GetData(a); return a; }
        public float[] DownloadAcc() { var a = new float[2 * g.Plane]; acc.GetData(a); return a; }
        /// <summary>surface rain [0, plane) and hail [plane, 2 plane) accumulations, mm</summary>
        public ComputeBuffer Accum => acc;
        /// <summary>pressure perturbation p'/rho0 of the last projection, m^2/s^2</summary>
        public ComputeBuffer Phi => phi;
        public void UploadAcc(float[] a) => acc.SetData(a);

        public ComputeBuffer FieldBuffer(int index)
        {
            switch (index) { case 0: return U; case 1: return V; case 2: return W; case 3: return TH; case 4: return QV; case 5: return QC; case 6: return QR; case 7: return QI; case 8: return QS; default: return QH; }
        }
        public static Layout FieldLayout(int index) => index == 0 ? Layout.U : index == 1 ? Layout.V : index == 2 ? Layout.W : Layout.Centre;
        public static int FieldBase(int index) => index == 0 ? 5 : index == 1 ? 6 : index == 3 ? 0 : index == 4 ? 1 : -1;
        public static readonly string[] FieldNames = { "u", "v", "w", "theta", "qv", "qc", "qr", "qi", "qs", "qh" };

        // ------------------------------------------------------------ checkpoints
        ComputeBuffer[] ckHalf;

        /// <summary>
        /// Quantise the live state to fp16 perturbations from the base state, in place, and queue an async readback of
        /// each field. Because the live run continues from exactly the quantised state, a seek that restores this
        /// checkpoint and re-runs reproduces the original run bit for bit (on the same GPU and driver).
        /// Returns one request per field (u, v, w, theta, qv, qc, qr, qi, qs, qh).
        /// </summary>
        public AsyncGPUReadbackRequest[] QuantiseAndCapture(Action<int, AsyncGPUReadbackRequest> onField = null)
        {
            PushConstants();
            if (ckHalf == null)
            {
                ckHalf = new ComputeBuffer[10];
                for (int f = 0; f < 10; f++) ckHalf[f] = Buf((LayoutCount(FieldLayout(f)) + 1) / 2);
            }
            var req = new AsyncGPUReadbackRequest[10];
            int kp = kern["PackHalf"], ku = kern["UnpackHalf"];
            for (int f = 0; f < 10; f++)
            {
                var L = FieldLayout(f); var (nx, ny, nz) = LayoutDims(L); int n = (nx * ny * nz + 1) / 2;
                cs.SetInts("LN", nx, ny, nz, FieldBase(f) + 1);
                cs.SetBuffer(kp, "Base", baseBuf); cs.SetBuffer(kp, "SRC", FieldBuffer(f)); cs.SetBuffer(kp, "HALF", ckHalf[f]);
                cs.Dispatch(kp, Groups(n, 256), 1, 1);
                cs.SetBuffer(ku, "Base", baseBuf); cs.SetBuffer(ku, "RHALF", ckHalf[f]); cs.SetBuffer(ku, "DST", FieldBuffer(f));
                cs.Dispatch(ku, Groups(n, 256), 1, 1);
                int field = f;
                req[f] = onField == null ? AsyncGPUReadback.Request(ckHalf[f])
                    : AsyncGPUReadback.Request(ckHalf[f], r => onField(field, r));
            }
            return req;
        }

        /// <summary>Restore one field from fp16 perturbation data (as captured by QuantiseAndCapture).</summary>
        public void RestoreField(int f, uint[] data)
        {
            PushConstants();
            int ku = kern["UnpackHalf"]; var L = FieldLayout(f); var (nx, ny, nz) = LayoutDims(L);
            half.SetData(data, 0, 0, data.Length);
            cs.SetInts("LN", nx, ny, nz, FieldBase(f) + 1);
            cs.SetBuffer(ku, "Base", baseBuf); cs.SetBuffer(ku, "RHALF", half); cs.SetBuffer(ku, "DST", FieldBuffer(f));
            cs.Dispatch(ku, Groups((nx * ny * nz + 1) / 2, 256), 1, 1);
        }

        /// <summary>Set the model clock (after restoring a checkpoint).</summary>
        public void SetTime(float time, long stepCount)
        {
            clk.SetData(new[] { time, 0f, 0f, 0f }); t = time; steps = stepCount; SegPhase = 0;
        }

        /// <summary>Upload a whole field (tests).</summary>
        public void Upload(ComputeBuffer f, float[] data) => f.SetData(data);

        /// <summary>Microphysics alone on the current fields, n times (conservation test).</summary>
        public void MicroOnly(int n)
        {
            PushConstants();
            int k = kern["MicroOnly"];
            cs.SetBuffer(k, "Base", baseBuf);
            cs.SetBuffer(k, "TH", TH); cs.SetBuffer(k, "QV", QV); cs.SetBuffer(k, "QC", QC); cs.SetBuffer(k, "QR", QR);
            cs.SetBuffer(k, "QI", QI); cs.SetBuffer(k, "QS", QS); cs.SetBuffer(k, "QH", QH);
            for (int i = 0; i < n; i++) cs.Dispatch(k, Groups(g.NX, 8), Groups(g.NY, 8), Groups(g.NZ, 4));
        }

        public void Dispose()
        {
            if (disposed) return; disposed = true;
            foreach (var bf in AllBuffers()) bf?.Release();
            if (segCbs != null) foreach (var c in segCbs) c.Release();
        }

        IEnumerable<ComputeBuffer> AllBuffers()
        {
            foreach (var bf in new[] { U, V, W, TH, QV, QC, QR, QI, QS, QH, uB, vB, wB, thB, qvB, qcB, qrB, qiB, qsB, qhB, tmp, lap, dph, acc, clk, baseBuf, zTab, sx, sy, divB, specA, phi, col, c3, c2, sum, stat, half, khB, kvB })
                yield return bf;
            if (ckHalf != null) foreach (var bf in ckHalf) yield return bf;
        }

        /// <summary>VRAM held by this model, bytes.</summary>
        public long VramBytes
        {
            get
            {
                long n = 0;
                foreach (var bf in AllBuffers())
                    if (bf != null) n += (long)bf.count * bf.stride;
                return n;
            }
        }
    }
}
