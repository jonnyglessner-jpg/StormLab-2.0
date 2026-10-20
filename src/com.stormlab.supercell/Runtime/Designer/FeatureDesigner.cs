// The feature designer (DESIGN.md 2.3): reads the brain's timeline (TIMELINE.md) and the environment and writes the
// skin's parameter block (SkinParams). Pure C#, no GPU.
//
// Every feature is first measured on each timeline frame (FrameFeatures, cached: frames are immutable), then blended
// over the frames around the playback time with a Gaussian in time, so features fade in and out instead of popping
// and the evaluation is stateless: any playback time, forwards or backwards, gives the same block. A feature the
// simulation does not show over that window falls back to StormLab 1.0's scripted rule (StormDesign.At), placed at the
// simulated updraft, so the storm never looks wrong because a physics term misbehaves.
using System;
using System.Collections.Generic;
using StormLab.Supercell.Dynamics;
using UnityEngine;

namespace StormLab.Supercell.Designer
{
    public sealed class FeatureDesigner
    {
        public readonly Timeline tl;
        public readonly Environment env;
        public readonly StormDiag diag;
        public readonly StormPlan plan;
        /// <summary>Gaussian sigma of the time blend, s (frames are 10 s apart)</summary>
        public float smoothing = 25f;
        /// <summary>sigma for the storm motion and the volume centre, which should barely move, s</summary>
        public float slowSmoothing = 150f;
        /// <summary>how far downwind of the updraft the suggested volume centre sits, km</summary>
        public float centreDownwindKm = 9f;

        readonly int NCX, NCY, NL; readonly float bs;
        readonly Vector2 shearDir;
        readonly float lclKm, elKm;
        readonly Dictionary<int, FrameFeatures> cache = new Dictionary<int, FrameFeatures>();
        float firstEcho = float.NaN;
        readonly StormFrame fallback = new StormFrame();

        public FeatureDesigner(Timeline timeline, Environment environment)
        {
            tl = timeline; env = environment;
            NCX = tl.NCX; NCY = tl.NCY; NL = tl.NL; bs = tl.blockSize;
            diag = StormDiag.Make(env);
            plan = StormDesign.Plan(env, diag);
            var sh = new Vector2((float)(WindProfile.At(env.w.u, 6000) - env.w.u[0]), (float)(WindProfile.At(env.w.v, 6000) - env.w.v[0]));
            shearDir = sh.sqrMagnitude > 1e-4f ? sh.normalized : Vector2.right;
            lclKm = (float)C.Clamp(env.pc.lcl / 1000, 0.5, 2.6);
            elKm = (float)C.Clamp((env.pc.el ?? 12000) / 1000, 8, 16.5);
        }

        // =================================================================== per-frame measurements
        /// <summary>What one timeline frame says about each feature, in grid metres and SI units.</summary>
        public sealed class FrameFeatures
        {
            public float t; public bool storm;
            public float wMax, cloudTop, echoTop18, area5;
            public Vector2 coreLow, core5;
            public float m1, m4; public Vector2 m1Pos, m4Pos; public float m1R, m4R, m1Z, m4Z, mesoBase, mesoTop, w1;
            public float anvil; public float anvilTop, anvilBot, iceArea, iceDown, iceUp, iceHalf; public bool iceClipped; public Vector2 anvilSR;
            public float mammIce;
            public float inflow; public Vector2 inflowSR;
            public float ffd; public Vector2 ffdC, ffdAxis; public float ffdArea, ffdDbz, ffdLen, ffdWid;
            public float hail; public Vector2 hailC; public float hailArea, hailComp, hailMm;
            public float rfd; public Vector2 rfdC; public float rfdReach, rfdTheta, rfdArea;
            public float rcore; public Vector2 rcoreC; public float rcoreR, rcoreDbz;
            public float wrap;
            public float cold; public Vector2 cpC; public float cpArea, cpMin, u2gf;
            public float flank; public Vector2 flankDir; public float flankLen;
            public float vault; public float vaultDepth;
            public Vector2 stormMotion, frameMotion;
        }

        float Map(TimelineFrame f, Map2D.Field fld, int i, int j) => Map2D.Decode(fld, f.map2D[(int)fld * NCX * NCY + j * NCX + i]);
        float Q(TimelineFrame f, Q3D.Field fld, int L, int i, int j)
        {
            int li = i - f.winI0, lj = j - f.winJ0;
            if (li < 0 || lj < 0 || li >= f.winNX || lj >= f.winNY || L < 0 || L >= NL) return 0;
            return Q3D.Decode(f.q3D[(((int)fld * NL + L) * f.winNY + lj) * f.winNX + li]);
        }
        Vector2 BlockPos(int i, int j) => new Vector2((i + 0.5f) * bs, (j + 0.5f) * bs);
        int BI(float x) => Mathf.Clamp(Mathf.FloorToInt(x / bs), 0, NCX - 1);
        int BJ(float y) => Mathf.Clamp(Mathf.FloorToInt(y / bs), 0, NCY - 1);
        static bool Ok(Vector2 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y);

