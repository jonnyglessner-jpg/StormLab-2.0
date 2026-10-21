// Storm-driven lightning activity: flash rate and flash-type split from the designed storm or the brain's timeline.
// Presentation estimates, not an electrification solver. See docs/meteorology/cloud-microphysics.md (Electrification).
using System;
using System.Collections.Generic;
using StormLab.Supercell.Dynamics;
using UnityEngine;

namespace StormLab.Supercell.Lightning
{
    /// <summary>Look controls for storm-driven lightning. Changing any of them changes the flash history.</summary>
    [Serializable]
    public sealed class StormLightningTuning
    {
        [Tooltip("Multiplies every storm-driven flash rate.")]
        [Min(0)] public float rateScale = 1;
        [Tooltip("Designed storms: total flashes per minute of a mature supercell with a 35 m/s, 3 km updraft.")]
        [Min(0)] public float designedReferencePerMinute = 80;
        [Tooltip("Designed storms: rate increase before each tornado (lightning jump), as a fraction of the rate.")]
        [Range(0, 3)] public float jumpBeforeTornado = .8f;
        [Tooltip("Designed storms: rate increase as each new updraft cycle starts.")]
        [Range(0, 3)] public float jumpNewCycle = .3f;
        [Tooltip("Brain: flashes per km² per 5 min per (m/s x g/kg) of graupel flux at -15 °C (McCaul et al. 2009).")]
        [Min(0)] public float graupelFluxCoefficient = .042f;
        [Tooltip("Brain: flashes per km² per 5 min per kg/m² of column ice. 0.2 is the value usually quoted for McCaul " +
            "et al. 2009; not checked against the paper.")]
        [Min(0)] public float columnIceCoefficient = .2f;
        [Tooltip("Brain: multiplies the brain's rate. 1 km block means smooth out the graupel-flux peaks.")]
        [Min(0)] public float brainRateScale = 1;
        [Tooltip("Strong updrafts on the Plains: extra positive share of ground flashes (inverted-polarity storms).")]
        [Range(0, 1)] public float invertedPolarityBoost = .2f;
    }

    /// <summary>Flash-type split shared by both sources. Ground fractions follow cloud-microphysics.md: about 20 % of flashes
    /// reach the ground, under 10 % on the High Plains, about 9 % of those positive.</summary>
    static class LightningPolarity
    {
        /// <param name="w">updraft speed, m/s</param>
        /// <param name="rain">0..1, how far the rain core has reached the ground</param>
        /// <param name="firstRain">0..1, the first minutes of rain at the ground (highest CG share)</param>
        /// <param name="decay">0..1, how far the storm has weakened from its peak (anvil and stratiform charge)</param>
        public static void Split(ClimoRegion region, double w, double rain, double firstRain, double decay,
            StormLightningTuning k, out float ground, out float positive)
        {
            double baseGround = region == ClimoRegion.Plains ? .12 : region == ClimoRegion.Dixie ? .22 : .2;
            // Strong updrafts lift the main negative charge, so more flashes stay in the cloud.
            double lift = C.Clamp(1.25 - w / 80, .6, 1.1);
            ground = (float)C.Clamp(baseGround * (.3 + .7 * rain) * (1 + .5 * firstRain) * lift * (1 + .6 * decay), 0, .6);
            double plains = region == ClimoRegion.Plains ? 1 : region == ClimoRegion.Midwest ? .5 : .2;
            positive = (float)C.Clamp(.09 + k.invertedPolarityBoost * plains * Ramp(w, 35, 55) * (1 - decay) + .25 * decay, 0, .6);
        }
        public static double Ramp(double t, double a, double b) { double x = C.Clamp((t - a) / (b - a), 0, 1); return x * x * (3 - 2 * x); }
    }

