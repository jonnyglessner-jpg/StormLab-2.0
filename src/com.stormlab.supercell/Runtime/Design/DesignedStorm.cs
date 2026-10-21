// The designed storm in real time: the game's storm when the simulation brain is not running. One storm, from
// initiation to its last anvil, is laid out when it spawns: it walks its track through the environment (sampled every
// 2 min where it is, eased over 10 min so it changes gradually), the genome realises each cycle from the environment of
// the moment, and every feature is then a pure function of time, so playback, the time slider and timelapse all agree.
//
// Times are seconds from initiation. Positions are km over the ground (x east, y north) in the environment's frame;
// Evaluate writes them relative to the skin volume's centre. Everything moves at its real rate: the storm at its Bunkers
// motion, the wall cloud and mesocyclone at v / r of the simulated rotation, the tornado at the speed its rating implies.
//
// The skin's block (SkinParams) is filled exactly as FeatureDesigner fills it from the brain, so the renderer cannot
// tell the two apart. Lifecycle rules follow StormLab 1.0's design.ts (web, 2026-10), generalised to 1-4 cycles and to
// any tornado shape.
using System;
using System.Collections.Generic;
using StormLab.Supercell.Designer;
using UnityEngine;

namespace StormLab.Supercell
{
    /// <summary>Where the storm reads its environment: a fixed sounding, or the day's mesoscale map.</summary>
    public interface IEnvironmentSource
    {
        /// <summary>The environment at (x, y) km at time t, seconds after the storm's initiation.</summary>
        Environment At(double xKm, double yKm, double tS);
    }

    /// <summary>The same environment everywhere and always (a preset or a single sounding).</summary>
    public sealed class FixedEnvironment : IEnvironmentSource
    {
        public readonly Environment E;
        public FixedEnvironment(Environment e) { E = e; }
        public Environment At(double xKm, double yKm, double tS) => E;
    }

    /// <summary>The tornado now, for the game: where it is, how big and how strong.</summary>
    public struct TornadoNow
    {
        public bool on;
        /// <summary>0..1 how established it is (fades in as it forms, out as it lifts)</summary>
        public double amount;
        /// <summary>ground position and the top under the wall cloud, km</summary>
        public Vec2 ground, top;
        /// <summary>visible radius at the ground and at the top, km; radius of maximum wind, km; peak wind m/s</summary>
        public double groundKm, topKm, rcKm, vmax;
        public int ef; public TornadoShape shape; public int cycle;
        /// <summary>0..1 rope-out; 0..1 breakdown into a collar; 0..1 suction-vortex phase; 0..1 its envelope (wedges)</summary>
        public double rope, breakdown, multi, envelope;
        /// <summary>central vortex radius (wedges and multi-vortex), km</summary>
        public double coreKm;
        public double condReach, dust, spin, lifeFrac;
        public TornadoGene gene;
        /// <summary>the shape it has now (its life's phases blended), and while it changes, from and to which shape and how far 0..1</summary>
        public TornadoShape phaseShape, morphFrom, morphTo; public double morph;
        /// <summary>profile and sinuosity now; sidewinder drag of the lower end, km, and its direction over the ground</summary>
        public double profile, sinuosity, sideKm; public Vec2 sideDir;
        /// <summary>satellite tornado: on, where (km over the ground), 0..1 amount, visible radius km, peak wind m/s</summary>
        public bool satOn; public Vec2 satPos; public double satAmount, satRadKm, satVmax;
    }

    public sealed class DesignedStorm
    {
        /// <summary>key frame spacing of the precomputed track, s</summary>
        public const double KeyDt = 30;
        /// <summary>environment sampling interval and the time constant the storm adjusts to it with, s</summary>
        public const double EnvDt = 120, EnvTau = 600;
        /// <summary>longest life the track is laid out for, s</summary>
        public const double MaxLife = 5 * 3600;

        public readonly StormGenome genome;
        public readonly IEnvironmentSource source;
        public readonly Vec2 spawnKm;
        public readonly List<CycleGene> cycles = new List<CycleGene>();
        /// <summary>the kind of storm at initiation (a supercell can still weaken later)</summary>
        public readonly StormKind kind;
        /// <summary>total life, s (the anvil remnant fades by then)</summary>
        public readonly double lifeS;
        /// <summary>1.0's plan at initiation: used for pulse, capped and clear days</summary>
        readonly StormPlan plan0; readonly Environment env0;
        readonly StormFrame f10 = new StormFrame();

        readonly List<Vec2> pos = new List<Vec2>();
        readonly List<StormCharacter> chars = new List<StormCharacter>();

        public DesignedStorm(IEnvironmentSource env, Vec2 spawn, int seed, ClimoRegion region)
        {
            source = env; spawnKm = spawn;
            env0 = env.At(spawn.x, spawn.y, 0);
            var ch0 = StormCharacter.From(env0);
            kind = ch0.kind;
            plan0 = StormDesign.Plan(env0, StormDiag.Make(env0));
            genome = new StormGenome(seed, region, ch0);
            lifeS = Build(ch0);
        }