        public FrameFeatures Measure(TimelineFrame f)
        {
            if (cache.TryGetValue(f.index, out var c) && Mathf.Approximately(c.t, f.t)) return c;
            var o = new FrameFeatures { t = f.t, stormMotion = f.stormMotion, frameMotion = f.frameMotion };
            o.wMax = f.wMax; o.cloudTop = f.cloudTop; o.echoTop18 = f.echoTop18; o.area5 = f.updraftArea5km;
            o.storm = f.wMax > 5f && f.cloudTop > lclKm * 1000f + 500f;
            o.core5 = Ok(f.updraftCentroid5km) ? f.updraftCentroid5km : f.updraftPos;
            o.coreLow = f.meso1.present ? f.meso1.pos : o.core5;
            Vector2 sm = f.stormMotion - f.frameMotion;   // storm drift through the grid

            // ---- mesocyclones
            o.m1 = f.meso1.present ? 1 : 0; o.m1Pos = f.meso1.pos; o.m1R = f.meso1.radius; o.m1Z = f.meso1.zeta;
            o.m4 = f.meso4.present ? 1 : 0; o.m4Pos = f.meso4.pos; o.m4R = f.meso4.radius; o.m4Z = f.meso4.zeta;
            o.mesoBase = f.mesoBase; o.mesoTop = f.mesoTop; o.w1 = f.w1UnderMeso;

            // ---- anvil: ice at 6 km and up in the storm-centred window
            o.anvilSR = f.anvilWindSR;
            Vector2 ad = f.anvilWindSR.sqrMagnitude > 4f ? f.anvilWindSR.normalized : (f.anvilWindGrid.sqrMagnitude > 1f ? f.anvilWindGrid.normalized : shearDir);
            if (f.q3D != null)
            {
                var area = new int[NL]; int Lmax = -1, amax = 0;
                for (int L = 5; L < NL; L++)
                {
                    for (int j = f.winJ0; j < f.winJ0 + f.winNY; j++) for (int i = f.winI0; i < f.winI0 + f.winNX; i++)
                        if (Q(f, Q3D.Field.Qi, L, i, j) > 1e-4f) area[L]++;
                    if (L >= 7 && area[L] > amax) { amax = area[L]; Lmax = L; }
                }
                if (Lmax > 0 && amax >= 4)
                {
                    int top = Lmax, bot = Lmax;
                    while (top + 1 < NL && area[top + 1] >= 0.25f * amax) top++;
                    while (bot - 1 >= 5 && area[bot - 1] >= 0.4f * amax) bot--;
                    o.anvilTop = (top + 0.5f) * 1000f; o.anvilBot = (bot - 0.5f) * 1000f;
                    o.iceArea = amax * bs * bs / 1e6f;
                    var an = new Vector2(-ad.y, ad.x);
                    float down = 0, up = 0, half = 0; bool clipped = false;
                    for (int j = f.winJ0; j < f.winJ0 + f.winNY; j++) for (int i = f.winI0; i < f.winI0 + f.winNX; i++)
                    {
                        if (Q(f, Q3D.Field.Qi, Lmax, i, j) <= 1e-4f) continue;
                        var r = BlockPos(i, j) - o.core5; float s = Vector2.Dot(r, ad), q = Vector2.Dot(r, an);
                        down = Mathf.Max(down, s); up = Mathf.Max(up, -s); half = Mathf.Max(half, Mathf.Abs(q));
                        if ((i == f.winI0 || j == f.winJ0 || i == f.winI0 + f.winNX - 1 || j == f.winJ0 + f.winNY - 1) && s > 0) clipped = true;
                    }
                    o.iceDown = down; o.iceUp = up; o.iceHalf = half; o.iceClipped = clipped;
                    o.anvil = 1;
                    // mammatus: ice mass at the anvil's base 6-20 km downwind (pouches need ice sinking from the base)
                    float sq = 0; int nq = 0;
                    for (int j = f.winJ0; j < f.winJ0 + f.winNY; j++) for (int i = f.winI0; i < f.winI0 + f.winNX; i++)
                    {
                        var r = BlockPos(i, j) - o.core5; float s = Vector2.Dot(r, ad);
                        if (s < 6000f || s > 20000f || Mathf.Abs(Vector2.Dot(r, an)) > half * 0.6f) continue;
                        sq += Q(f, Q3D.Field.Qi, bot, i, j); nq++;
                    }
                    o.mammIce = nq > 0 ? sq / nq : 0;
                }
                // ---- inflow vault (BWER): height of the precipitation overhang above the low-level updraft
                {
                    int ci = BI(o.coreLow.x), cj = BJ(o.coreLow.y); int Lp = -1;
                    for (int L = 1; L < NL && Lp < 0; L++)
                    {
                        float pmin = float.MaxValue;
                        for (int dj = -1; dj <= 1; dj++) for (int di = -1; di <= 1; di++)
                            pmin = Mathf.Min(pmin, Q(f, Q3D.Field.Qr, L, ci + di, cj + dj) + Q(f, Q3D.Field.Qh, L, ci + di, cj + dj));
                        if (pmin > 5e-4f) Lp = L;
                    }
                    if (Lp > 0) { o.vaultDepth = (Lp - 0.5f) * 1000f; o.vault = 1; }
                }
            }

            // ---- maps: inflow, forward flank, hail core, hook rain, wrap
            {
                Vector2 sumU = Vector2.zero; int nIn = 0;
                double fw = 0; Vector2 fc = Vector2.zero; float fArea = 0, fDbz = 0;
                double hw = 0; Vector2 hc = Vector2.zero; float hArea = 0, hComp = 0, hMm = 0;
                double rw = 0; Vector2 rc = Vector2.zero; float rArea = 0, rDbz = 0;
                var fpts = new List<Vector3>();
                Vector2 fwd = f.stormMotion.sqrMagnitude > 1f ? f.stormMotion.normalized : (Vector2)ToV(plan.fwd);
                for (int j = 0; j < NCY; j++) for (int i = 0; i < NCX; i++)
                {
                    var p = BlockPos(i, j); var r = p - o.coreLow; float d = r.magnitude;
                    if (d > 30000f) continue;
                    float dbz1 = Map(f, Map2D.Field.Dbz1km, i, j), comp = Map(f, Map2D.Field.CompositeDbz, i, j), th = Map(f, Map2D.Field.ThetaPrimeSfc, i, j);
                    // inflow: warm, rain-free air 6-20 km out on the storm's forward side
                    if (d > 6000f && d < 20000f && th > -0.5f && dbz1 < 15f && Vector2.Dot(r, fwd) > -0.3f * d)
                    {
                        var u = new Vector2(Map(f, Map2D.Field.USfc, i, j), Map(f, Map2D.Field.VSfc, i, j)) - sm;
                        if (Vector2.Dot(u, -r) > 0) { sumU += u; nIn++; }
                    }
                    bool rear = d > 1500f && Vector2.Dot(r, shearDir) <= 0.2f * d;
                    // forward-flank downdraft rain: downshear side
                    if (dbz1 >= 30f && !rear)
                    {
                        float w = dbz1 - 25f; fw += w; fc += w * p; fArea += 1; fDbz = Mathf.Max(fDbz, dbz1);
                        fpts.Add(new Vector3(p.x, p.y, w));
                    }
                    // hook / RFD rain: rear side, close to the low-level meso
                    if (dbz1 >= 35f && rear && d < 8000f) { float w = dbz1 - 30f; rw += w; rc += w * p; rArea += 1; rDbz = Mathf.Max(rDbz, dbz1); }
                    // hail core: the 55 dBZ composite core near the updraft
                    if (comp >= 55f && d < 20000f) { float w = comp - 50f; hw += w; hc += w * p; hArea += 1; hComp = Mathf.Max(hComp, comp); hMm = Mathf.Max(hMm, Map(f, Map2D.Field.HailSizeMm, i, j)); }
                }
                float ba = bs * bs / 1e6f;
                if (nIn >= 4) { o.inflowSR = sumU / nIn; o.inflow = 1; }
                if (fArea >= 6)
                {
                    o.ffd = 1; o.ffdC = fc / (float)fw; o.ffdArea = fArea * ba; o.ffdDbz = fDbz;
                    // principal axes of the weighted rain area
                    double sxx = 0, syy = 0, sxy = 0;
                    foreach (var q in fpts) { double dx = q.x - o.ffdC.x, dy = q.y - o.ffdC.y; sxx += q.z * dx * dx; syy += q.z * dy * dy; sxy += q.z * dx * dy; }
                    sxx /= fw; syy /= fw; sxy /= fw;
                    double tr = sxx + syy, det = sxx * syy - sxy * sxy, disc = Math.Sqrt(Math.Max(tr * tr / 4 - det, 0));
                    double l1 = tr / 2 + disc, l2 = Math.Max(tr / 2 - disc, 0);
                    var ax = Math.Abs(sxy) > 1e-6 ? new Vector2((float)(l1 - syy), (float)sxy).normalized : (sxx >= syy ? Vector2.right : Vector2.up);
                    if (Vector2.Dot(ax, o.ffdC - o.coreLow) < 0) ax = -ax;
                    o.ffdAxis = ax; o.ffdLen = 2f * Mathf.Sqrt((float)l1); o.ffdWid = 2f * Mathf.Sqrt((float)l2);
                }
                if (hArea >= 1) { o.hail = 1; o.hailC = hc / (float)hw; o.hailArea = hArea * ba; o.hailComp = hComp; o.hailMm = hMm; }
                if (rArea >= 2) { o.rcore = 1; o.rcoreC = rc / (float)rw; o.rcoreR = Mathf.Sqrt(rArea * ba / Mathf.PI) * 1000f; o.rcoreDbz = rDbz; }
                // wrap: how much of a ring 3-6 km round the low-level meso is in rain (HP storms wrap all the way)
                if (f.meso1.present)
                {
                    int wet = 0;
                    for (int b = 0; b < 36; b++)
                    {
                        float a = b * 10f * Mathf.Deg2Rad; var dir = new Vector2(Mathf.Sin(a), Mathf.Cos(a)); bool any = false;
                        for (float s = 3000f; s <= 6000f && !any; s += 1000f)
                        {
                            var q = f.meso1.pos + s * dir; if (q.x < 0 || q.y < 0 || q.x >= NCX * bs || q.y >= NCY * bs) continue;
                            any = Map(f, Map2D.Field.Dbz1km, BI(q.x), BJ(q.y)) >= 35f;
                        }
                        if (any) wet++;
                    }
                    o.wrap = wet / 36f;
                }
            }

            // ---- rear-flank downdraft
            if (f.rfdPresent && Ok(f.rfdCentroid)) { o.rfd = 1; o.rfdC = f.rfdCentroid; o.rfdReach = f.rfdReach; o.rfdTheta = f.rfdMeanTheta; o.rfdArea = f.rfdArea; }

            // ---- cold pool, gust front, flanking line
            o.u2gf = f.updraftToGustFront;
            if (f.coldPoolArea > 4f && Ok(f.coldPoolCentroid))
            {
                o.cold = 1; o.cpC = f.coldPoolCentroid; o.cpArea = f.coldPoolArea; o.cpMin = f.coldPoolMin1km;
                // the flanking line follows the rear-flank gust front: its farthest point in the rear-right sector
                Vector2 fwd = f.stormMotion.sqrMagnitude > 1f ? f.stormMotion.normalized : (Vector2)ToV(plan.fwd);
                var lft = new Vector2(-fwd.y, fwd.x); var target = (-fwd - 0.6f * lft).normalized;
                float best = float.MinValue; Vector2 bp = o.coreLow;
                for (int b = 0; b < 36; b++)
                {
                    float a = b * 10f * Mathf.Deg2Rad; var p = f.coldPoolCentroid + f.gustFront[b] * new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                    var r = p - o.coreLow; float d = r.magnitude; if (d < 1f) continue;
                    float score = Vector2.Dot(r / d, target); if (score < 0.5f) continue;
                    score = score * 0.5f + Mathf.Min(d, 25000f) / 25000f;
                    if (score > best) { best = score; bp = p; }
                }
                if (best > float.MinValue) { var r = bp - o.coreLow; o.flankDir = r.normalized; o.flankLen = r.magnitude; o.flank = 1; }
            }
            cache[f.index] = o;
            return o;
        }