    /// <summary>Lightning from the designed storm's life story: updraft size and speed, age, cycles and decay.
    /// Depends only on the storm and t, so it replays.</summary>
    public static class DesignedLightning
    {
        public static LightningActivity At(DesignedStorm storm, double t, StormLightningTuning k)
        {
            if (storm == null || t < 0 || t > storm.lifeS || (storm.kind != StormKind.Supercell && storm.kind != StormKind.Pulse))
                return LightningActivity.None;
            var ch = storm.CharacterAt(t);
            if (ch.w <= 0) return LightningActivity.None;
            double tm = t / 60;
            // Flash rate follows updraft volume through the charging zone: updraft area times a speed term, and the depth of
            // cloud colder than freezing where graupel and ice collide.
            double R = ch.R * UpdraftScale(storm, t);
            double volume = (R / 3) * (R / 3) * Math.Pow(C.Clamp((ch.w - 8) / 27, 0, 2), 1.5);
            double coldDepth = LightningPolarity.Ramp(ch.el - ch.freezeKm, 2, 6);
            double growth, live, rain, firstRain, jump = 0;
            if (storm.kind == StormKind.Supercell)
            {
                // First flashes as the tower passes the charging zone (DesignedStorm: tower 0-14 min, anvil and rain 14-24).
                growth = LightningPolarity.Ramp(tm, 8, 22);
                live = 1 - LightningPolarity.Ramp(t, storm.lifeS - storm.genome.decayS, storm.lifeS - .25 * storm.genome.decayS);
                rain = LightningPolarity.Ramp(tm, 14, 24);
                firstRain = LightningPolarity.Ramp(tm, 16, 22) * (1 - LightningPolarity.Ramp(tm, 28, 40));
                foreach (var c in storm.cycles)
                {
                    // Lightning jump: the rate surges 10-20 min before touchdown and eases off as the tornado forms.
                    if (c.tornado != null)
                    {
                        double f = c.tornado.formS;
                        jump = Math.Max(jump, k.jumpBeforeTornado * LightningPolarity.Ramp(t, f - 1200, f - 600) *
                            (1 - LightningPolarity.Ramp(t, f - 120, f + 480)));
                    }
                    jump = Math.Max(jump, k.jumpNewCycle * LightningPolarity.Ramp(t, c.wallS - 900, c.wallS - 300) *
                        (1 - LightningPolarity.Ramp(t, c.wallS + 120, c.wallS + 720)));
                }
            }
            else
            {
                // Pulse storm (StormDesign stages): towering cumulus to 13 min, mature 13-22, heavy rain 22-34, collapsing
                // 34-50. The flash rate peaks before the collapse.
                growth = LightningPolarity.Ramp(tm, 9, 16);
                live = 1 - LightningPolarity.Ramp(tm, 26, 45);
                rain = LightningPolarity.Ramp(tm, 15, 24);
                firstRain = LightningPolarity.Ramp(tm, 18, 23) * (1 - LightningPolarity.Ramp(tm, 30, 40));
            }
            double rate = k.rateScale * k.designedReferencePerMinute * volume * coldDepth * growth * live * (1 + jump);
            LightningPolarity.Split(storm.genome.region, ch.w * live, rain, firstRain, 1 - live, k, out float ground, out float positive);
            return new LightningActivity((float)Math.Max(0, rate), ground, positive);
        }

        // The cycles' updraft size, eased over 20 min around each new wall cloud (as DesignedStorm.UpdraftScale).
        static double UpdraftScale(DesignedStorm storm, double t)
        {
            double s = 1;
            foreach (var c in storm.cycles) s = C.Lerp(s, c.updraft, LightningPolarity.Ramp(t, c.wallS - 600, c.wallS + 600));
            return s;
        }
    }

    /// <summary>Lightning from the brain's timeline, after McCaul et al. (2009): graupel flux at -15 °C (weight 0.95) and
    /// column ice (weight 0.05), each a flash density per km² per 5 min, summed over the 40 km storm window. Each frame is
    /// reduced when it arrives; the summaries outlive the timeline's ring buffer, so seeks back keep them.</summary>
    public sealed class BrainLightning : IDisposable
    {
        struct Summary
        {
            public bool has;
            /// <summary>sum over blocks of w x qh at -15 °C x block area, m/s g/kg km²; and of column ice x area, kg/m² km²</summary>
            public float graupelFlux, columnIce;
            public float wMax, rainKm2;
        }