        // =================================================================== laying out the storm
        double Build(in StormCharacter ch0)
        {
            var p = spawnKm; var smooth = ch0; var raw = ch0;
            double nextWall = genome.firstWallS, hostileSince = double.NaN, end = double.NaN;
            double life = kind == StormKind.Supercell ? MaxLife : kind == StormKind.Pulse ? 75 * 60 : kind == StormKind.Capped ? 4 * 3600 : 0;
            CycleGene last = null;
            for (int i = 0; ; i++)
            {
                double t = i * KeyDt;
                if (i > 0 && Math.Abs(t / EnvDt - Math.Round(t / EnvDt)) < 1e-6)
                {
                    var e = source.At(p.x, p.y, t);
                    raw = ReferenceEquals(e, env0) ? ch0 : StormCharacter.From(e);
                }
                if (i > 0) smooth = StormCharacter.Lerp(smooth, raw, 1 - Math.Exp(-KeyDt / EnvTau));
                pos.Add(p); chars.Add(smooth);
                if (t >= life || t >= MaxLife) break;

                if (kind == StormKind.Supercell && double.IsNaN(end))
                {
                    // a supercell that moves into air that can no longer feed it (capped, stable, or too little shear)
                    // stops cycling 15 min later and decays
                    bool hostile = raw.kind != StormKind.Supercell;
                    if (hostile && double.IsNaN(hostileSince)) hostileSince = t; else if (!hostile) hostileSince = double.NaN;
                    bool dying = !double.IsNaN(hostileSince) && t - hostileSince > 900;
                    if (!dying && t >= nextWall && cycles.Count < genome.plannedCycles)
                    {
                        var c = genome.Realise(cycles.Count, t, smooth, last, Mag(smooth.motion));
                        cycles.Add(c); last = c;
                        nextWall = genome.NextWallS(c, smooth);
                    }
                    // once it is dying, the cycle under way winds down within 5-10 min: its tornado lifts, the wall cloud goes
                    if (dying && last != null && last.endS > t + 600)
                    {
                        var tg = last.tornado;
                        if (tg != null && tg.formS > t + 120) last.tornado = null;
                        else if (tg != null && tg.EndS > t + 300) tg.lifeS = Math.Max(t + 300 - tg.formS, 90);
                        last.occludeS = Math.Min(last.occludeS, t + 300); last.endS = t + 600;
                    }
                    double lastEnd = last != null ? Math.Max(last.endS, last.tornado?.EndS ?? 0) : double.NaN;
                    if (dying && (last == null || t > lastEnd)) end = t;
                    else if (cycles.Count >= genome.plannedCycles && last != null && t > lastEnd) end = lastEnd;
                    if (!double.IsNaN(end)) life = Math.Min(MaxLife, end + genome.decayS);
                }
                var m = smooth.motion; p = new Vec2(p.x + m.x * KeyDt / 1000, p.y + m.y * KeyDt / 1000);
            }
            if (kind == StormKind.Supercell && double.IsNaN(end)) life = MaxLife;
            return life;
        }

        static double Mag(Vec2 v) => Math.Sqrt(v.x * v.x + v.y * v.y);
        static double Ramp(double t, double a, double b) { double x = C.Clamp((t - a) / (b - a), 0, 1); return x * x * (3 - 2 * x); }

        void Key(double t, out int i, out double f)
        {
            double k = C.Clamp(t / KeyDt, 0, pos.Count - 1.000001);
            i = (int)Math.Floor(k); f = k - i;
            if (i >= pos.Count - 1) { i = Math.Max(pos.Count - 2, 0); f = pos.Count > 1 ? 1 : 0; }
        }
        /// <summary>The updraft at cloud base, km over the ground.</summary>
        public Vec2 UpdraftAt(double t)
        {
            if (pos.Count == 1) return pos[0];
            Key(t, out int i, out double f); var a = pos[i]; var b = pos[i + 1];
            var q = new Vec2(a.x + (b.x - a.x) * f, a.y + (b.y - a.y) * f);
            if (t > (pos.Count - 1) * KeyDt) { var m = chars[chars.Count - 1].motion; double dt = t - (pos.Count - 1) * KeyDt; q = new Vec2(b.x + m.x * dt / 1000, b.y + m.y * dt / 1000); }
            return q;
        }
        /// <summary>What the environment says about the storm at time t (eased along its track).</summary>
        public StormCharacter CharacterAt(double t)
        {
            if (chars.Count == 1) return chars[0];
            Key(t, out int i, out double f); return StormCharacter.Lerp(chars[i], chars[i + 1], f);
        }
        public Vec2 MotionAt(double t) => CharacterAt(t).motion;
        /// <summary>The environment the storm is in at time t (its wind profile drives the kinematic wind).</summary>
        public Environment EnvironmentAt(double t) { var p = UpdraftAt(t); return source.At(p.x, p.y, t); }
        /// <summary>South-west and north-east corners of the ground the storm covers over its life, km (updraft track +- margin).</summary>
        public void TrackBounds(double marginKm, out Vec2 min, out Vec2 max)
        {
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            foreach (var p in pos) { x0 = Math.Min(x0, p.x); y0 = Math.Min(y0, p.y); x1 = Math.Max(x1, p.x); y1 = Math.Max(y1, p.y); }
            min = new Vec2(x0 - marginKm, y0 - marginKm); max = new Vec2(x1 + marginKm, y1 + marginKm);
        }

        // =================================================================== the tornado
        /// <summary>Precipitation-mode scale: classic value c, LP value lp, HP value hp, blended by hpness.</summary>
        static double Mode(double hpness, double lp, double c, double hp) => hpness < 0 ? C.Lerp(c, lp, C.Clamp(-hpness / 0.6, 0, 1)) : C.Lerp(c, hp, C.Clamp(hpness / 0.8, 0, 1));

        struct CycleGeom { public Vec2 wc, rcB, curlS, curlN; public double Rs, Rw, wallAmt, lower, slot, slotR, rfdAmt, horse, occ, big; }

