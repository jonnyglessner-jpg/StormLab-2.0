// The storm genome: what makes one designed storm different from the next. The environment decides the storm's
// character (StormCharacter: how big, how tilted, how wet, how rotating, which way it goes); the genome's random draws,
// fixed by a seed, decide its life story (how many cycles, which ones put down tornadoes, how strong, how wide, what
// shape, how long), calibrated against the SPC record (TornadoClimatology). Each cycle is realised from the environment
// the storm is in when that cycle begins, so a storm that moves into richer air can produce a stronger second tornado,
// and one that runs into a cap fades.
using System;
using System.Collections.Generic;

namespace StormLab.Supercell
{
    public enum TornadoShape { Rope, Cone, Stovepipe, Wedge, MultiVortex, Drillbit, ElephantTrunk, DustWhirl }

    /// <summary>What the environment at one place and time says about a storm there. All lengths km, winds m/s.</summary>
    public struct StormCharacter
    {
        public StormKind kind;
        /// <summary>cloud base (LCL) and storm top (EL), km</summary>
        public double zb, el;
        /// <summary>updraft speed, m/s, and updraft radius at cloud base, km</summary>
        public double w, R;
        /// <summary>updraft tilt: horizontal km per km of height, along the 0-6 km shear</summary>
        public Vec2 tilt;
        /// <summary>storm motion (Bunkers right mover) over the ground, m/s</summary>
        public Vec2 motion;
        /// <summary>storm-relative anvil-level wind and 0-1 km inflow, m/s</summary>
        public Vec2 anvilSR, inflowSR;
        /// <summary>-1 (LP) .. 1 (HP); rotation at 1 km and 4 km, m/s</summary>
        public double hpness, vLow, vMid;
        public double cape, cin, lclM, shear6, srh1, stp, ptor, freezeKm;

        public static StormCharacter From(Environment E)
        {
            var S = StormDiag.Make(E);
            var c = new StormCharacter();
            c.kind = !S.forms ? (S.capped ? StormKind.Capped : StormKind.None)
                : S.mode == StormMode.Pulse || S.mode == StormMode.Multicell ? StormKind.Pulse : StormKind.Supercell;
            c.lclM = E.pc.lcl; c.cape = E.pc.cape; c.cin = E.pc.cin;
            c.zb = C.Clamp(E.pc.lcl / 1000, 0.4, 2.8);
            c.el = C.Clamp((E.pc.el ?? 12000) / 1000, 6.5, 16);
            c.w = S.forms ? S.w : 0;
            // Updraft width follows the buoyancy: low CAPE gives a narrow updraft (minisupercell ~1.6 km at 600 J/kg), big
            // CAPE a broad one (~3.5 km at 3000, 3.8 km cap). LP storms are narrower than their CAPE suggests (dry entrainment).
            c.R = C.Clamp(1.4 + 2.4 * C.Clamp((Math.Sqrt(Math.Max(E.pc.cape, 0)) - 20) / 40, 0, 1), 1.4, 3.8) * (S.precip == Precip.LP ? 0.88 : 1);
            // Tilt is the shear relative to the updraft: strong shear on a weak updraft lays the tower over, big CAPE
            // stands it up. 0.22 x shear6 / w: 35 m/s on a 20 m/s updraft = 0.39 km/km, 28 on 53 = 0.12.
            var sh = new Vec2(WindProfile.At(E.w.u, 6000) - E.w.u[0], WindProfile.At(E.w.v, 6000) - E.w.v[0]);
            double shm = Math.Sqrt(sh.x * sh.x + sh.y * sh.y), ta = C.Clamp(0.22 * E.shear6 / Math.Max(c.w, 8), 0.03, 0.45);
            c.tilt = shm > 0.1 ? new Vec2(sh.x / shm * ta, sh.y / shm * ta) : new Vec2(0, 0);
            c.motion = E.bk.rm;
            var an = WindProfile.LayerMean(E.w, 9000, 11000); c.anvilSR = new Vec2(an.x - E.bk.rm.x, an.y - E.bk.rm.y);
            var lo = WindProfile.LayerMean(E.w, 0, 1000); c.inflowSR = new Vec2(lo.x - E.bk.rm.x, lo.y - E.bk.rm.y);
            c.hpness = S.precip == Precip.Ordinary ? 0 : C.Clamp(S.hpness, -1, 1);
            c.vLow = S.vLow; c.vMid = S.vMid;
            c.shear6 = E.shear6; c.srh1 = E.srh1; c.stp = E.stp; c.ptor = S.ptor; c.freezeKm = S.fzl / 1000;
            return c;
        }