        static Vector2 ToV(Vec2 v) => new Vector2((float)v.x, (float)v.y);

        // =================================================================== blending over time
        struct Acc
        {
            public float w, wp; public Vector2 v; public float x;
            public void Add(float wt, float present, Vector2 vv) { w += wt; wp += wt * present; if (present > 0) v += wt * present * vv; }
            public void Add(float wt, float present, float xx) { w += wt; wp += wt * present; if (present > 0) x += wt * present * xx; }
            public float Frac => w > 0 ? wp / w : 0;
            public Vector2 V => wp > 0 ? v / wp : Vector2.zero;
            public float X => wp > 0 ? x / wp : 0;
        }

        readonly List<(FrameFeatures f, float w)> win = new List<(FrameFeatures, float)>();
        readonly List<(FrameFeatures f, float w)> near = new List<(FrameFeatures, float)>();

        void Window(float t, float sigma)
        {
            win.Clear();
            if (tl.Latest < 0) return;
            float t0 = Mathf.Max(0, t - 2f * sigma), t1 = Mathf.Min(tl.Horizon, t + 2f * sigma);
            int i0 = Mathf.FloorToInt(t0 / tl.interval), i1 = Mathf.CeilToInt(t1 / tl.interval);
            for (int i = i0; i <= i1; i++)
            {
                var fr = tl.Get(i); if (fr == null || !fr.valid) continue;
                float d = (fr.t - t) / Mathf.Max(sigma, 1f);
                win.Add((Measure(fr), Mathf.Exp(-0.5f * d * d)));
            }
        }