        /// <summary>Wall cloud, RFD surge and horseshoe of cycle c at time t (positions km over the ground).</summary>
        CycleGeom Geom(CycleGene c, double t, in StormCharacter ch, Vec2 up, Vec2 fwd, Vec2 lft, double towerR)
        {
            var g = new CycleGeom();
            double tau = t - c.wallS, hp = ch.hpness;
            double meso = C.Clamp(ch.vLow / 20, 0.3, 1.2) * c.meso;
            g.occ = Ramp(t, c.occludeS - 300, c.occludeS + 420);
            double fade = 1 - Ramp(t, c.endS - 360, c.endS);
            g.Rw = C.Clamp(1.4 + 1.1 * meso, 1.2, 2.9) * Mode(hp, 0.9, 1, 1.2);
            g.wallAmt = Ramp(tau, 0, 420) * fade;
            // the wall cloud hangs 15-33 % of the base height below it (Plains: 200-450 m under a 1.2-1.5 km base), more with
            // stronger rotation: a 1.0-style fixed lowering would leave only a stub of tornado under a low base
            g.lower = ch.zb * C.Clamp(0.15 + 0.15 * meso, 0.15, 0.33) * Ramp(tau, 0, 540) * fade;
            // the RFD surge comes 4-6 min before the tornado (or 8 min into a cycle that makes none)
            var tg = c.tornado;
            double rfd0 = tg != null ? Math.Max(240, tg.formS - c.wallS - 360) : 480;
            // a wide tornado comes with a broad surge and horseshoe; a rope with a tight one
            g.big = tg != null ? C.Clamp((tg.groundKm - 0.12) / 0.5, 0, 1) : 0;
            g.slot = Mode(hp, 0.8, 1, 0.45) * Ramp(tau, rfd0, rfd0 + 540) * fade;
            g.slotR = (g.Rw + 0.8 + 2.2 * Ramp(tau, rfd0 + 240, rfd0 + 1320)) * (1 + 0.8 * g.big);
            g.rfdAmt = Mode(hp, 0.25, 0.9, 1.4) * Ramp(tau, rfd0 - 60, rfd0 + 420);
            g.horse = g.slot * Ramp(tau, rfd0 + 120, rfd0 + 600);
            Vec2 At2(double f, double l) => new Vec2(up.x + f * fwd.x + l * lft.x, up.y + f * fwd.y + l * lft.y);
            Vec2 Dir(double th) => new Vec2(Math.Cos(th) * fwd.x + Math.Sin(th) * lft.x, Math.Cos(th) * fwd.y + Math.Sin(th) * lft.y);
            // the wall cloud forms under the rear-right of the updraft base, then occludes rearward
            var wcRaw = At2(-1.0 - 2.2 * g.occ, -0.8 + 1.4 * g.occ);
            // the surge, seen from below, is a horseshoe closed on the inflow side and open to the rear; the wall cloud and
            // tornado sit on the inner side of its northern arm (+60 deg), the anticyclonic curl at its southern tip
            // The anchor (northern curl: wall cloud and tornado) is placed from the cycle's mature horseshoe and updraft, so
            // it stays put relative to the updraft while the surge grows: the tornado then travels at the storm's motion,
            // and only moves relative to it as the storm occludes. The drawn surge radius grows from 20 % to full.
            double slotGrowth = Ramp(tau, rfd0, rfd0 + 600), sc = (0.85 + 0.15 * c.meso) * (1 + 0.9 * g.big);
            double towerM = ch.R * c.updraft * 1.25;
            var dn = Dir(60 * Math.PI / 180); var ds = Dir(-170 * Math.PI / 180);
            double Rm = Math.Min(g.Rw * sc * 0.748, Math.Min(towerM * 0.462 * sc, Math.Max(towerM * 0.9 - 0.85, 0.3)));
            var rc = new Vec2(wcRaw.x - 0.88 * Rm * dn.x, wcRaw.y - 0.88 * Rm * dn.y);
            // keep the whole rim under the main updraft, moving the northern-curl anchor with it
            double dx = rc.x - up.x, dy = rc.y - up.y, reach = Math.Max(towerM * 0.9 - Rm - 0.65, 0.15);
            double fit = Math.Min(1, reach / Math.Max(Math.Sqrt(dx * dx + dy * dy), 1e-3));
            rc = new Vec2(up.x + dx * fit, up.y + dy * fit);
            g.wc = new Vec2(rc.x + 0.88 * Rm * dn.x, rc.y + 0.88 * Rm * dn.y);
            double R = Rm * (0.55 + 0.198 * Ramp(tau, rfd0 + 60, rfd0 + 960)) / 0.748 * (0.2 + 0.8 * slotGrowth);
            // the surge's centre sits back from the fixed anchor by its current radius
            rc = new Vec2(g.wc.x - 0.88 * R * dn.x, g.wc.y - 0.88 * R * dn.y);
            g.Rs = R; g.rcB = rc;
            g.curlN = g.wc; g.curlS = new Vec2(rc.x + R * ds.x, rc.y + R * ds.y);
            return g;
        }