        public readonly StormRun run;
        public readonly ClimoRegion region;
        readonly StormLightningTuning tuning;
        readonly List<Summary> frames = new List<Summary>();
        // Running peak rate and the first frame with rain at the ground, filled from the first frame up.
        readonly List<float> peak = new List<float>();
        int firstRainIndex = -1, prefixValidThrough = -1;
        readonly float z15;
        readonly float[] rhoAtLayer;

        public BrainLightning(StormRun run, ClimoRegion region, StormLightningTuning tuning)
        {
            this.run = run ?? throw new ArgumentNullException(nameof(run));
            this.tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            this.region = region;
            var g = run.g; var b = run.b;
            z15 = HeightOf(g, b, 258.15f);
            rhoAtLayer = new float[run.timeline.NL];
            for (int L = 0; L < rhoAtLayer.Length; L++) rhoAtLayer[L] = Interp(g.zc, b.rho0, L * 1000f);
            run.timeline.FrameAdded += Add;
            for (int i = Math.Max(0, run.timeline.Oldest); i <= run.timeline.Latest; i++) Add(run.timeline.Get(i));
        }

        public void Dispose() { run.timeline.FrameAdded -= Add; }

        /// <summary>Activity at model time t, blended between frames. Before the first or after the last stored frame it holds
        /// the nearest one; attach before the run starts for a complete history.</summary>
        public LightningActivity At(double t)
        {
            float interval = run.timeline.interval;
            int i = (int)Math.Floor(t / interval + 1e-4);
            int a = Nearest(i, -1), b = Nearest(i + 1, +1);
            if (a < 0) return LightningActivity.None;
            if (b < a) b = a;
            double w = b > a ? C.Clamp((t - a * interval) / ((b - a) * interval), 0, 1) : 0;
            Prefix(b);
            Summary sa = frames[a], sb = frames[b];
            double unscaled = C.Lerp(Rate(sa), Rate(sb), w), best = Math.Max(peak[a], peak[b]);
            double rain = LightningPolarity.Ramp(C.Lerp(sa.rainKm2, sb.rainKm2, w), 0, 30), firstRain = 0;
            if (firstRainIndex >= 0 && firstRainIndex <= b)
            {
                double since = t - firstRainIndex * interval;
                firstRain = LightningPolarity.Ramp(since, 0, 360) * (1 - LightningPolarity.Ramp(since, 720, 1800));
            }
            double decay = best > .5 ? LightningPolarity.Ramp(1 - unscaled / best, .3, .9) : 0;
            LightningPolarity.Split(region, C.Lerp(sa.wMax, sb.wMax, w), rain, firstRain, decay, tuning, out float ground, out float positive);
            return new LightningActivity((float)Math.Max(0, unscaled * tuning.rateScale * tuning.brainRateScale), ground, positive);
        }

        // Flashes per minute before the rate scales: McCaul's blend of the two densities, per 5 min, over the window.
        double Rate(in Summary s) =>
            (.95 * tuning.graupelFluxCoefficient * s.graupelFlux + .05 * tuning.columnIceCoefficient * s.columnIce) / 5;

        // The stored frame nearest i, searching in direction step first, then back the other way.
        int Nearest(int i, int step)
        {
            if (frames.Count == 0) return -1;
            i = Math.Max(0, Math.Min(i, frames.Count - 1));
            for (int j = i; j >= 0 && j < frames.Count; j += step) if (frames[j].has) return j;
            for (int j = i - step; j >= 0 && j < frames.Count; j -= step) if (frames[j].has) return j;
            return -1;
        }

        void Prefix(int through)
        {
            float running = prefixValidThrough >= 0 ? peak[prefixValidThrough] : 0;
            for (int i = prefixValidThrough + 1; i <= through && i < frames.Count; i++)
            {
                var s = frames[i];
                if (s.has) running = Math.Max(running, (float)Rate(s));
                if (firstRainIndex < 0 && s.has && s.rainKm2 >= 10) firstRainIndex = i;
                peak[i] = running; prefixValidThrough = i;
            }
        }