        static Vec2 L(Vec2 a, Vec2 b, double t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        /// <summary>Blend toward another character (the kind switches at the half-way point).</summary>
        public static StormCharacter Lerp(in StormCharacter a, in StormCharacter b, double t)
        {
            var c = a;
            c.kind = t < 0.5 ? a.kind : b.kind;
            c.zb = C.Lerp(a.zb, b.zb, t); c.el = C.Lerp(a.el, b.el, t); c.w = C.Lerp(a.w, b.w, t); c.R = C.Lerp(a.R, b.R, t);
            c.tilt = L(a.tilt, b.tilt, t); c.motion = L(a.motion, b.motion, t); c.anvilSR = L(a.anvilSR, b.anvilSR, t); c.inflowSR = L(a.inflowSR, b.inflowSR, t);
            c.hpness = C.Lerp(a.hpness, b.hpness, t); c.vLow = C.Lerp(a.vLow, b.vLow, t); c.vMid = C.Lerp(a.vMid, b.vMid, t);
            c.cape = C.Lerp(a.cape, b.cape, t); c.cin = C.Lerp(a.cin, b.cin, t); c.lclM = C.Lerp(a.lclM, b.lclM, t);
            c.shear6 = C.Lerp(a.shear6, b.shear6, t); c.srh1 = C.Lerp(a.srh1, b.srh1, t); c.stp = C.Lerp(a.stp, b.stp, t);
            c.ptor = C.Lerp(a.ptor, b.ptor, t); c.freezeKm = C.Lerp(a.freezeKm, b.freezeKm, t);
            return c;
        }

        public bool LP => hpness < -0.35;
        public bool HP => hpness > 0.5;
    }

    /// <summary>One tornado: its rating, size, shape and timing. Times are seconds from the storm's initiation.</summary>
    public sealed class TornadoGene
    {
        public int ef; public TornadoShape shape;
        /// <summary>peak 3-s gust, m/s; radius of maximum wind, km; damage-path width, m; path length, km</summary>
        public double vmax, rcKm, widthM, pathKm;
        /// <summary>visible funnel radius at the ground and where it meets the wall cloud, km</summary>
        public double groundKm, topKm;
        /// <summary>profile: 0 slender (stays narrow, flares at the top) .. 1 wedge (wide all the way down)</summary>
        public double profile;
        /// <summary>0 straight .. 1 strongly bent and snaking (ropes, elephant trunks)</summary>
        public double sinuosity;
        /// <summary>how far down the condensation reaches, as a fraction of the cloud-base height (1 = to the ground)</summary>
        public double condReach;
        /// <summary>suction vortices: count, and the fraction of the tornado's life they are visible before the core takes over (MultiVortex: most of it)</summary>
        public int subVortices; public double multiFrac;
        /// <summary>a wide tornado that breaks down into a collar cloud before roping out</summary>
        public bool collar;
        public double formS, lifeS;
        /// <summary>fraction of the life spent roping out</summary>
        public double ropeFrac;
        /// <summary>dust and debris strength (0 .. ~1.5), and how far behind the storm it is left as it ropes out, km</summary>
        public double dust, driftKm;
        /// <summary>0 in the open .. 1 wrapped in rain (HP)</summary>
        public double rainWrap;

        public double EndS => formS + lifeS;
        /// <summary>wind at radius r (km) from the centre, m/s: Rankine vortex with the peak at rc</summary>
        public double WindAt(double rKm) => rKm <= rcKm ? vmax * rKm / Math.Max(rcKm, 1e-3) : vmax * rcKm / rKm;
    }

    /// <summary>One wall-cloud / RFD / tornado cycle. Times are seconds from the storm's initiation.</summary>
    public sealed class CycleGene
    {
        public int index;
        /// <summary>wall cloud forms; the gust front occludes the circulation; the wall cloud is gone</summary>
        public double wallS, occludeS, endS;
        /// <summary>low-level rotation and updraft size relative to the environment's (random walk from cycle to cycle)</summary>
        public double meso, updraft;
        /// <summary>null when this cycle does not produce a tornado</summary>
        public TornadoGene tornado;
        public bool antiTornado;
    }