        TornadoNow TornadoOf(CycleGene c, double t, in CycleGeom g, Vec2 fwd, Vec2 lft)
        {
            var o = new TornadoNow(); var tg = c.tornado;
            if (tg == null) return o;
            double tau = t - tg.formS, L = tg.lifeS;
            if (tau < 0 || tau > L) return o;
            o.gene = tg; o.cycle = c.index; o.ef = tg.ef; o.shape = tg.shape;
            double formT = C.Clamp(0.08 * L, 45, 150), fadeT = C.Clamp(0.07 * L, 40, 180);
            double form = Ramp(tau, 0, formT), die = Ramp(tau, L - fadeT, L);
            o.amount = form * (1 - die); o.on = o.amount > 0.02;
            o.lifeFrac = tau / L;
            // its life story: the phases blended in turn, each over MorphFrac of the life
            double pg = tg.groundKm, pt = tg.topKm, pc = 1, psd = 0;
            o.profile = tg.profile; o.sinuosity = tg.sinuosity; o.phaseShape = o.morphFrom = o.morphTo = tg.shape;
            var ph = tg.phases;
            if (ph != null && ph.Length > 0)
            {
                pg = ph[0].groundKm; pt = ph[0].topKm; o.profile = ph[0].profile; o.sinuosity = ph[0].sinuosity; pc = ph[0].cond; psd = ph[0].sideKm;
                o.phaseShape = ph[0].shape;
                for (int i = 1; i < ph.Length; i++)
                {
                    double w = Ramp(o.lifeFrac, ph[i].startFrac, ph[i].startFrac + TornadoGene.MorphFrac);
                    if (w <= 0) continue;
                    pg = C.Lerp(pg, ph[i].groundKm, w); pt = C.Lerp(pt, ph[i].topKm, w);
                    o.profile = C.Lerp(o.profile, ph[i].profile, w); o.sinuosity = C.Lerp(o.sinuosity, ph[i].sinuosity, w);
                    pc = C.Lerp(pc, ph[i].cond, w); psd = C.Lerp(psd, ph[i].sideKm, w);
                    if (w < 1) { o.morphFrom = ph[i - 1].shape; o.morphTo = ph[i].shape; o.morph = w; }
                    if (w >= 0.5) o.phaseShape = ph[i].shape;
                }
            }
            // rope-out takes its fraction of the life, but no more than 12 min
            double ropeS = Math.Min(tg.ropeFrac * L, 720);
            o.rope = Ramp(tau, L - ropeS, L - 0.15 * ropeS);
            // wide tornadoes: suction vortices first, then a central vortex takes over and the envelope grows to full
            // width; some break down into a collar cloud before the central vortex ropes out (StormLab web, second tornado)
            bool big = tg.shape == TornadoShape.Wedge || tg.shape == TornadoShape.MultiVortex || tg.multiFrac > 0;
            double takeover = tg.shape == TornadoShape.MultiVortex ? 0.35 : Ramp(tau, 0.12 * L, Math.Max(0.45, tg.multiFrac + 0.15) * L);
            double grow = big ? (0.3 + 0.7 * takeover) : (0.6 + 0.4 * form);
            o.groundKm = pg * grow * (1 - 0.75 * o.rope);
            o.topKm = (pt * (big ? (0.5 + 0.5 * takeover) : 1)) * (1 - 0.6 * o.rope);
            o.multi = tg.subVortices > 0 ? Ramp(tau, 0.02 * L, 0.06 * L) * (1 - Ramp(tau, tg.multiFrac * L * 0.75, tg.multiFrac * L)) : 0;
            o.breakdown = tg.collar ? Ramp(tau, 0.68 * L, 0.78 * L) : 0;
            o.envelope = big ? (tg.shape == TornadoShape.MultiVortex ? 0.35 + 0.25 * takeover : takeover) : 0;
            o.coreKm = big ? Math.Max(tg.rcKm * 1.6, 0.04) * Ramp(tau, 0.05 * L, 0.2 * L) * (1 - 0.5 * o.rope) : 0;
            o.rcKm = tg.rcKm * (1 - 0.5 * o.rope);
            o.vmax = tg.vmax * (0.55 + 0.45 * form) * (1 - 0.5 * o.rope) * (1 - 0.4 * o.breakdown);
            o.condReach = Math.Min(tg.condReach * pc, 1) * Ramp(tau, 0, formT * 1.4) * (1 - 0.45 * Ramp(tau, L - 2 * fadeT, L));
            o.dust = tg.dust * Ramp(tau, 20, 100) * (1 - Ramp(tau, L - 0.3 * ropeS, L)) * (1 - 0.35 * o.breakdown);
            // The funnel's surface turns at the wind there: v(r) / r. Inside the core (r < rc) that is vmax / rc; outside,
            // vmax rc / r^2. Capped so the striations do not alias at 60 frames a second.
            double rv = Math.Max(o.groundKm, 0.01);
            o.spin = C.Clamp(tg.WindAt(rv) / (rv * 1000), 0.05, 2.0) * (0.6 + 0.4 * form);
            // the tornado hangs from the northern curl; as it ropes out it is left behind the occluding storm
            o.top = g.curlN;
            double drift = tg.driftKm * o.rope;
            // its lean (fixed relative to the storm's motion, more as it ropes out), then the rope-out drift
            Vec2 Dir(double a) => new Vec2(Math.Cos(a) * fwd.x + Math.Sin(a) * lft.x, Math.Cos(a) * fwd.y + Math.Sin(a) * lft.y);
            var ld = Dir(tg.leanAngle); double lean = tg.leanKm * (1 + 0.8 * o.rope);
            o.ground = new Vec2(o.top.x + (-fwd.x * 0.8 - lft.x * 0.6) * drift + ld.x * lean, o.top.y + (-fwd.y * 0.8 - lft.y * 0.6) * drift + ld.y * lean);
            o.sideKm = psd; o.sideDir = Dir(tg.sideAngle);
            // satellite: forms at satFrac of the parent's life, orbits it counter-clockwise at about 15 m/s, lives a few minutes
            if (tg.satellite)
            {
                double ts = tau - tg.satFrac * L;
                double sa = Ramp(ts, 0, 25) * (1 - Ramp(ts, tg.satLifeS - 40, tg.satLifeS)) * (1 - o.rope);
                if (ts >= 0 && ts <= tg.satLifeS && sa > 0.02)
                {
                    double ang = tg.satAngle + ts * 15 / (tg.satOrbitKm * 1000);
                    o.satOn = true; o.satAmount = sa * o.amount; o.satRadKm = tg.satRadKm; o.satVmax = tg.satVmax * sa;
                    o.satPos = new Vec2(o.ground.x + tg.satOrbitKm * Math.Cos(ang), o.ground.y + tg.satOrbitKm * Math.Sin(ang));
                }
            }
            return o;
        }

        /// <summary>The tornado at time t (the strongest one if two overlap), in km over the ground.</summary>
        public TornadoNow TornadoAt(double t)
        {
            Frame(t, out var ch, out var up, out var fwd, out var lft, out double towerR);
            var best = new TornadoNow();
            foreach (var c in cycles)
            {
                if (c.tornado == null || t < c.tornado.formS || t > c.tornado.EndS) continue;
                var g = Geom(c, t, ch, up, fwd, lft, towerR);
                var o = TornadoOf(c, t, g, fwd, lft);
                if (o.amount > best.amount) best = o;
            }
            return best;
        }