        /// <summary>model time of the first frame with an updraft over 10 m/s (NaN until there is one)</summary>
        public float FirstEcho
        {
            get
            {
                if (!float.IsNaN(firstEcho)) return firstEcho;
                for (int i = Mathf.Max(0, tl.Oldest); i <= tl.Latest; i++) { var f = tl.Get(i); if (f != null && f.valid && f.wMax > 10f) { firstEcho = f.t; break; } }
                return firstEcho;
            }
        }

        // =================================================================== the parameter block
        static float Ramp(float x, float a, float b) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3 - 2 * t); }

        /// <summary>
        /// Fill the parameter block for playback time t (s). clock = wall-clock seconds (spin inside features);
        /// centreGrid = the volume centre in grid metres (positions are written relative to it, in km).
        /// Returns false when there is no timeline frame at t yet.
        /// </summary>
        public bool Evaluate(float t, float clock, Vector2 centreGrid, SkinParams P)
        {
            P.Clear();
            Window(t, smoothing);
            if (win.Count == 0) return false;
            near.Clear(); near.AddRange(win);
            Feature sim = Feature.None, fb = Feature.None;

            Acc storm = default, coreLow = default, core5 = default, area5 = default, top = default, wmax = default;
            Acc m1 = default, m1z = default, m1r = default, m4 = default, m4z = default, m4r = default, mb = default, mt = default, w1 = default, m1p = default;
            Acc anv = default, aTop = default, aBot = default, aArea = default, aDown = default, aUp = default, aSR = default, mamm = default, clip = default;
            Acc inflow = default, ffd = default, ffdC = default, ffdAx = default, ffdL = default, ffdW = default, ffdZ = default;
            Acc hail = default, hailC = default, hailA = default, hailCmp = default;
            Acc rfd = default, rfdC = default, rfdR = default, rfdT = default, rc = default, rcC = default, rcR = default, rcZ = default, wrap = default;
            Acc cold = default, cpC = default, cpA = default, cpM = default, u2gf = default, flank = default, flankD = default, flankL = default;
            Acc vault = default, vaultD = default;
            foreach (var (f, w) in near)
            {
                float s = f.storm ? 1 : 0;
                storm.Add(w, 1, s);
                coreLow.Add(w, s, f.coreLow); core5.Add(w, s, f.core5); area5.Add(w, s, f.area5); top.Add(w, s, f.cloudTop); wmax.Add(w, 1, f.wMax);
                m1.Add(w, 1, f.m1); m1p.Add(w, f.m1, f.m1Pos); m1z.Add(w, f.m1, f.m1Z); m1r.Add(w, f.m1, f.m1R);
                m4.Add(w, 1, f.m4); m4z.Add(w, f.m4, f.m4Z); m4r.Add(w, f.m4, f.m4R);
                float md = f.mesoTop > 0 ? 1 : 0; mb.Add(w, md, f.mesoBase); mt.Add(w, md, f.mesoTop); w1.Add(w, 1, f.w1);
                anv.Add(w, 1, f.anvil); aTop.Add(w, f.anvil, f.anvilTop); aBot.Add(w, f.anvil, f.anvilBot); aArea.Add(w, 1, f.iceArea);
                aDown.Add(w, f.anvil, f.iceDown); aUp.Add(w, f.anvil, f.iceUp); aSR.Add(w, 1, f.anvilSR); mamm.Add(w, f.anvil, f.mammIce); clip.Add(w, f.anvil, f.iceClipped ? 1 : 0);
                inflow.Add(w, f.inflow, f.inflowSR);
                ffd.Add(w, 1, f.ffd); ffdC.Add(w, f.ffd, f.ffdC); ffdAx.Add(w, f.ffd, f.ffdAxis); ffdL.Add(w, f.ffd, f.ffdLen); ffdW.Add(w, f.ffd, f.ffdWid); ffdZ.Add(w, f.ffd, f.ffdDbz);
                hail.Add(w, 1, f.hail); hailC.Add(w, f.hail, f.hailC); hailA.Add(w, f.hail, f.hailArea); hailCmp.Add(w, f.hail, f.hailComp);
                rfd.Add(w, 1, f.rfd); rfdC.Add(w, f.rfd, f.rfdC); rfdR.Add(w, f.rfd, f.rfdReach); rfdT.Add(w, f.rfd, f.rfdTheta);
                rc.Add(w, 1, f.rcore); rcC.Add(w, f.rcore, f.rcoreC); rcR.Add(w, f.rcore, f.rcoreR); rcZ.Add(w, f.rcore, f.rcoreDbz);
                wrap.Add(w, f.m1, f.wrap);
                cold.Add(w, 1, f.cold); cpC.Add(w, f.cold, f.cpC); cpA.Add(w, f.cold, f.cpArea); cpM.Add(w, f.cold, f.cpMin);
                u2gf.Add(w, f.cold, Mathf.Clamp(f.u2gf, -20000f, 20000f));
                flank.Add(w, 1, f.flank); flankD.Add(w, f.flank, f.flankDir); flankL.Add(w, f.flank, f.flankLen);
                vault.Add(w, 1, f.vault); vaultD.Add(w, f.vault, f.vaultDepth);
            }

            // slow quantities: storm motion and the volume centre
            Window(t, slowSmoothing);
            Acc mot = default, slowCore = default, slowSR = default; Vector2 frameMotion = Vector2.zero;
            foreach (var (f, w) in win) { mot.Add(w, f.storm ? 1 : 0, f.stormMotion); slowCore.Add(w, f.storm ? 1 : 0, f.coreLow); slowSR.Add(w, f.anvil, f.anvilSR); frameMotion = f.frameMotion; }

            // ---- what is happening, and the 1.0 fallback at the same storm age
            float tFire = FirstEcho;
            float age = float.IsNaN(tFire) ? -1f : (t - tFire) / 60f;
            float stormAmt = storm.X;
            Vector2 motion = mot.wp > 0 ? mot.V : frameMotion;
            Vector2 fwd = motion.sqrMagnitude > 1f ? motion.normalized : ToV(plan.fwd); var lft = new Vector2(-fwd.y, fwd.x);
            Vector2 low = coreLow.wp > 0 ? coreLow.V : (slowCore.wp > 0 ? slowCore.V : new Vector2(NCX * bs * 0.45f, NCY * bs * 0.5f));
            Vector2 c5 = core5.wp > 0 ? core5.V : low;
            Vector2 V(Vector2 g) => (g - centreGrid) / 1000f;
            Vector2 up = V(low);

            StormDesign.At(plan, Mathf.Max(age, 0f), clock, fallback);
            var F1 = fallback.p; Vector2 up1 = new Vector2(F1[16 * 4], F1[16 * 4 + 1]);
            Vector4 Fb(Slot s, bool pos)
            {
                int k = (int)s * 4; var r = new Vector4(F1[k], F1[k + 1], F1[k + 2], F1[k + 3]);
                if (pos) { r.x += up.x - up1.x; r.y += up.y - up1.y; }
                return r;
            }

            float zb = lclKm;
            bool anvilSim = anv.Frac > 0.05f;
            float anvTop = anvilSim ? aTop.X / 1000f : elKm;
            float el = Mathf.Clamp(anvTop, 7f, 17.5f);
            Vector2 aSRv = aSR.wp > 0 ? aSR.V : Vector2.zero;
            Vector2 slowAnv = slowSR.wp > 0 ? slowSR.V : aSRv;
            Vector2 adir = slowAnv.sqrMagnitude > 4f ? slowAnv.normalized : ToV(plan.adir);

            P.Set(Slot.F, fwd.x, fwd.y, lft.x, lft.y);
            P.Set(Slot.Base, zb, el, clock, age);
            P.Set(Slot.Up, up.x, up.y, 0, 0);
            P.Set(Slot.Motion, motion.x, motion.y, frameMotion.x, frameMotion.y);
            sim |= Feature.Motion;
            int pm = diag.precip == Precip.LP ? 0 : diag.precip == Precip.Classic ? 1 : diag.precip == Precip.HP ? 2 : -1;
            P.Set(Slot.Env, (float)(env.pc.lcl / 1000), (float)((env.pc.el ?? 0) / 1000), (float)(env.pc.cape / 1000), pm);
            // the precipitation curtain: thin, streaky and back-lit for LP, dense for HP (1.0's hpness: anvil-level storm-relative
            // wind and mid-level moisture); how much rain there is and where still comes from the simulated reflectivity
            float hp = (float)diag.hpness;
            P.Set(Slot.Curtain, Mathf.Clamp(0.7f + 0.7f * hp, 0.3f, 1.2f), Mathf.Clamp(0.6f - 0.4f * hp, 0.25f, 0.85f), Mathf.Clamp01(0.5f - 0.6f * hp), hp);

            // a capped day: the sim keeps it quiet, 1.0 draws the bubbling cumulus
            if (stormAmt < 0.02f)
            {
                if (plan.kind == StormKind.Capped)
                {
                    P[Slot.Tower] = Fb(Slot.Tower, false); P[Slot.Base] = new Vector4(zb, el, clock, Mathf.Max(age, 0));
                    fb |= Feature.Tower;
                }
                P.stage = age < 0 && !float.IsNaN(tFire) ? "Before initiation" : plan.kind == StormKind.Capped ? "Capped" : "Clear";
                P.caption = plan.kind == StormKind.Capped ? "The cap holds: cumulus bubble up and fall back." : "No storm in the simulation at this time.";
                Finish(P, t, tFire, sim, fb, near.Count, low, adir);
                return true;
            }

            // ---- updraft tower
            float towerTop = Mathf.Max(zb + 0.6f, top.X / 1000f);
            float a5 = area5.X;
            float R = a5 > 1f ? Mathf.Clamp(0.9f + 0.5f * Mathf.Sqrt(a5 / Mathf.PI), 1.6f, 4.0f) * (diag.precip == Precip.LP ? 0.9f : 1f) : (float)plan.R * Mathf.Lerp(0.6f, 1f, Ramp(wmax.X, 5, 25));
            float towerAmt = stormAmt * Mathf.Clamp01(wmax.X / 10f);
            float m4f = m4.Frac, m1f = m1.Frac;
            float strat = (float)plan.striation * m4f * Mathf.Clamp(m4z.X / 0.02f, 0.4f, 1.2f);
            if (m4f > 0.05f) sim |= Feature.Striation | Feature.Meso; else if (age > 20) { strat = Fb(Slot.Tower, false).w; fb |= Feature.Striation; }
            P.Set(Slot.Tower, Mathf.Min(towerTop, el + 0.4f), R, towerAmt, strat);
            sim |= Feature.Tower;
            // ---- the storm's look as it matures (live 1.0's timings, on the simulated storm's age): the barrel widens at
            // mid levels once it rotates there, the anvil thins to a wide veil of ice late on, and the flanking line matures
            {
                var u = P[Slot.Up];
                u.z = Ramp(age, 12f, 44f) * Mathf.Clamp01(m4f * 1.5f);
                u.w = anvilSim ? Ramp(age, 68f, 93f) : 0f;
                P[Slot.Up] = u;
                P.Set(Slot.Late, Ramp(age, 68f, 90f), 0, 0, 0);
            }
            // ---- stacked plates on the low barrel (near field): 3-7 laminar discs over its lowest 1.2-1.75 km, more and
            // stronger as the striation (the simulated mid-level rotation x 1.0's LP/classic/HP factor) grows. They turn at
            // the simulated rotation (MesoV.z low down, Tilt.w aloft). The LP photos show 3-8 over 2-3 km; the near box
            // reaches 2 km above the base, and the storm volume's grooves carry the striations above it
            {
                int plates = Mathf.RoundToInt(Mathf.Lerp(3f, 7f, Mathf.Clamp01((strat - 0.15f) / 0.75f)));
                float hb = Mathf.Clamp(0.25f * plates + 0.2f, 1.2f, 1.75f);
                P.Set(Slot.Plates, plates, hb / plates, hb, Mathf.Clamp01(1.7f * strat));
            }

            // ---- tilt, overshooting top, mid-level spin
            Vector2 tilt = (c5 - low) / Mathf.Max(5000f - zb * 1000f, 1500f);
            if (tilt.magnitude > 0.35f) tilt = tilt.normalized * 0.35f;
            float otH = anvilSim ? Mathf.Clamp(top.X / 1000f - anvTop, 0f, 2.5f) : 0f;
            float overshoot = Mathf.Clamp(otH / 1.8f, 0f, 1.3f);
            float spin4 = 0.5f * m4z.X * m4f;
            P.Set(Slot.Tilt, tilt.x, tilt.y, overshoot, spin4);
            Vector2 otC = up + tilt * (el - zb);
            P.Set(Slot.Ot, otC.x, otC.y, otH, R * 0.75f);
            if (anvilSim) sim |= Feature.Overshoot;

            // ---- anvil
            float anvilAge = 0; // minutes since the anvil first spread
            {
                for (int i = Mathf.Max(0, tl.Oldest); i <= tl.Latest; i++)
                {
                    var fr = tl.Get(i); if (fr == null || !fr.valid || fr.t > t) break;
                    if (Measure(fr).anvil > 0 && Measure(fr).iceArea > 30f) { anvilAge = (t - fr.t) / 60f; break; }
                }
            }
            if (anvilSim)
            {
                float growth = Mathf.Clamp01(aArea.X / 250f) * anv.Frac;
                float down = aDown.X / 1000f;
                float srs = slowAnv.magnitude;
                // The model counts ice above a threshold, and the far anvil thins below it (or runs off the domain) while it
                // is still visible cirrus: the anvil reaches at least as far as the ice the storm-relative anvil wind has
                // carried downwind since it began spreading (x 0.7: ice falls out on the way). Late in the storm (Up.w) up
                // to about 95 km, as live 1.0 draws it; the march draws it beyond the skin's 60 km volume
                float thinA = P[Slot.Up].w;
                float lenMax = 52f + 43f * thinA;
                float len = Mathf.Max(down, Mathf.Min(srs * anvilAge * 60f / 1000f * 0.7f, (float)plan.anvilLen + 43f * thinA));
                len = Mathf.Clamp(len, 4f, lenMax);
                float back = Mathf.Clamp(aUp.X / 1000f, 1f, 12f);
                float bottom = Mathf.Clamp(aBot.X / 1000f, el - 4f, el - 1f);
                // mammatus: ice at the anvil's base downwind, once the anvil has spread for a while
                float mam = Mathf.Clamp01((mamm.X - 3e-5f) / 1.5e-4f) * Ramp(anvilAge, 15f, 30f);
                if (mamm.X > 0) sim |= Feature.Mammatus; else { mam = Fb(Slot.Anvil2, false).z; fb |= Feature.Mammatus; }
                P.Set(Slot.Anvil, adir.x, adir.y, len, growth);
                P.Set(Slot.Anvil2, bottom, el + 0.25f, mam, back);
                Vector2 mc = up + adir * (len * 0.3f + 4f);
                P.Set(Slot.Mamm, mc.x, mc.y, len * 0.22f, 0.25f + 0.35f * mam);
                sim |= Feature.Anvil;
            }
            else
            {
                // no ice spreading yet: 1.0's young anvil (normally zero this early)
                var a = Fb(Slot.Anvil, false); a.x = adir.x; a.y = adir.y; P[Slot.Anvil] = a;
                var a2 = Fb(Slot.Anvil2, false); a2.x = el - 2.4f; a2.y = el + 0.25f; P[Slot.Anvil2] = a2;
                if (a.w > 0) fb |= Feature.Anvil;
            }
            P.Set(Slot.Anvil3, aSRv.x, aSRv.y, anvilAge, Mathf.Clamp01(aArea.X / 400f));

            // ---- mesocyclones
            Vector2 m1Pos = m1p.wp > 0 ? V(m1p.V) : up;
            P.Set(Slot.Meso1, m1Pos.x, m1Pos.y, m1r.X / 1000f, m1z.X * m1f);
            P.Set(Slot.Meso4, 0, 0, m4r.X / 1000f, m4z.X * m4f);
            {
                Acc m4p = default; foreach (var (f, w) in near) m4p.Add(w, f.m4, f.m4Pos);
                Vector2 p4 = m4p.wp > 0 ? V(m4p.V) : up + tilt * 4f; var m = P[Slot.Meso4]; m.x = p4.x; m.y = p4.y; P[Slot.Meso4] = m;
            }
            float u2 = u2gf.wp > 0 ? u2gf.X / 1000f : 20f;
            P.Set(Slot.MesoV, mb.X / 1000f, mt.X / 1000f, 0.5f * m1z.X * m1f, u2);

            // ---- wall cloud, tail cloud, scud (low-level meso, not undercut by the cold pool)
            float Rw = (float)plan.Rw;
            float lowRot = 0f;   // how established the low-level rotation (and so the wall cloud) is, 0..1
            if (m1f > 0.05f)
            {
                float zeta1 = m1z.X;
                // undercut by the cold pool: the wall cloud shrinks to 30 % once the gust front is 6 km past the updraft, but a
                // live low-level meso keeps some lowering (the brain's cold pools run 1-3 K too cold, so its occlusion
                // signal comes early and stays on; a full cut-off would hide the wall cloud for the rest of the storm)
                float occl = Mathf.Lerp(0.3f, 1f, Mathf.Clamp01(1f + u2 / 6f));
                float amt = m1f * Ramp(zeta1, 0.004f, 0.012f) * occl * Mathf.Clamp01(w1.X / 6f);
                Rw = Mathf.Clamp(0.55f * m1r.X / 1000f, 1.2f, 2.8f) * (diag.precip == Precip.HP ? 1.15f : 1f);
                float meso = Mathf.Clamp(zeta1 / 0.015f, 0.3f, 1.2f);
                float lower = Mathf.Clamp(0.25f + 0.3f * meso, 0.2f, 0.6f) * Mathf.Clamp(1.6f - zb / 2f, 0.6f, 1.3f);
                float ffdNear = Mathf.Clamp01(ffd.Frac) * Mathf.Clamp01((ffdZ.X - 30f) / 15f);
                lowRot = m1f * Ramp(zeta1, 0.004f, 0.012f);
                P.Set(Slot.Wall, m1Pos.x, m1Pos.y, Rw, amt);
                P.Set(Slot.Wall2, lower, 0.5f * zeta1, amt * ffdNear, amt * 0.8f);
                sim |= Feature.Wall;
            }
            else if (age > 30)
            {
                P[Slot.Wall] = Fb(Slot.Wall, true); P[Slot.Wall2] = Fb(Slot.Wall2, false);
                fb |= Feature.Wall;
            }

            // ---- inflow tail and vault
            float inflowFrac = inflow.Frac;
            Vector2 inSR = inflow.wp > 0 ? inflow.V : Vector2.zero; float inSpd = inSR.magnitude;
            float rh = (float)C.Clamp(1 - (env.pc.lcl - 500) / 2500, 0, 1);
            P.Set(Slot.Inflow, inSR.x, inSR.y, inSpd, rh);
            Vector2 tdir;
            if (inflowFrac > 0.3f && inSpd > 2f)
            {
                tdir = -inSR / inSpd;
                float tl0 = Mathf.Clamp(6f + 0.4f * inSpd, 6f, 16f);
                float amt = (float)plan.tail * Mathf.Max(m1f, m4f) * Ramp(inSpd, 3f, 10f) * Ramp(age, 15f, 25f);
                P.Set(Slot.Tail, tdir.x, tdir.y, tl0, amt);
                int bands = 1 + (inSpd > 12f ? 1 : 0) + (rh > 0.65f ? 1 : 0);
                P.Set(Slot.Tail2, bands, 0.35f + 0.1f * bands, 0.2f + 0.4f * rh, 0.035f);
                sim |= Feature.Tail;
            }
            else
            {
                var tf = Fb(Slot.Tail, false); P[Slot.Tail] = tf; tdir = new Vector2(tf.x, tf.y);
                P.Set(Slot.Tail2, 1, 0.45f, 0.4f, 0.035f);
                if (tf.w > 0) fb |= Feature.Tail;
            }
            {
                float vd = vault.Frac > 0.2f ? Mathf.Clamp(vaultD.X / 1000f - zb, 0f, 10f) : 3f;
                float amt = vault.Frac > 0.2f ? Ramp(vd, 0.5f, 3f) : 0.6f;
                P.Set(Slot.Vault, tdir.x, tdir.y, vd, amt);
                if (vault.Frac > 0.2f) sim |= Feature.Vault; else fb |= Feature.Vault;
            }

            // ---- forward-flank rain, hail core
            if (ffd.Frac > 0.05f)
            {
                Vector2 c = V(ffdC.V); float area = Mathf.Max(ffdL.X * ffdW.X * Mathf.PI / 1e6f, 1f);
                float size = Mathf.Clamp(Mathf.Sqrt(area / (24f * Mathf.PI)) * 1.6f, 0.5f, 2f);
                P.Set(Slot.Ffd, c.x, c.y, size, ffd.Frac * Mathf.Clamp((ffdZ.X - 30f) / 20f, 0f, 1.5f));
                Vector2 ax = ffdAx.V.sqrMagnitude > 1e-4f ? ffdAx.V.normalized : fwd;
                P.Set(Slot.Ffd2, ax.x, ax.y, Mathf.Clamp(ffdL.X / 1000f, 2f, 20f), Mathf.Clamp(ffdW.X / 1000f, 1.5f, 12f));
                sim |= Feature.Ffd;
            }
            else if (age > 14) { P[Slot.Ffd] = Fb(Slot.Ffd, true); fb |= Feature.Ffd; }
            if (hail.Frac > 0.05f)
            {
                Vector2 c = V(hailC.V);
                P.Set(Slot.Hail, c.x, c.y, Mathf.Clamp(Mathf.Sqrt(hailA.X / Mathf.PI) * 1.3f, 1f, 4f), hail.Frac * Mathf.Clamp((hailCmp.X - 50f) / 15f, 0f, 1.2f));
                sim |= Feature.Hail;
            }
            else if (age > 18) { P[Slot.Hail] = Fb(Slot.Hail, true); fb |= Feature.Hail; }

            // ---- rear-flank downdraft: clear slot, horseshoe, hook rain, wrap
            float wrapAmt = Mathf.Clamp((wrap.X - 0.45f) / 0.35f, 0f, 1f) * 0.75f;
            if (rfd.Frac > 0.05f)
            {
                Vector2 c = V(rfdC.V); float reach = rfdR.X / 1000f;
                float dry = Mathf.Clamp01(1f + rfdT.X / 4f);   // mean surface theta' 0 K = 1 (warm), -4 K = 0 (cold)
                float slot = rfd.Frac * (0.4f + 0.6f * dry) * (1f - 0.5f * wrapAmt);
                // the order of events (live 1.0): the wall cloud forms under the low-level rotation first, and only then
                // does the RFD carve a clear slot beside it; the horseshoe follows the slot as it widens
                slot *= Ramp(lowRot, 0.05f, 0.35f);
                float horse = slot * Ramp(slot, 0.1f, 0.45f);
                // The slot as the cloud base shows it: live 1.0's compact rim, a little under half the updraft's radius,
                // growing with the slot, whatever the size of the simulated outflow surge beneath it (that is the gust
                // front, which reaches much further than the notch in the base)
                float Rtow = P[Slot.Tower].y;
                float Rs = Mathf.Min(Mathf.Clamp((c - m1Pos).magnitude * 1.1f, 1.2f, 4f), Mathf.Min(Rtow * 0.462f, Mathf.Max(Rtow * 0.9f - 0.85f, 0.3f)))
                           * (0.2f + 0.8f * Ramp(slot, 0f, 0.6f));
                // and the whole rim, lip included, stays under the updraft: the surge's centre is pulled in toward the
                // updraft if the simulated one lies further out
                Vector2 dU = c - up; float fitReach = Mathf.Max(Rtow * 0.9f - Rs - 0.65f, 0.15f);
                if (dU.magnitude > fitReach) c = up + dU.normalized * fitReach;
                float rainAmt = rc.Frac * Mathf.Clamp((rcZ.X - 30f) / 20f, 0f, 1.4f);
                P.Set(Slot.Rfd, slot, Mathf.Clamp(reach * 0.7f, Rw + 0.8f, Rw + 3f), rainAmt, wrapAmt);
                P.Set(Slot.Rfdb, c.x, c.y, Rs, horse);
                // (a tornado, when Phase 3 adds one, forms only once this slot is open: Rfd.x past ~0.3)
                P.Set(Slot.Slot2, c.x, c.y, dry, reach);
                sim |= Feature.Rfd;
            }
            else if (age > 30)
            {
                P[Slot.Rfd] = Fb(Slot.Rfd, false); P[Slot.Rfdb] = Fb(Slot.Rfdb, true);
                fb |= Feature.Rfd;
            }
            if (rc.Frac > 0.05f) { Vector2 c = V(rcC.V); P.Set(Slot.RfdCore, c.x, c.y, Mathf.Clamp(rcR.X / 1000f, 0.8f, 4f), rc.Frac * Mathf.Clamp((rcZ.X - 30f) / 20f, 0f, 1.4f)); }

            // ---- southern curl (anti-mesocyclone): not in the timeline, so always 1.0's rule
            {
                var an = Fb(Slot.Anti, true); an.w = 0;   // the anticyclonic tornado belongs to Phase 3
                P[Slot.Anti] = an; if (an.z > 0) fb |= Feature.Anti;
            }

            // ---- cold pool and flanking line
            if (cold.Frac > 0.05f)
            {
                Vector2 c = V(cpC.V);
                P.Set(Slot.Gust, c.x, c.y, Mathf.Sqrt(cpA.X / Mathf.PI), -cpM.X);
                sim |= Feature.ColdPool;
            }
            if (flank.Frac > 0.05f)
            {
                Vector2 d = flankD.V.sqrMagnitude > 1e-4f ? flankD.V.normalized : -fwd;
                float len = Mathf.Clamp(flankL.X / 1000f, 4f, 25f);
                float growth = flank.Frac * Ramp(cpA.X, 30f, 250f);
                P.Set(Slot.Flank, d.x, d.y, growth, Mathf.Clamp(Mathf.Round(len / 2.7f), 3, 8));
                P.Set(Slot.Flank2, up.x, up.y, len, growth);
                sim |= Feature.Flank;
            }
            else if (age > 18) { P[Slot.Flank] = Fb(Slot.Flank, false); fb |= Feature.Flank; }

            // ---- stage and caption, from what the sim shows
            int stageIx;
            bool rfdOn = rfd.Frac > 0.3f, occluded = u2 < -1.5f;
            if (!anvilSim) { stageIx = 1; P.stage = "Towering cumulus"; P.caption = "An updraft breaks through and rises as a hard, cauliflower tower."; }
            else if (m4f < 0.3f) { stageIx = 2; P.stage = "Cumulonimbus"; P.caption = "The tower reaches the tropopause and spreads out as an anvil downwind; rain and hail start to fall."; }
            else if (m1f < 0.3f) { stageIx = 3; P.stage = "Supercell organising"; P.caption = "The updraft rotates aloft (a mesocyclone); the base flattens and inflow streams in."; }
            else if (occluded) { stageIx = 6; P.stage = "Occlusion"; P.caption = "The gust front has wrapped round and undercut the low-level updraft; a new updraft base takes over."; }
            else if (rfdOn) { stageIx = 5; P.stage = "Mature supercell"; P.caption = "A rear-flank downdraft surges round the wall cloud and carves a clear slot into the base."; }
            else { stageIx = 4; P.stage = "Wall cloud"; P.caption = "Rotation reaches down to 1 km: rain-cooled air from the forward flank condenses into a rotating wall cloud."; }
            P[Slot.Time] = new Vector4(t, tl.Horizon, float.IsNaN(tFire) ? -1 : tFire, stageIx);
            Finish(P, t, tFire, sim, fb, near.Count, slowCore.wp > 0 ? slowCore.V : low, adir);
            return true;
        }

        void Finish(SkinParams P, float t, float tFire, Feature sim, Feature fb, int n, Vector2 lowGrid, Vector2 adir)
        {
            P.fromSim = sim; P.fromFallback = fb;
            P.Set(Slot.Source, (float)(int)sim, (float)(int)fb, smoothing, n);
            if (P[Slot.Time].x == 0 && t > 0) P[Slot.Time] = new Vector4(t, tl.Horizon, float.IsNaN(tFire) ? -1 : tFire, 0);
            P.suggestedCentre = lowGrid + adir * centreDownwindKm * 1000f;
        }
    }
}
