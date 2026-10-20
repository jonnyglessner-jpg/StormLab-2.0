// Timeline storage (TIMELINE.md): a ring buffer of compact feature frames every 10 sim-seconds, fp16 full-grid
// checkpoints every 15 sim-minutes, and the CPU feature extraction that turns the GPU's 1 km reductions into a frame.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    public sealed class Timeline
    {
        public readonly SimGrid g;
        /// <summary>sim-seconds between frames</summary>
        public readonly float interval;
        /// <summary>1 km map size (blocks), number of 1 km layers, block size (m)</summary>
        public readonly int NCX, NCY, NL;
        public readonly float blockSize;
        /// <summary>heights of the w levels the wColumn arrays use, m</summary>
        public readonly float[] zw;
        public readonly Vector2 frameMotion;
        public readonly int capacity;
        readonly TimelineFrame[] ring;

        /// <summary>highest frame index stored (-1 when empty)</summary>
        public int Latest { get; private set; } = -1;
        public event Action<TimelineFrame> FrameAdded;

        public Timeline(SimGrid g, int ncx, int ncy, int nl, float blockSize, Vector2 frameMotion, int capacity, float interval = 10f)
        {
            this.g = g; NCX = ncx; NCY = ncy; NL = nl; this.blockSize = blockSize; this.frameMotion = frameMotion;
            this.capacity = Math.Max(2, capacity); this.interval = interval;
            ring = new TimelineFrame[this.capacity];
            zw = (float[])g.zw.Clone();
        }

        /// <summary>the frame with this index, or null if it has not been computed or has been evicted</summary>
        public TimelineFrame Get(int index)
        {
            if (index < 0 || index > Latest) return null;
            var f = ring[index % capacity];
            return f != null && f.index == index ? f : null;
        }
        /// <summary>the frame at or before model time t</summary>
        public TimelineFrame AtTime(float t) => Get(Mathf.FloorToInt(t / interval + 1e-4f));
        /// <summary>the two frames around t and the blend weight between them (for smooth playback)</summary>
        public bool Bracket(float t, out TimelineFrame a, out TimelineFrame b, out float w)
        {
            int i = Mathf.FloorToInt(t / interval + 1e-4f);
            a = Get(i); b = Get(i + 1) ?? a; w = a == null ? 0 : Mathf.Clamp01((t - a.t) / interval);
            return a != null;
        }
        /// <summary>oldest frame index still held</summary>
        public int Oldest => Latest < 0 ? -1 : Math.Max(0, Latest - capacity + 1);
        /// <summary>model time of the newest frame, s (the computed horizon)</summary>
        public float Horizon => Latest < 0 ? 0f : Latest * interval;

        public void Store(TimelineFrame f)
        {
            ring[f.index % capacity] = f;
            if (f.index > Latest) Latest = f.index;
            FrameAdded?.Invoke(f);
        }

        public long Bytes
        {
            get { long n = 0; foreach (var f in ring) if (f != null) n += f.Bytes; return n; }
        }
    }

    public sealed class Checkpoint
    {
        public float t; public long step; public int frameIndex;
        /// <summary>fp16 pairs (two halves per uint) of the perturbation from the base state, per field u, v, w, theta, qv, qc, qr, qi, qs, qh</summary>
        public uint[][] fields;
        /// <summary>surface rain and hail accumulations (fp32, 2 x NX x NY)</summary>
        public float[] acc;
        public long Bytes { get { long n = acc != null ? acc.Length * 4L : 0; if (fields != null) foreach (var f in fields) if (f != null) n += f.Length * 4L; return n; } }
    }

    public sealed class CheckpointStore
    {
        readonly List<Checkpoint> list = new List<Checkpoint>();
        public IReadOnlyList<Checkpoint> All => list;
        public bool Has(long step) { foreach (var c in list) if (c.step == step) return true; return false; }
        public void Add(Checkpoint c) { if (!Has(c.step)) { list.Add(c); list.Sort((a, b) => a.step.CompareTo(b.step)); } }
        public Checkpoint LatestAtOrBefore(long step)
        {
            Checkpoint best = null;
            foreach (var c in list) if (c.step <= step && (best == null || c.step > best.step)) best = c;
            return best;
        }
        public long Bytes { get { long n = 0; foreach (var c in list) n += c.Bytes; return n; } }
    }

    /// <summary>Builds a TimelineFrame from the GPU's per-1 km-column (C2), per-1 km-block (C3) and core-column (SUM) reductions.</summary>
    public sealed class FeatureExtractor
    {
        readonly SimGrid g; readonly int NCX, NCY, NL, F; readonly float bx;
        readonly Vector2 frameMotion, shearDir;
        /// <summary>width of the storm-centred 3D window, blocks (km)</summary>
        public int windowBlocks = 40;
        const int NF2 = SupercellModel.NF2, NF3 = SupercellModel.NF3;

        public FeatureExtractor(SimGrid g, int ncx, int ncy, int nl, int f, Vector2 frameMotion, Vector2 shear0to6)
        {
            this.g = g; NCX = ncx; NCY = ncy; NL = nl; F = f; bx = f * g.dx; this.frameMotion = frameMotion;
            shearDir = shear0to6.sqrMagnitude > 1e-6f ? shear0to6.normalized : Vector2.right;
        }

        /// <summary>centre of 1 km block (i, j), m (the last block may be partial)</summary>
        Vector2 BlockPos(int i, int j) => new Vector2(0.5f * (i * F + Mathf.Min(i * F + F, g.NX)) * g.dx, 0.5f * (j * F + Mathf.Min(j * F + F, g.NY)) * g.dy);
        int BI(float x) => Mathf.Clamp(Mathf.FloorToInt(x / bx), 0, NCX - 1);
        int BJ(float y) => Mathf.Clamp(Mathf.FloorToInt(y / bx), 0, NCY - 1);
        float C2(float[] c2, int i, int j, int f) => c2[(j * NCX + i) * NF2 + f];
        float C3(float[] c3, int L, int i, int j, int f) => c3[((L * NCY + j) * NCX + i) * NF3 + f];

        public void Extract(TimelineFrame fr, float[] c2, float[] c3, float[] sum, Timeline tl)
        {
            int NZ = g.NZ;
            fr.frameMotion = frameMotion;
            // ---- domain scalars
            float au = 0, av = 0, aw = 0;
            fr.maxDbz = -99; fr.echoTop18 = fr.echoTop50 = fr.cloudTop = 0; fr.maxHailMm = fr.maxRainMm = fr.maxHailAccMm = 0;
            for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
            {
                fr.maxDbz = Mathf.Max(fr.maxDbz, C2(c2, i, j, 7));
                fr.echoTop18 = Mathf.Max(fr.echoTop18, C2(c2, i, j, 5)); fr.echoTop50 = Mathf.Max(fr.echoTop50, C2(c2, i, j, 6));
                fr.cloudTop = Mathf.Max(fr.cloudTop, C2(c2, i, j, 17));
                fr.maxHailMm = Mathf.Max(fr.maxHailMm, C2(c2, i, j, 8)); fr.maxRainMm = Mathf.Max(fr.maxRainMm, C2(c2, i, j, 9));
                fr.maxHailAccMm = Mathf.Max(fr.maxHailAccMm, C2(c2, i, j, 10));
                au += C2(c2, i, j, 11); av += C2(c2, i, j, 12); aw += C2(c2, i, j, 13);
            }
            // ---- updraft core
            int fi = Mathf.Clamp((int)sum[0], 0, g.NX - 1), fj = Mathf.Clamp((int)sum[1], 0, g.NY - 1), kmax = Mathf.Clamp((int)sum[3], 0, NZ);
            fr.wMax = sum[2];
            fr.updraftPos = new Vector2((fi + 0.5f) * g.dx, (fj + 0.5f) * g.dy);
            fr.updraftZ = g.zw[kmax];
            fr.wColumn = new float[NZ + 1];
            Array.Copy(sum, 4, fr.wColumn, 0, NZ + 1);
            Vector2 core = fr.updraftPos;
            // 5 km updraft centroid
            int L5 = Mathf.Min(5, NL - 1); float sw = 0; Vector2 sp = Vector2.zero; int n5 = 0;
            for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
            {
                var pos = BlockPos(i, j); if ((pos - core).sqrMagnitude > 12000f * 12000f) continue;
                float w = C3(c3, L5, i, j, 5) - 10f; if (w <= 0) continue;
                sw += w; sp += w * pos; n5++;
            }
            fr.updraftCentroid5km = sw > 0 ? sp / sw : new Vector2(float.NaN, float.NaN);
            fr.updraftArea5km = n5 * bx * bx / 1e6f;
            // storm cell: centroid of the 40 dBZ composite echo within 25 km of the previous frame's cell (or of the
            // updraft core when there is no previous cell), followed frame to frame as radar storm-tracking does
            {
                var prevF = tl.Get(fr.index - 1);
                Vector2 seed = prevF != null && !float.IsNaN(prevF.stormCentroid.x) ? prevF.stormCentroid : core;
                double sw4 = 0; Vector2 sp4 = Vector2.zero;
                for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
                {
                    float dbz = C2(c2, i, j, 7); if (dbz < 40f) continue;
                    var pos = BlockPos(i, j); if ((pos - seed).sqrMagnitude > 25000f * 25000f) continue;
                    float wgt = dbz - 35f; sw4 += wgt; sp4 += wgt * pos;
                }
                fr.stormCentroid = sw4 > 0 ? sp4 / (float)sw4 : new Vector2(float.NaN, float.NaN);
            }
            // storm motion: least-squares drift of that centroid over the last 10 minutes along a contiguous track (no
            // jump over 3 km between frames); at least 3 minutes of track, otherwise the previous estimate is carried
            fr.stormMotion = frameMotion;
            {
                int back = Mathf.RoundToInt(600f / tl.interval), n = 0;
                double st = 0, sx = 0, sy = 0, stt = 0, stx = 0, sty = 0;
                Vector2 lastP = fr.stormCentroid;
                if (!float.IsNaN(lastP.x))
                    for (int k = 0; k <= back; k++)
                    {
                        var p = k == 0 ? fr : tl.Get(fr.index - k);
                        if (p == null || float.IsNaN(p.stormCentroid.x) || (p.stormCentroid - lastP).magnitude > 3000f) break;
                        lastP = p.stormCentroid; double tt = p.t - fr.t;
                        n++; st += tt; sx += lastP.x; sy += lastP.y; stt += tt * tt; stx += tt * lastP.x; sty += tt * lastP.y;
                    }
                double den = n * stt - st * st;
                if (n * tl.interval >= 180f && den > 1e-6)
                    fr.stormMotion = frameMotion + new Vector2((float)((n * stx - st * sx) / den), (float)((n * sty - st * sy) / den));
                else { var last = tl.Get(fr.index - 1); if (last != null) fr.stormMotion = last.stormMotion; }
            }

            // ---- mesocyclones
            fr.meso4 = Meso(c3, Mathf.Min(4, NL - 1), core, 15000f);
            fr.meso1 = Meso(c3, Mathf.Min(1, NL - 1), core, 15000f);
            // depth: contiguous layers around 4 km where the 3x3-max block vorticity stays above 0.01 1/s
            {
                Vector2 mc = fr.meso4.present ? fr.meso4.pos : fr.meso1.present ? fr.meso1.pos : core;
                int ci = BI(mc.x), cj = BJ(mc.y);
                Func<int, float> zl = L => { float m = -1; for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++) { int i = ci + di, j = cj + dj; if (i < 0 || j < 0 || i >= NCX || j >= NCY) continue; m = Mathf.Max(m, C3(c3, L, i, j, 7)); } return m; };
                int start = -1;
                for (int L = Mathf.Min(4, NL - 1); L >= 1 && start < 0; L--) if (zl(L) >= 0.01f) start = L;
                if (start < 0) for (int L = 5; L < Mathf.Min(9, NL) && start < 0; L++) if (zl(L) >= 0.01f) start = L;
                if (start < 0) { fr.mesoBase = fr.mesoTop = fr.mesoDepth = 0; }
                else
                {
                    int lb = start, lt = start;
                    while (lb - 1 >= 0 && zl(lb - 1) >= 0.01f) lb--;
                    while (lt + 1 < NL && zl(lt + 1) >= 0.01f) lt++;
                    fr.mesoBase = Mathf.Max(0, (lb - 0.5f) * 1000f); fr.mesoTop = (lt + 0.5f) * 1000f; fr.mesoDepth = fr.mesoTop - fr.mesoBase;
                }
            }
            Vector2 low = fr.meso1.present ? fr.meso1.pos : core;
            fr.zeta0Peak = 0; fr.w1UnderMeso = 0;
            for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
            {
                float d2 = (BlockPos(i, j) - low).sqrMagnitude;
                if (d2 <= 10000f * 10000f) fr.zeta0Peak = Mathf.Max(fr.zeta0Peak, C3(c3, 0, i, j, 8));
                if (d2 <= 5000f * 5000f && NL > 1) fr.w1UnderMeso = Mathf.Max(fr.w1UnderMeso, C3(c3, 1, i, j, 5));
            }

            // ---- cold pool and gust front
            fr.coldPoolMin = 0; fr.coldPoolMin1km = 0; float area = 0; Vector2 cs = Vector2.zero; var box = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            var cold = new bool[NCX * NCY];
            for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
            {
                float th = C2(c2, i, j, 2);
                fr.coldPoolMin = Mathf.Min(fr.coldPoolMin, th);
                fr.coldPoolMin1km = Mathf.Min(fr.coldPoolMin1km, C2(c2, i, j, 18));
                if (th >= -1f) continue;
                cold[j * NCX + i] = true; var pos = BlockPos(i, j);
                area += 1; cs += pos;
                box.x = Mathf.Min(box.x, pos.x); box.y = Mathf.Min(box.y, pos.y); box.z = Mathf.Max(box.z, pos.x); box.w = Mathf.Max(box.w, pos.y);
            }
            fr.coldPoolArea = area * bx * bx / 1e6f;
            fr.gustFront = new float[36];
            if (area > 0)
            {
                fr.coldPoolCentroid = cs / area; fr.coldPoolBox = box;
                for (int b = 0; b < 36; b++)
                {
                    float a = b * 10f * Mathf.Deg2Rad; var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a)); float last = 0;
                    for (float s = 0; s <= 80000f; s += 0.5f * bx)
                    {
                        var q = fr.coldPoolCentroid + s * dir;
                        if (q.x < 0 || q.y < 0 || q.x >= g.Lx || q.y >= g.Ly) break;
                        if (cold[BJ(q.y) * NCX + BI(q.x)]) last = s;
                    }
                    fr.gustFront[b] = area > 0 ? last + 0.5f * bx : 0;
                }
                // signed distance from the low-level updraft to the -1 K edge
                int pi = BI(low.x), pj = BJ(low.y); bool inside = cold[pj * NCX + pi]; float best = float.MaxValue;
                for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
                {
                    if (cold[j * NCX + i] == inside) continue;
                    best = Mathf.Min(best, (BlockPos(i, j) - low).magnitude);
                }
                fr.updraftToGustFront = best == float.MaxValue ? 0 : (inside ? -best : best);
            }
            else { fr.coldPoolCentroid = new Vector2(float.NaN, float.NaN); fr.coldPoolBox = Vector4.zero; fr.updraftToGustFront = 1e6f; }

            // ---- rear-flank downdraft: not downshear of the low-level meso, w < -2 m/s at 1 km, cold at the ground
            {
                float ra = 0, rw = 0, minW = 0, minT = 0, sumT = 0, reach = 0, wind = 0; Vector2 rc = Vector2.zero; int L1 = Mathf.Min(1, NL - 1);
                for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
                {
                    var pos = BlockPos(i, j); var r = pos - low; float d = r.magnitude;
                    if (d > 12000f) continue;
                    if (d > 0.5f * bx && Vector2.Dot(r, shearDir) > 0.2f * d) continue;
                    float w = C3(c3, L1, i, j, 6), th = C2(c2, i, j, 2);
                    if (w >= -2f || th >= -0.5f) continue;
                    ra += 1; rw += -w; rc += -w * pos; minW = Mathf.Min(minW, w); minT = Mathf.Min(minT, th); sumT += th; reach = Mathf.Max(reach, d);
                    var ug = new Vector2(C2(c2, i, j, 3), C2(c2, i, j, 4)) + frameMotion; wind = Mathf.Max(wind, ug.magnitude);
                }
                fr.rfdArea = ra * bx * bx / 1e6f; fr.rfdPresent = fr.rfdArea >= 2f;
                fr.rfdCentroid = rw > 0 ? rc / rw : new Vector2(float.NaN, float.NaN);
                fr.rfdMinW = minW; fr.rfdMinTheta = minT; fr.rfdMeanTheta = ra > 0 ? sumT / ra : 0; fr.rfdReach = reach; fr.rfdMaxWind = wind;
            }

            // ---- anvil-level wind
            fr.anvilWindGrid = aw > 0 ? new Vector2(au / aw, av / aw) : Vector2.zero;
            fr.anvilWindSR = aw > 0 ? fr.anvilWindGrid + frameMotion - fr.stormMotion : Vector2.zero;

            // ---- 1 km maps
            int nm = NCX * NCY; fr.map2D = new byte[Map2D.Count * nm];
            int[] src = { 7, 14, 2, 0, 1, 3, 4, 8, 9 };
            for (int f = 0; f < Map2D.Count; f++)
                for (int c = 0; c < nm; c++) fr.map2D[f * nm + c] = Map2D.Encode((Map2D.Field)f, c2[c * NF2 + src[f]]);

            // ---- storm-centred 3D window of qc, qr, qi, qh
            int W = Mathf.Min(windowBlocks, NCX), H = Mathf.Min(windowBlocks, NCY);
            Vector2 wc = !float.IsNaN(fr.updraftCentroid5km.x) ? fr.updraftCentroid5km : core;
            fr.winNX = W; fr.winNY = H;
            fr.winI0 = Mathf.Clamp(Mathf.RoundToInt(wc.x / bx) - W / 2, 0, NCX - W);
            fr.winJ0 = Mathf.Clamp(Mathf.RoundToInt(wc.y / bx) - H / 2, 0, NCY - H);
            fr.q3D = new byte[Q3D.Count * NL * W * H];
            int[] qf = { 0, 1, 2, 4 };
            for (int f = 0; f < Q3D.Count; f++) for (int L = 0; L < NL; L++) for (int j = 0; j < H; j++) for (int i = 0; i < W; i++)
                fr.q3D[((f * NL + L) * H + j) * W + i] = Q3D.Encode(C3(c3, L, fr.winI0 + i, fr.winJ0 + j, qf[f]));
            fr.valid = true;
        }

        MesoState Meso(float[] c3, int L, Vector2 around, float radius)
        {
            var m = new MesoState(); float best = float.MinValue; int bi = 0, bj = 0;
            for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
            {
                if ((BlockPos(i, j) - around).sqrMagnitude > radius * radius) continue;
                float z = C3(c3, L, i, j, 7); if (z > best) { best = z; bi = i; bj = j; }
            }
            if (best == float.MinValue) return m;
            m.zeta = best;
            float sw = 0; Vector2 sp = Vector2.zero; float peak = 0;
            for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
            {
                int i = bi + di, j = bj + dj; if (i < 0 || j < 0 || i >= NCX || j >= NCY) continue;
                float z = C3(c3, L, i, j, 7); peak = Mathf.Max(peak, C3(c3, L, i, j, 8));
                if (z > 0) { sw += z; sp += z * BlockPos(i, j); }
            }
            m.pos = sw > 0 ? sp / sw : BlockPos(bi, bj); m.zetaPeak = peak;
            int n = 0; var c = BlockPos(bi, bj);
            if (best > 0) for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
                if ((BlockPos(i, j) - c).sqrMagnitude <= 6000f * 6000f && C3(c3, L, i, j, 7) >= 0.5f * best) n++;
            m.radius = Mathf.Sqrt(n * bx * bx / Mathf.PI);
            m.present = best >= 0.004f;
            return m;
        }
    }
}