        /// <summary>Ground-relative wind of the tornado at a point (km), m/s, horizontal: rotation (Rankine, counter-clockwise) + the storm's motion.</summary>
        public Vector2 TornadoWind(double t, Vec2 pKm)
        {
            var o = TornadoAt(t); var m = MotionAt(t);
            if (!o.on) return Vector2.zero;
            double dx = pKm.x - o.ground.x, dy = pKm.y - o.ground.y, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > 5 * Math.Max(o.groundKm, o.rcKm) + 0.3 + (o.satOn ? o.gene.satOrbitKm : 0)) return Vector2.zero;
            double v = o.amount * (r <= o.rcKm ? o.vmax * r / Math.Max(o.rcKm, 1e-3) : o.vmax * o.rcKm / r);
            double s = r > 1e-6 ? v / r : 0;
            // swirl plus the inflow spiralling in (about a third of the swirl near the ground)
            double wx = -dy * s - 0.33 * dx * s + m.x * o.amount, wy = dx * s - 0.33 * dy * s + m.y * o.amount;
            if (o.satOn)
            {
                // the satellite's own swirl (its radius of maximum wind about its visible radius)
                double sx = pKm.x - o.satPos.x, sy = pKm.y - o.satPos.y, sr = Math.Sqrt(sx * sx + sy * sy), rc = Math.Max(o.satRadKm, 0.01);
                double sv = sr <= rc ? o.satVmax * sr / rc : o.satVmax * rc / sr, ss = sr > 1e-6 ? sv / sr : 0;
                wx += -sy * ss; wy += sx * ss;
            }
            return new Vector2((float)wx, (float)wy);
        }

        void Frame(double t, out StormCharacter ch, out Vec2 up, out Vec2 fwd, out Vec2 lft, out double towerR)
        {
            ch = CharacterAt(t); up = UpdraftAt(t);
            double m = Mag(ch.motion); fwd = m > 0.5 ? new Vec2(ch.motion.x / m, ch.motion.y / m) : new Vec2(1, 0); lft = new Vec2(-fwd.y, fwd.x);
            double tm = t / 60;
            towerR = ch.R * UpdraftScale(t) * (0.8 + 0.2 * Ramp(tm, 4, 20) + 0.25 * Ramp(tm, 16, 44));
        }

        /// <summary>The cycles' updraft size, eased over 20 min around each new wall cloud.</summary>
        double UpdraftScale(double t)
        {
            double s = 1;
            foreach (var c in cycles) s = C.Lerp(s, c.updraft, Ramp(t, c.wallS - 600, c.wallS + 600));
            return s;
        }