        void Add(TimelineFrame f)
        {
            if (f == null || !f.valid || f.q3D == null || f.map2D == null || f.index < 0) return;
            while (frames.Count <= f.index) { frames.Add(default); peak.Add(0); }
            frames[f.index] = Summarise(f, run.timeline);
            // A replayed frame invalidates the running peak and first rain from that frame on.
            if (prefixValidThrough >= f.index) prefixValidThrough = f.index - 1;
            if (firstRainIndex >= f.index) firstRainIndex = -1;
        }

        Summary Summarise(TimelineFrame f, Timeline tl)
        {
            int nl = tl.NL, W = f.winNX, H = f.winNY, nm = tl.NCX * tl.NCY;
            float area = tl.blockSize * tl.blockSize / 1e6f;
            // The 1 km map holds each column's strongest w; scale it by the core's w at -15 °C over the core's peak.
            float coreMax = 0, core15 = Interp(tl.zw, f.wColumn, z15);
            if (f.wColumn != null) foreach (float w in f.wColumn) coreMax = Math.Max(coreMax, w);
            float shape = coreMax > 1 ? Math.Max(0, Math.Min(1, core15 / coreMax)) : 0;
            // q3D layers are centred on 0, 1, ... km: interpolate qh to the -15 °C height.
            int lo = Math.Max(0, Math.Min((int)Math.Floor(z15 / 1000), nl - 1)), hi = Math.Min(lo + 1, nl - 1);
            float fz = Math.Max(0, Math.Min(1, z15 / 1000 - lo));
            double graupel = 0, ice = 0; int rainBlocks = 0;
            for (int j = 0; j < H; j++)
            for (int i = 0; i < W; i++)
            {
                int c = (f.winJ0 + j) * tl.NCX + f.winI0 + i;
                float w15 = Map2D.Decode(Map2D.Field.WMax, f.map2D[(int)Map2D.Field.WMax * nm + c]) * shape;
                float qh15 = (1 - fz) * Q(f, nl, Q3D.Field.Qh, lo, i, j) + fz * Q(f, nl, Q3D.Field.Qh, hi, i, j);
                graupel += w15 * qh15 * 1000;   // m/s x g/kg
                // Column ice: cloud ice and hail/graupel (the timeline does not store snow).
                for (int L = 0; L < nl; L++)
                    ice += rhoAtLayer[L] * (Q(f, nl, Q3D.Field.Qi, L, i, j) + Q(f, nl, Q3D.Field.Qh, L, i, j)) * (L == 0 ? 500 : 1000);
                if (Map2D.Decode(Map2D.Field.Dbz1km, f.map2D[(int)Map2D.Field.Dbz1km * nm + c]) >= 40) rainBlocks++;
            }
            return new Summary { has = true, graupelFlux = (float)(graupel * area), columnIce = (float)(ice * area), wMax = f.wMax, rainKm2 = rainBlocks * area };
        }

        static float Q(TimelineFrame f, int nl, Q3D.Field field, int L, int i, int j)
            => Q3D.Decode(f.q3D[(((int)field * nl + L) * f.winNY + j) * f.winNX + i]);

        // Height of a base-state temperature, T = theta x Exner, m.
        static float HeightOf(SimGrid g, BaseState b, float kelvin)
        {
            for (int k = 1; k < g.NZ; k++)
            {
                float t0 = b.th0[k - 1] * b.pi0[k - 1], t1 = b.th0[k] * b.pi0[k];
                if (t0 > kelvin && t1 <= kelvin) return g.zc[k - 1] + (g.zc[k] - g.zc[k - 1]) * (t0 - kelvin) / (t0 - t1);
            }
            return b.th0[0] * b.pi0[0] <= kelvin ? 0 : g.zc[g.NZ - 1];
        }

        static float Interp(float[] z, float[] v, float at)
        {
            if (z == null || v == null || v.Length == 0) return 0;
            int n = Math.Min(z.Length, v.Length);
            if (at <= z[0]) return v[0];
            for (int k = 1; k < n; k++)
                if (at <= z[k]) return v[k - 1] + (v[k] - v[k - 1]) * (at - z[k - 1]) / (z[k] - z[k - 1]);
            return v[n - 1];
        }
    }
}