    /// <summary>The seeded random draws behind one storm. Realise turns them into cycles using the environment at the time.</summary>
    public sealed class StormGenome
    {
        public readonly int seed; public readonly ClimoRegion region;
        /// <summary>how many cycles the storm aims for (1-4), set at initiation from the climatology of cyclic families</summary>
        public int plannedCycles;
        /// <summary>first wall cloud, s after initiation</summary>
        public double firstWallS;
        /// <summary>slow decay after the last cycle, s</summary>
        public double decayS;

        public StormGenome(int seed, ClimoRegion region, in StormCharacter atInitiation)
        {
            this.seed = seed; this.region = region;
            var r = new StormRng(seed, 0);
            // Cycle count: the family-size climatology, tilted by STP (stronger days cycle more), and by updraft
            // strength (a big, long-lived updraft keeps going). Non-tornadic supercells still cycle 1-3 times.
            double stpEff = atInitiation.stp * (0.75 + 0.5 * C.Clamp((atInitiation.w - 15) / 25, 0, 1));
            int n = TornadoClimatology.SampleFamilySize(region, stpEff, r.U());
            // A family counts tornadoes; storms also have cycles that make none, and a strong, well-fed supercell keeps
            // cycling for 2-4 h whether or not it produces.
            double feed = C.Clamp((atInitiation.w - 20) / 25, 0, 1) * C.Clamp((atInitiation.shear6 - 18) / 12, 0, 1);
            if (r.U() < 0.35 + 0.35 * feed) n++;
            if (r.U() < 0.3 * feed) n++;
            plannedCycles = Math.Max(1, Math.Min(4, n));
            // HP storms that wrap their circulation in rain choke on their own outflow sooner
            if (atInitiation.HP && plannedCycles > 2 && r.U() < 0.5) plannedCycles--;
            // first wall cloud 34-66 min after initiation (first tornado typically 45-90 min), sooner with strong low-level shear
            firstWallS = 60 * (r.Range(42, 66) - 8 * C.Clamp((atInitiation.srh1 - 150) / 250, 0, 1));
            decayS = 60 * r.Range(18, 35);
        }

        /// <summary>
        /// Realise cycle k, beginning at wallS, from the character of the storm then. prev is the previous cycle (null for
        /// the first). groundSpeed is the storm's speed over the ground, m/s.
        /// </summary>
        public CycleGene Realise(int k, double wallS, in StormCharacter ch, CycleGene prev, double groundSpeed)
        {
            var r = new StormRng(seed, 100 + k);
            var cyc = new CycleGene { index = k, wallS = wallS };
            // Random walk of low-level rotation and updraft size from cycle to cycle: the next cycle can be stronger,
            // the same or weaker, and the updraft does not have to grow.
            double m0 = prev?.meso ?? 1, u0 = prev?.updraft ?? 1;
            cyc.meso = C.Clamp(m0 * Math.Exp(0.28 * r.N()), 0.55, 1.6);
            cyc.updraft = C.Clamp(u0 * Math.Exp(0.07 + 0.11 * r.N()), 0.8, 1.45);

            // Does this cycle make a tornado? STP's tornado odds for a supercell, raised by this cycle's rotation.
            double ptor = C.Clamp(1.45 * ch.ptor * Math.Pow(cyc.meso, 1.2), 0, 0.93);
            bool torn = ch.kind == StormKind.Supercell && r.U() < ptor;
            double uEf = r.U(), uW = r.U(), uL = r.U(), uV = r.U(), uShape = r.U(), uDelay = r.U(), uAnti = r.U();
            if (!torn)
            {
                cyc.occludeS = wallS + 60 * r.Range(22, 30);
                cyc.endS = cyc.occludeS + 60 * r.Range(5, 9);
                return cyc;
            }

            var t = new TornadoGene();
            // rating from this cycle's environment, with this cycle's rotation boosting or damping it
            double stpEff = ch.stp * cyc.meso * cyc.meso;
            t.ef = TornadoClimatology.SampleEf(region, stpEff, uEf);
            t.widthM = TornadoClimatology.SampleWidthM(region, t.ef, uW);
            t.pathKm = TornadoClimatology.SampleLengthKm(region, t.ef, uL);
            t.vmax = TornadoClimatology.SampleVmax(t.ef, uV);
            // radius of maximum wind: the damage path's edge is where the wind drops to 65 mph (29 m/s), which in a
            // Rankine vortex is at r = rc x vmax / 29, so rc = half-width x 29 / vmax
            t.rcKm = C.Clamp(0.5 * t.widthM / 1000 * 29.0 / t.vmax, 0.012, 0.6);
            // path length / ground speed; brief spin-ups in the record come from all kinds of storm, so a supercell's tornado
            // lasts at least 90 s (EF0-1) or 3 min (EF2+)
            t.lifeS = C.Clamp(t.pathKm * 1000 / Math.Max(groundSpeed, 6), t.ef >= 2 ? 180 : 90, 95 * 60);
            // forms 8-18 min after the wall cloud, sooner with strong low-level shear
            t.formS = wallS + 60 * (r.Range(8, 18) - 4 * C.Clamp((ch.srh1 - 200) / 300, 0, 1)) * (0.85 + 0.15 * uDelay);
            PickShape(t, ch, ref r, uShape);
            t.rainWrap = C.Clamp((ch.hpness - 0.2) / 0.6, 0, 1) * r.Range(0.6, 1.0);
            t.driftKm = r.Range(0.4, 1.4);
            cyc.tornado = t;
            cyc.antiTornado = k >= 1 && ch.stp >= 4 && t.ef >= 2 && uAnti < 0.25;
            // the gust front wraps round as the tornado ropes out; the wall cloud goes a few minutes after it lifts
            cyc.occludeS = t.formS + t.lifeS * (1 - 0.6 * t.ropeFrac);
            cyc.endS = Math.Max(t.EndS + 60 * r.Range(3, 7), cyc.occludeS + 360);
            return cyc;
        }