        // =================================================================== the skin's parameter block
        /// <summary>
        /// Fill the skin's parameter block at time t (s after initiation). clock = wall-clock seconds; centreKm = the
        /// volume centre, km over the ground (positions are written relative to it).
        /// </summary>
        public void Evaluate(double t, double clock, Vec2 centreKm, SkinParams P)
        {
            P.Clear();
            Frame(t, out var ch, out var up, out var fwd, out var lft, out double towerR);
            double tm = t / 60, cx = centreKm.x, cy = centreKm.y;
            void SetP(Slot s, Vec2 p, double z, double w) => P.Set(s, (float)(p.x - cx), (float)(p.y - cy), (float)z, (float)w);
            void Set(Slot s, double x, double y, double z, double w) => P.Set(s, (float)x, (float)y, (float)z, (float)w);
            Vec2 At2(double f, double l) => new Vec2(up.x + f * fwd.x + l * lft.x, up.y + f * fwd.y + l * lft.y);
            double anm = Mag(ch.anvilSR); var adir = anm > 2 ? new Vec2(ch.anvilSR.x / anm, ch.anvilSR.y / anm) : fwd;
            double hp = ch.hpness;
            int pm = hp < -0.35 ? 0 : hp > 0.5 ? 2 : 1;
            P.suggestedCentre = new Vector2((float)((up.x + adir.x * 9) * 1000), (float)((up.y + adir.y * 9) * 1000));

            Set(Slot.F, fwd.x, fwd.y, lft.x, lft.y);
            Set(Slot.Motion, ch.motion.x, ch.motion.y, 0, 0);
            Set(Slot.Env, ch.lclM / 1000, ch.el, ch.cape / 1000, ch.kind == StormKind.Supercell ? pm : -1);
            Set(Slot.Curtain, C.Clamp(0.7 + 0.7 * hp, 0.3, 1.2), C.Clamp(0.6 - 0.4 * hp, 0.25, 0.85), C.Clamp(0.5 - 0.6 * hp, 0, 1), hp);
            P.fromFallback = Feature.Designed;

            if (kind != StormKind.Supercell)
            {
                // pulse, capped and clear days: 1.0's rules, placed at the updraft
                StormDesign.At(plan0, tm, clock, f10);
                var a = f10.p; var up1 = new Vec2(a[16 * 4], a[16 * 4 + 1]);
                bool[] isPos = { false, false, false, false, false, false, true, false, false, true, true, false, false, true, false, true, true, false, true, true, false };
                for (int s = 0; s < StormDesign.NSLOT; s++)
                {
                    float x = a[s * 4], y = a[s * 4 + 1];
                    if (isPos[s]) { x += (float)(up.x - up1.x - cx); y += (float)(up.y - up1.y - cy); }
                    P.v[s] = new Vector4(x, y, a[s * 4 + 2], a[s * 4 + 3]);
                }
                P.stage = f10.stage; P.caption = f10.caption;
                Set(Slot.Time, t, lifeS, 0, 0);
                return;
            }

            // ---- whole-storm life
            double live = 1 - Ramp(t, lifeS - genome.decayS, lifeS - 0.25 * genome.decayS);
            double towerTop = ch.zb + 1 + (ch.el + 0.4 - ch.zb - 1) * Ramp(tm, 0, 14);
            towerTop = C.Lerp(ch.zb + 0.8, towerTop, 0.4 + 0.6 * live);
            double overshoot = Ramp(tm, 14, 20) * (0.7 + 0.3 * Math.Sin(tm * 0.9)) * C.Clamp(ch.w / 35, 0.4, 1.3) * live;
            double anvilAmt = Ramp(tm, 14, 24) * (1 - 0.5 * Ramp(t, lifeS - 0.5 * genome.decayS, lifeS));
            double anvilLen = Math.Min(5 + C.Clamp(26 + anm, 20, 52) * Ramp(tm, 14, 55), 52);
            double back = Math.Min(ch.R * (0.8 + 2.2 * Ramp(tm, 16, 40)), 12);
            double mamm = 0.8 * Ramp(tm, 32, 48);
            double striation = Mode(hp, 1, 0.65, 0.3) * C.Clamp(ch.vMid / 18, 0.4, 1.2);
            double strat = striation * Ramp(tm, 20, 32) * live;
            double ffdAmt = Mode(hp, 0.35, 1, 1.5) * Ramp(tm, 14, 24) * (0.3 + 0.7 * live);
            double hailAmt = Mode(hp, 0.6, 1, 1.1) * C.Clamp(ch.w / 30, 0.4, 1.3) * Ramp(tm, 18, 28) * live;
            double tailAmt = Mode(hp, 0.6, 1, 0.85) * Ramp(tm, 24, 34) * live;
            double flankAmt = Ramp(tm, 18, 30) * live;
            // mid-level rotation turns at its real rate: v / r of the 4 km mesocyclone
            double mesoSpin = C.Clamp(ch.vMid / (towerR * 1000), 0.002, 0.03) * Ramp(tm, 16, 30) * live;
            double towerAmt = 0.25 + 0.75 * live;

            Set(Slot.Base, ch.zb, ch.el, clock, tm);
            Set(Slot.Tower, towerTop, towerR, towerAmt, strat);
            Set(Slot.Tilt, ch.tilt.x, ch.tilt.y, overshoot, mesoSpin);
            Set(Slot.Anvil, adir.x, adir.y, anvilLen, anvilAmt);
            Set(Slot.Anvil2, ch.el - 2.4, ch.el + 0.25, mamm, back);
            // live 1.0: the barrel widens at mid levels as the storm organises, the anvil thins to a wide veil of ice after the
            // first cycle, and the flanking line matures into bigger towers
            SetP(Slot.Up, up, Ramp(tm, 12, 44), Ramp(tm, 68, 93) * anvilAmt);
            Set(Slot.Late, Ramp(tm, 68, 90) * live, 0, 0, 0);
            var otC = new Vec2(up.x + ch.tilt.x * (ch.el - ch.zb), up.y + ch.tilt.y * (ch.el - ch.zb));
            SetP(Slot.Ot, otC, overshoot * 1.4, towerR * 0.75);
            double anvilAge = Math.Max(0, tm - 14);
            Set(Slot.Anvil3, ch.anvilSR.x, ch.anvilSR.y, anvilAge, C.Clamp(anvilAmt * Ramp(tm, 14, 60), 0, 1));
            SetP(Slot.Mamm, new Vec2(up.x + adir.x * (anvilLen * 0.3 + 4), up.y + adir.y * (anvilLen * 0.3 + 4)), anvilLen * 0.22, 0.25 + 0.35 * mamm);
            int plates = (int)Math.Round(C.Lerp(3, 7, C.Clamp((strat - 0.15) / 0.75, 0, 1)));
            double hb = C.Clamp(0.25 * plates + 0.2, 1.2, 1.75);
            Set(Slot.Plates, plates, hb / plates, hb, C.Clamp(1.7 * strat, 0, 1));

            // ---- forward flank, hail, inflow, vault, flanking line
            double ffdP = Mode(hp, 0.35, 1, 1.5);
            SetP(Slot.Ffd, At2(ffdP < 0.5 ? 9 : 6, ffdP < 0.5 ? 6 : 4.5), ffdP > 1.2 ? 1.35 : 1, ffdAmt);
            SetP(Slot.Hail, At2(2.8, 2.2), 2.0, hailAmt);
            double inSpd = Mag(ch.inflowSR);
            var tdir = inSpd > 1 ? new Vec2(-ch.inflowSR.x / inSpd, -ch.inflowSR.y / inSpd) : Unit(fwd.x - 0.7 * lft.x, fwd.y - 0.7 * lft.y);
            double rh = C.Clamp(1 - (ch.lclM - 500) / 2500, 0, 1);
            Set(Slot.Tail, tdir.x, tdir.y, C.Clamp(6 + 0.4 * inSpd, 6, 16), tailAmt);
            Set(Slot.Inflow, ch.inflowSR.x, ch.inflowSR.y, inSpd, rh);
            int bands = 1 + (inSpd > 12 ? 1 : 0) + (rh > 0.65 ? 1 : 0);
            Set(Slot.Tail2, bands, 0.35 + 0.1 * bands, 0.2 + 0.4 * rh, 0.035);
            double vd = C.Clamp(1.5 + ch.w / 12, 1.5, 6);
            Set(Slot.Vault, tdir.x, tdir.y, vd, Ramp(tm, 18, 30) * Ramp(vd, 0.5, 3) * live);
            var flank = Unit(-fwd.x - 0.3 * lft.x, -fwd.y - 0.3 * lft.y);
            Set(Slot.Flank, flank.x, flank.y, flankAmt, 6);
            SetP(Slot.Flank2, up, C.Clamp(8 + 0.2 * tm, 8, 22), flankAmt);
            // the cold pool spreads behind the updraft as the rain falls: colder under HP storms, warmer under LP
            double cpR = C.Clamp(2 + 0.12 * Math.Max(tm - 18, 0), 0, 16) * Ramp(tm, 18, 30);
            SetP(Slot.Gust, At2(-0.5 * cpR + 3, 0.3 * cpR), cpR, Mode(hp, 2, 4, 6) * Ramp(tm, 18, 40));

            // ---- the cycles: wall cloud, RFD clear slot and horseshoe, tornado
            CycleGene wallC = null; CycleGeom wallG = default; double bestWall = -1;
            var tor = new TornadoNow(); CycleGeom torG = default; double antiAmt = 0, antiTor = 0; CycleGeom antiG = default;
            foreach (var c in cycles)
            {
                if (t < c.wallS - 1 || t > Math.Max(c.endS, c.tornado?.EndS ?? 0) + 60) continue;
                var g = Geom(c, t, ch, up, fwd, lft, towerR);
                double score = g.wallAmt + g.slot;
                if (score > bestWall) { bestWall = score; wallC = c; wallG = g; }
                var o = TornadoOf(c, t, g, fwd, lft);
                if (o.amount > tor.amount) { tor = o; torG = g; }
                double tau = t - c.wallS;
                double am = (c.tornado != null && ch.srh1 > 150 ? C.Clamp(ch.srh1 / 300, 0.4, 1) : 0) * Ramp(tau, 840, 1140) * (1 - Ramp(tau, 1620, 1920));
                if (am > antiAmt) { antiAmt = am; antiG = g; }
                if (c.antiTornado && c.tornado != null)
                {
                    double ta = t - (c.tornado.formS + 0.45 * c.tornado.lifeS);
                    double at = Ramp(ta, 0, 90) * (1 - Ramp(ta, 200, 330));
                    if (at > antiTor) { antiTor = at; antiG = g; }
                }
            }
            double wallSpin = 0, Rw = 1.6, rfdWrap = Mode(hp, 0, 0, 0.75) * C.Clamp((hp - 0.3) / 0.5, 0, 1);
            if (wallC != null)
            {
                var g = wallG; Rw = g.Rw;
                // the wall cloud turns at the low-level mesocyclone's v / r (real time: one turn in a few minutes)
                wallSpin = C.Clamp(ch.vLow * wallC.meso / (g.Rw * 1000 * 0.6), 0.004, 0.06);
                double tailCloud = g.wallAmt * Ramp(t - wallC.wallS, 240, 600);
                SetP(Slot.Wall, g.wc, g.Rw, g.wallAmt);
                Set(Slot.Wall2, g.lower, wallSpin, tailCloud, g.wallAmt * 0.8);
                Set(Slot.Rfd, g.slot, g.slotR, g.rfdAmt, rfdWrap);
                SetP(Slot.Rfdb, g.rcB, g.Rs, g.horse);
                double dry = C.Clamp((ch.lclM - 600) / 1200, 0, 1) * Mode(hp, 1, 0.8, 0.4);
                SetP(Slot.Slot2, g.rcB, dry, g.slotR);
                double hook = Mode(hp, 0.15, 0.6, 1.3) * g.rfdAmt;
                SetP(Slot.RfdCore, new Vec2(g.rcB.x - 0.6 * g.Rs * lft.x, g.rcB.y - 0.6 * g.Rs * lft.y), C.Clamp(0.8 + g.Rs, 0.8, 4), hook);
                SetP(Slot.Meso1, g.wc, g.Rw * 1.3, 2 * wallSpin * g.wallAmt);
                Set(Slot.MesoV, ch.zb, Math.Min(ch.el - 2, ch.zb + 6), wallSpin * g.wallAmt, C.Lerp(8, -3, g.occ));
            }
            else Set(Slot.MesoV, ch.zb, Math.Min(ch.el - 2, ch.zb + 6), 0, 20);
            SetP(Slot.Meso4, new Vec2(up.x + ch.tilt.x * 4, up.y + ch.tilt.y * 4), towerR, 2 * mesoSpin);
            SetP(Slot.Anti, antiG.curlS, antiAmt, antiTor);

            if (tor.on)
            {
                var gn = tor.gene;
                SetP(Slot.Tor, tor.ground, tor.amount, tor.profile);
                Set(Slot.Tor2, Math.Max(tor.groundKm, 0.008), Math.Max(tor.topKm, tor.groundKm), tor.condReach, tor.rope);
                SetP(Slot.Tor3, tor.top, tor.dust, tor.ef);
                bool plain = tor.envelope <= 0 && tor.multi <= 0;
                Set(Slot.Tor4, tor.coreKm, tor.multi, tor.breakdown, plain ? 0 : 1 + tor.envelope);
                Set(Slot.Tor5, 0.9, Math.Max(0.07 * tor.groundKm / 0.8, 0.025), gn.subVortices, tor.sinuosity);
                Set(Slot.Tor6, tor.spin, tor.vmax, tor.rcKm, tor.envelope > 0.3 ? 1.35 : 2.4);
                Set(Slot.Tor7, gn.rainWrap, (int)tor.phaseShape, tor.ef, tor.lifeFrac);
                Set(Slot.Tor8, gn.albedo, gn.roughness, gn.ghost, gn.striation);
                Set(Slot.Tor9, gn.soilR, gn.soilG, gn.soilB, gn.dirt * Ramp(tor.lifeFrac, 0.02, 0.15));
                // the plane the funnel bends in: across the lean, so a rope's S curve and its lean are not the same bend
                double bendA = Math.Atan2(fwd.y, fwd.x) + gn.leanAngle + Math.PI / 2;
                Set(Slot.Tor10, tor.sideDir.x, tor.sideDir.y, tor.sideKm, bendA);
                if (tor.satOn) SetP(Slot.Tor11, tor.satPos, tor.satAmount, tor.satRadKm);
                P.fromFallback |= Feature.Tornado;
            }

            // ---- stage and caption
            Stage(P, t, tm, ch, wallC, wallG, tor, antiTor, live);
        }