        /// <summary>When the next cycle's wall cloud forms, after cycle c.</summary>
        public double NextWallS(CycleGene c, in StormCharacter ch)
        {
            var r = new StormRng(seed, 200 + c.index);
            if (c.tornado == null) return c.occludeS - 60 * r.Range(2, 6);   // the new meso forms as the old one occludes
            // the next tornado follows at the climatological interval after this one ends; its wall cloud forms the
            // usual 8-18 min before that, but never before this tornado is half-way through its life
            double gap = 60 * Math.Max(TornadoClimatology.SampleIntervalMin(region, r.U()), 4);
            double nextForm = c.tornado.EndS + gap;
            return Math.Max(nextForm - 60 * r.Range(8, 15), c.tornado.formS + 0.5 * c.tornado.lifeS);
        }

        static void PickShape(TornadoGene t, in StormCharacter ch, ref StormRng r, double u)
        {
            double W = t.widthM;
            // Condensation: the funnel reaches the ground when the pressure drop in the vortex reaches the drop from the
            // ground to the LCL. Low, moist bases (Dixie, HP) condense all the way; high, dry bases (High Plains LP)
            // leave a gap filled by a dust whirl. Stronger vortices pull it lower.
            double cond = C.Clamp(1.3 - (ch.lclM - 700) / 1300, 0.2, 1) + 0.25 * C.Clamp((t.vmax - 45) / 50, 0, 1);
            t.condReach = C.Clamp(cond * r.Range(0.85, 1.1), 0.15, 1);

            // shape weights by width, rating and base height
            double wide = C.Clamp((W - 500) / 700, 0, 1), narrow = C.Clamp((250 - W) / 200, 0, 1), mid = Math.Max(0, 1 - wide - narrow);
            double strong = C.Clamp((t.ef - 1) / 3.0, 0, 1), dry = C.Clamp((ch.lclM - 1300) / 900, 0, 1);
            var wts = new double[8];
            wts[(int)TornadoShape.Rope] = 1.0 * narrow;
            wts[(int)TornadoShape.Drillbit] = 0.7 * narrow + 0.3 * mid * dry;
            wts[(int)TornadoShape.ElephantTrunk] = 0.6 * narrow + 0.25 * mid;
            wts[(int)TornadoShape.Cone] = 0.9 * mid + 0.2 * narrow;
            wts[(int)TornadoShape.Stovepipe] = 0.7 * mid * (0.5 + strong) + 0.2 * wide;
            wts[(int)TornadoShape.Wedge] = 1.4 * wide * (1 - 0.5 * dry);
            wts[(int)TornadoShape.MultiVortex] = 0.5 * wide * (0.3 + strong) + 0.35 * mid * strong + 0.3 * dry * strong;
            wts[(int)TornadoShape.DustWhirl] = 0.8 * C.Clamp((0.45 - t.condReach) / 0.25, 0, 1) * (1 - wide);
            double sum = 0; foreach (var x in wts) sum += x;
            double acc = 0; t.shape = TornadoShape.Cone;
            for (int i = 0; i < 8; i++) { acc += wts[i] / sum; if (u < acc) { t.shape = (TornadoShape)i; break; } }
            ApplyShape(t, ch, ref r);
        }