        static Vec2 Unit(double x, double y) { double m = Math.Sqrt(x * x + y * y); return m > 0 ? new Vec2(x / m, y / m) : new Vec2(1, 0); }

        static readonly string[] ShapeName = { "Rope tornado", "Cone tornado", "Stovepipe tornado", "Wedge tornado", "Multiple-vortex tornado", "Drillbit tornado", "Elephant-trunk tornado", "Dust whirl under the wall cloud", "Sidewinder tornado" };
        static readonly string[] ShapeNoun = { "rope", "cone", "stovepipe", "wedge", "multiple-vortex tornado", "drillbit", "elephant trunk", "dust whirl", "sidewinder" };
        static string A(string n) => ("aeiou".IndexOf(n[0]) >= 0 ? "an " : "a ") + n;
        static string Cap1(string n) => char.ToUpperInvariant(n[0]) + n.Substring(1);

        static double ShapeWidth(TornadoShape s) => s switch { TornadoShape.Wedge => 0.8, TornadoShape.MultiVortex => 0.75, TornadoShape.Stovepipe => 0.6, TornadoShape.ElephantTrunk => 0.5, TornadoShape.DustWhirl => 0.5, TornadoShape.Cone => 0.45, TornadoShape.Drillbit => 0.4, TornadoShape.Rope => 0.35, _ => 0.3 };