        /// <summary>Give a tornado another shape (tools, tests and scripted scenarios): its size, profile, suction vortices and dust follow.</summary>
        public static void Reshape(TornadoGene t, TornadoShape shape, in StormCharacter ch, int seed)
        {
            var r = new StormRng(seed, 900);
            t.shape = shape;
            t.condReach = C.Clamp((C.Clamp(1.3 - (ch.lclM - 700) / 1300, 0.2, 1) + 0.25 * C.Clamp((t.vmax - 45) / 50, 0, 1)), 0.15, 1);
            ApplyShape(t, ch, ref r);
        }

        static void ApplyShape(TornadoGene t, in StormCharacter ch, ref StormRng r)
        {
            double W = t.widthM, dry = C.Clamp((ch.lclM - 1300) / 900, 0, 1);
            // visible funnel size: a fraction of the damage-path half-width, by shape (a wedge's condensation is nearly
            // as wide as its damage; a rope's is a thin tube inside it)
            double half = 0.5 * W / 1000;
            switch (t.shape)
            {
                case TornadoShape.Rope:          t.groundKm = 0.35 * half; t.topKm = 2.8 * t.groundKm + 0.05; t.profile = 0.08; t.sinuosity = r.Range(0.5, 0.8); break;
                case TornadoShape.Drillbit:      t.groundKm = 0.4 * half;  t.topKm = 4.0 * t.groundKm + 0.08; t.profile = 0.22; t.sinuosity = r.Range(0.05, 0.25); break;
                case TornadoShape.ElephantTrunk: t.groundKm = 0.5 * half;  t.topKm = 2.6 * t.groundKm + 0.08; t.profile = 0.18; t.sinuosity = r.Range(0.7, 1.0); break;
                case TornadoShape.Cone:          t.groundKm = 0.45 * half; t.topKm = 3.4 * t.groundKm + 0.1;  t.profile = 0.6;  t.sinuosity = r.Range(0.05, 0.2); break;
                case TornadoShape.Stovepipe:     t.groundKm = 0.6 * half;  t.topKm = 1.45 * t.groundKm + 0.06; t.profile = 0.85; t.sinuosity = r.Range(0.0, 0.1); break;
                case TornadoShape.Wedge:         t.groundKm = 0.85 * half; t.topKm = 1.25 * t.groundKm + 0.1; t.profile = 0.95; t.sinuosity = 0.03; break;
                case TornadoShape.MultiVortex:   t.groundKm = 0.75 * half; t.topKm = 1.35 * t.groundKm + 0.1; t.profile = 0.9;  t.sinuosity = 0.05; break;
                default:                         t.groundKm = 0.5 * half;  t.topKm = 3.0 * t.groundKm + 0.08; t.profile = 0.4;  t.sinuosity = r.Range(0.1, 0.35); break;   // dust whirl
            }
            t.groundKm = C.Clamp(t.groundKm, 0.012, 0.95);
            t.topKm = C.Clamp(t.topKm, t.groundKm, 1.4);
            if (t.shape == TornadoShape.DustWhirl) t.condReach = Math.Min(t.condReach, 0.3);

            // suction vortices: the multi-vortex shape keeps them most of its life; big, strong wedges and stovepipes
            // start as multiple vortices before a central vortex takes over (the web Lab's second tornado)
            if (t.shape == TornadoShape.MultiVortex) { t.subVortices = 3 + (int)(r.U() * 4); t.multiFrac = r.Range(0.6, 0.85); }
            else if ((t.shape == TornadoShape.Wedge || t.shape == TornadoShape.Stovepipe) && t.ef >= 2 && r.U() < 0.55) { t.subVortices = 3 + (int)(r.U() * 3); t.multiFrac = r.Range(0.15, 0.3); }
            t.collar = (t.shape == TornadoShape.Wedge || t.shape == TornadoShape.MultiVortex) && t.lifeS > 600 && r.U() < 0.6;
            t.ropeFrac = t.shape == TornadoShape.Rope ? r.Range(0.3, 0.5) : r.Range(0.15, 0.32);
            // dust and debris: more with stronger winds and dry ground (high bases)
            t.dust = C.Clamp((0.45 + 0.2 * t.ef) * (0.8 + 0.5 * dry) * r.Range(0.8, 1.15), 0.2, 1.6);
        }
    }
}