        void Stage(SkinParams P, double t, double tm, in StormCharacter ch, CycleGene wallC, in CycleGeom g, in TornadoNow tor, double antiTor, double live)
        {
            int ix; string stage, cap;
            if (live < 0.6) { ix = 7; stage = "Dissipating"; cap = "The storm has lost its inflow. The updraft weakens and the base rises; only the anvil drifts on downwind."; }
            else if (antiTor > 0.15) { ix = 9; stage = "Anticyclonic tornado"; cap = "Rare: a second tornado at the southern curl of the rear-flank downdraft surge, turning clockwise, while the main circulation turns counter-clockwise."; }
            else if (tor.on && tor.amount > 0.15)
            {
                ix = 8;
                var gn = tor.gene;
                if (tor.rope > 0.4) { stage = "Tornado roping out"; cap = "The rear-flank downdraft has wrapped around the circulation and cut off its warm inflow. The tornado thins into a rope and is left behind as the storm occludes."; }
                else if (tor.breakdown > 0.4) { stage = "Tornado breaking down"; cap = "The wide funnel lifts from below into a collar cloud under the wall cloud, exposing the narrower vortex inside it."; }
                else if (tor.satOn && tor.satAmount > 0.4) { stage = "Satellite tornado"; cap = "A smaller tornado has formed beside the main one and orbits it counter-clockwise, inside the same broad circulation."; }
                else if (tor.multi > 0.5) { stage = "Multiple-vortex tornado"; cap = "Several suction vortices orbit inside a wider circulation, each one a narrow swath of the strongest winds."; }
                else if (tor.morph > 0.15 && tor.morph < 0.85)
                {
                    string a = ShapeNoun[(int)tor.morphFrom], b = ShapeNoun[(int)tor.morphTo];
                    if (tor.morphFrom == TornadoShape.DustWhirl) { stage = "Funnel coming down"; cap = "The vortex tightens and its condensation funnel lowers from the wall cloud to meet the dust whirl at the ground."; }
                    else if (tor.morphTo == TornadoShape.Sidewinder) { stage = "Rope starting to sidewind"; cap = "Outflow drags the rope's lower end sideways along the ground while its top stays under the wall cloud."; }
                    else
                    {
                        bool wider = ShapeWidth(tor.morphTo) > ShapeWidth(tor.morphFrom);
                        stage = $"{Cap1(a)} {(wider ? "widening" : "narrowing")} into {A(b)}";
                        cap = wider ? "The vortex is strengthening and broadening: its condensation funnel widens as the pressure drop deepens over a wider core."
                                    : "The circulation is tightening: the funnel narrows as the vortex contracts.";
                    }
                }
                else
                {
                    var sh = tor.phaseShape;
                    stage = ShapeName[(int)sh];
                    cap = sh == TornadoShape.Sidewinder
                        ? "A sidewinder: the rope's lower end is dragged along the ground by the outflow, snaking sideways while its top stays under the wall cloud."
                        : $"EF{tor.ef} tornado, about {Math.Round(gn.widthM / 0.9144 / 10) * 10:0} yards wide, moving with the storm at {Mag(ch.motion) / C.MPH:0} mph.";
                    cap += gn.rainWrap > 0.5 ? " It is wrapped in rain: hard to see until it is close." : gn.condReach < 0.5 ? " The cloud base is high: the condensation funnel hangs only part way down, and a dust whirl marks it at the ground." : "";
                    if (gn.skin == FunnelSkin.Laminar) cap += " Its condensation is smooth and laminar, with fine helical striations.";
                    else if (gn.skin == FunnelSkin.Ghost) cap += " A thin, translucent funnel, hard to pick out against the sky.";
                    if (tor.dust > 0.6 && gn.soilName != null && (gn.soilName.StartsWith("red") || gn.soilName == "pale tan"))
                        cap += gn.soilName == "pale tan" ? " It raises pale dust off the dry ground." : $" It is lofting {gn.soilName} into its debris cloud.";
                }
            }
            else if (wallC != null && g.wallAmt > 0.2)
            {
                double tau = t - wallC.wallS;
                if (g.occ > 0.5) { ix = 6; stage = "Occlusion"; cap = "The gust front wraps around the old circulation, which weakens and drifts to the rear. A new updraft base and wall cloud can take over to the east."; }
                else if (g.slot > 0.3) { ix = 5; stage = wallC.tornado != null ? "Rear-flank downdraft" : "Mature supercell"; cap = "Dry air from behind the storm descends and surges round the south side of the wall cloud, carving a horseshoe-shaped clear slot into the base." + (wallC.tornado == null ? " Strong low-level rotation, but no tornado this cycle." : ""); }
                else { ix = 4; stage = wallC.index > 0 ? "New wall cloud" : "Wall cloud forming"; cap = "Rain-cooled, moist air from the forward flank is pulled into the updraft and condenses below the main base: a rotating wall cloud under the rear of the updraft."; }
            }
            else if (tm < 12) { ix = 1; stage = "Towering cumulus"; cap = "A boundary-layer thermal breaks the cap and rises as a hard, cauliflower tower."; }
            else if (tm < 22) { ix = 2; stage = "Cumulonimbus"; cap = "The tower reaches the tropopause and spreads out as an anvil downwind. Rain and hail start to fall on the forward flank."; }
            else if (cycles.Count == 0 || t < cycles[0].wallS) { ix = 3; stage = "Supercell organising"; cap = "Wind shear tilts the inflow's spin into the updraft: the updraft rotates, the base flattens and striates, and an inflow tail streams in."; }
            else { ix = 3; stage = "Supercell"; cap = "Between cycles: the rotating updraft reorganises before the next wall cloud forms."; }
            P.stage = stage; P.caption = cap;
            P.Set(Slot.Time, (float)t, (float)lifeS, 0, ix);
        }
    }
}
