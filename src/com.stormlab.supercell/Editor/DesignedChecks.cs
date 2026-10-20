// Checks of the designed storm (Runtime/Design): pure C#, no Unity editor calls, so the same checks run from
// DesignedValidation (Unity batch / menu) and from a plain .NET harness. Each returns "detail" on success or
// "FAIL detail" on failure.
using System;
using System.Collections.Generic;
using System.Linq;
using StormLab.Supercell.Designer;
using SEnv = StormLab.Supercell.Environment;

namespace StormLab.Supercell.EditorTools
{
    public static class DesignedChecks
    {
        static SEnv Env(double T0F, double TdF, double moistFt, double lapse, double cap, double llj, double shear, double upper)
            => SEnv.Make(new StormPreset { T0F = T0F, TdF = TdF, moistFt = moistFt, lapse = lapse, cap = cap, llj = llj, shear = shear, upper = upper }.ToEnv());
        static SEnv Preset(string key) => SEnv.Make(StormPreset.All.First(p => p.key == key).ToEnv());
        static double Mag(Vec2 v) => Math.Sqrt(v.x * v.x + v.y * v.y);
        static string F(double x) => x.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public static readonly (string name, Func<string> run)[] All =
        {
            ("same seed gives the same storm, another seed a different one", Determinism),
            ("environment: high shear + low CAPE is smaller and more tilted than high CAPE", ShearVsCape),
            ("environment: High Plains dryline storm has a high base and exposed tornadoes; HP wraps them in rain", Visibility),
            ("climatology: EF mix and widths per region at the reference STP", Climatology),
            ("climatology: higher STP shifts the mix toward strong tornadoes", StpShift),
            ("cycles: 1-4 per storm, tornado count and gaps near the record, later cycles both weaker and stronger", Cycles),
            ("geometry: clear slot and horseshoe grow with tornado size", Geometry),
            ("variety: one shape comes in many proportions; tornadoes change shape, skin and soil; satellites are rare", Variety),
            ("real time: storm and tornado move at the storm motion, wall cloud turns at v/r", RealTime),
            ("along-track environment: richer air turns an LP storm classic; a cap kills it early", AlongTrack),
            ("kinematic wind: inflow below the base, updraft, counter-clockwise mesocyclone, outflow aloft", KinematicWind),
            ("every preset evaluates over its whole life without NaN", Presets),
        };

        // ------------------------------------------------------------------
        static string Variety()
        {
            int n = 0, morph = 0, sat = 0, side = 0, lam = 0, ghost = 0, lean = 0;
            var flare = new List<double>(); var albedo = new List<double>();
            var soils = new Dictionary<ClimoRegion, HashSet<string>>();
            foreach (var (key, reg) in new[] { ("plains", ClimoRegion.Plains), ("dixie", ClimoRegion.Dixie), ("ohio", ClimoRegion.Midwest) })
            {
                var src = new FixedEnvironment(Preset(key)); soils[reg] = new HashSet<string>();
                for (int seed = 1; seed <= 300; seed++)
                {
                    var st = new DesignedStorm(src, new Vec2(0, 0), seed, reg);
                    foreach (var c in st.cycles)
                    {
                        var t = c.tornado; if (t == null) continue; n++;
                        if (t.phases.Length > 1) morph++;
                        if (t.satellite) sat++;
                        if (t.phases.Any(q => q.shape == TornadoShape.Sidewinder)) side++;
                        if (t.skin == FunnelSkin.Laminar) lam++;
                        if (t.skin == FunnelSkin.Ghost) ghost++;
                        if (t.leanKm > 0.1) lean++;
                        soils[reg].Add(t.soilName);
                        if (t.shape == TornadoShape.Cone) flare.Add(t.topKm / t.groundKm);
                        albedo.Add(t.albedo);
                        // the shape it has now always comes from its phases, and its funnel stays finite
                        for (double f = 0.01; f < 1; f += 0.07)
                        {
                            var o = st.TornadoAt(t.formS + f * t.lifeS);
                            if (double.IsNaN(o.groundKm) || double.IsNaN(o.topKm) || double.IsNaN(o.ground.x)) return $"FAIL NaN in tornado of seed {seed} at {F(f)} of its life";
                        }
                    }
                }
            }
            if (n < 100) return $"FAIL only {n} tornadoes";
            double Sh(int k) => k / (double)n;
            double Cv(List<double> xs) { double m = xs.Average(); return Math.Sqrt(xs.Sum(x => (x - m) * (x - m)) / xs.Count) / m; }
            string d = $"{n} tornadoes: change shape {F(Sh(morph))}, satellite {F(Sh(sat))}, sidewind {F(Sh(side))}, laminar {F(Sh(lam))}, translucent {F(Sh(ghost))}, lean > 100 m {F(Sh(lean))}; "
                + $"cone flare spread (cv) {F(flare.Count > 1 ? Cv(flare) : 0)}, brightness {F(albedo.Min())}-{F(albedo.Max())}; soils "
                + string.Join(", ", soils.Select(kv => $"{kv.Key}: {string.Join("/", kv.Value)}"));
            if (Sh(morph) < 0.15 || Sh(morph) > 0.65) return "FAIL share changing shape out of range; " + d;
            if (sat == 0 || Sh(sat) > 0.15) return "FAIL satellites missing or too common; " + d;
            if (lam == 0 || ghost == 0 || side == 0) return "FAIL a skin or the sidewinder never appears; " + d;
            if (flare.Count > 5 && Cv(flare) < 0.1) return "FAIL cones all have the same proportions; " + d;
            if (!soils[ClimoRegion.Plains].Contains("red") || soils.Values.Any(v => v.Count < 2)) return "FAIL soil colours do not vary by region; " + d;
            return d;
        }

        static string Determinism()
        {
            var src = new FixedEnvironment(Preset("plains"));
            var a = new DesignedStorm(src, new Vec2(0, 0), 42, ClimoRegion.Plains);
            var b = new DesignedStorm(src, new Vec2(0, 0), 42, ClimoRegion.Plains);
            var c = new DesignedStorm(src, new Vec2(0, 0), 43, ClimoRegion.Plains);
            var Pa = new SkinParams(); var Pb = new SkinParams(); var Pc = new SkinParams();
            int diffC = 0;
            for (double t = 0; t < a.lifeS; t += 97)
            {
                a.Evaluate(t, t, new Vec2(0, 0), Pa); b.Evaluate(t, t, new Vec2(0, 0), Pb); c.Evaluate(t, t, new Vec2(0, 0), Pc);
                for (int i = 0; i < SkinParams.NSLOT; i++)
                {
                    if (Pa.v[i] != Pb.v[i]) return $"FAIL slot {i} differs at t = {t} s with the same seed";
                    if (Pa.v[i] != Pc.v[i]) diffC++;
                }
            }
            if (a.lifeS != b.lifeS || a.cycles.Count != b.cycles.Count) return "FAIL life or cycle count differs with the same seed";
            return diffC > 0 ? $"identical over {a.lifeS / 60:0} min; seed 43 differs in {diffC} slot-samples" : "FAIL seed 43 gives the same storm as seed 42";
        }

        static string ShearVsCape()
        {
            // a cool-season, low-CAPE, high-shear day vs a big-CAPE, moderate-shear Plains day
            var lo = StormCharacter.From(Env(72, 64, 3000, 6.2, 0, 50, 75, 90));
            var hi = StormCharacter.From(Env(90, 70, 4000, 8.5, 0.5, 30, 38, 60));
            string d = $"low CAPE {lo.cape:0} J/kg, shear {lo.shear6:0} m/s: R {F(lo.R)} km, tilt {F(Mag(lo.tilt))}, top {F(lo.el)} km; high CAPE {hi.cape:0}, shear {hi.shear6:0}: R {F(hi.R)}, tilt {F(Mag(hi.tilt))}, top {F(hi.el)}";
            if (lo.kind != StormKind.Supercell || hi.kind != StormKind.Supercell) return $"FAIL not both supercells ({lo.kind}, {hi.kind}); {d}";
            if (!(lo.R < hi.R - 0.5)) return "FAIL low-CAPE updraft not smaller; " + d;
            if (!(Mag(lo.tilt) > 1.8 * Mag(hi.tilt))) return "FAIL low-CAPE updraft not clearly more tilted; " + d;
            if (!(lo.el < hi.el)) return "FAIL low-CAPE storm not lower-topped; " + d;
            return d;
        }

        static (double cond, double wrap, int n) TornadoStats(SEnv E, ClimoRegion reg, int count)
        {
            var ch = StormCharacter.From(E); ch.ptor = Math.Max(ch.ptor, 0.6);   // force tornadoes so their look can be compared
            double cond = 0, wrap = 0; int n = 0;
            for (int s = 0; s < count; s++)
            {
                var g = new StormGenome(s, reg, ch);
                var c = g.Realise(0, 2400, ch, null, Mag(ch.motion));
                if (c.tornado == null) continue;
                cond += c.tornado.condReach; wrap += c.tornado.rainWrap; n++;
            }
            return (cond / Math.Max(n, 1), wrap / Math.Max(n, 1), n);
        }

        static string Visibility()
        {
            // a High Plains dryline storm (hot, drier surface air: high base; strong winds aloft: LP to classic)
            var dl = Env(92, 60, 0, 8.5, 0.5, 40, 50, 90);
            var hpE = Preset("dixie");
            var a = StormCharacter.From(dl); var b = StormCharacter.From(hpE);
            var sa = TornadoStats(dl, ClimoRegion.Plains, 400); var sb = TornadoStats(hpE, ClimoRegion.Dixie, 400);
            string d = $"dryline: base {F(a.zb)} km, hpness {F(a.hpness)}, condensation reach {F(sa.cond)}, rain wrap {F(sa.wrap)}; Dixie HP: base {F(b.zb)} km, hpness {F(b.hpness)}, reach {F(sb.cond)}, wrap {F(sb.wrap)}";
            if (!(a.zb > b.zb + 0.8)) return "FAIL dryline base not clearly higher; " + d;
            if (!(a.hpness < 0.1)) return "FAIL dryline storm not LP/classic; " + d;
            if (!(sa.wrap < 0.05)) return "FAIL dryline tornadoes rain-wrapped; " + d;
            if (!(sb.wrap > 0.4)) return "FAIL HP tornadoes not rain-wrapped; " + d;
            if (!(sb.cond > sa.cond + 0.15)) return "FAIL low-based HP funnels should condense further down; " + d;
            // tall: the visible column from base to ground is the cloud base height
            return d;
        }

        static StormCharacter Ref(ClimoRegion r, double stp)
        {
            var ch = StormCharacter.From(Preset(r == ClimoRegion.Dixie ? "dixie" : r == ClimoRegion.Midwest ? "ohio" : "plains"));
            ch.stp = stp; ch.ptor = 0.9;
            return ch;
        }

        static string Climatology()
        {
            var sb = new List<string>();
            foreach (ClimoRegion reg in Enum.GetValues(typeof(ClimoRegion)))
            {
                var ch = Ref(reg, TornadoClimatology.StpRef);
                var counts = new int[6]; var widths = Enumerable.Range(0, 6).Select(_ => new List<double>()).ToArray(); int n = 0;
                for (int s = 0; s < 4000; s++)
                {
                    var c = new StormGenome(s, reg, ch).Realise(0, 2400, ch, null, Mag(ch.motion));
                    if (c.tornado == null) continue;
                    counts[c.tornado.ef]++; widths[c.tornado.ef].Add(c.tornado.widthM / 0.9144); n++;
                }
                var exp = TornadoClimatology.RatingShare[(int)reg];
                for (int k = 0; k < 6; k++)
                {
                    double p = counts[k] / (double)n, tol = 3 * Math.Sqrt(exp[k] * (1 - exp[k]) / n) + 0.03 * exp[k] + 0.003;
                    // the cycle's rotation walk (meso^2 multiplies STP) spreads the mix a little; allow 25 % of the share on top
                    tol += 0.25 * exp[k];
                    if (Math.Abs(p - exp[k]) > tol) return $"FAIL {reg} EF{k}: {F(p)} vs record {F(exp[k])} (n {n})";
                    if (k <= 3 && widths[k].Count > 50)
                    {
                        widths[k].Sort(); double med = widths[k][widths[k].Count / 2], rec = TornadoClimatology.WidthYd[(int)reg][k][2];
                        if (Math.Abs(med / rec - 1) > 0.2) return $"FAIL {reg} EF{k} median width {med:0} yd vs record {rec:0}";
                    }
                }
                sb.Add($"{reg}: EF0-5 " + string.Join("/", counts.Select(c => F(c / (double)n))) + " vs " + string.Join("/", exp.Select(F)));
            }
            return string.Join("; ", sb);
        }

        static string StpShift()
        {
            double Strong(double stp)
            {
                var ch = Ref(ClimoRegion.Plains, stp); int strong = 0, n = 0;
                for (int s = 0; s < 3000; s++)
                {
                    var c = new StormGenome(s, ClimoRegion.Plains, ch).Realise(0, 2400, ch, null, Mag(ch.motion));
                    if (c.tornado == null) continue; n++; if (c.tornado.ef >= 2) strong++;
                }
                return strong / (double)n;
            }
            double a = Strong(0.5), b = Strong(1.5), c2 = Strong(6);
            string d = $"EF2+ share at STP 0.5 / 1.5 / 6: {F(a)} / {F(b)} / {F(c2)}";
            return a < b && b < c2 && c2 > 0.3 ? d : "FAIL " + d;
        }

        static string Cycles()
        {
            var src = new FixedEnvironment(Preset("plains"));
            var hist = new int[6]; var torHist = new int[6]; var gaps = new List<double>(); int weaker = 0, stronger = 0, pairs = 0, smaller = 0, upPairs = 0;
            for (int s = 0; s < 400; s++)
            {
                var st = new DesignedStorm(src, new Vec2(0, 0), s, ClimoRegion.Plains);
                int nc = st.cycles.Count; hist[Math.Min(nc, 5)]++;
                if (nc < 1 || nc > 4) return $"FAIL seed {s} has {nc} cycles";
                var tors = st.cycles.Where(c => c.tornado != null).Select(c => c.tornado).ToList();
                torHist[Math.Min(tors.Count, 5)]++;
                for (int i = 1; i < tors.Count; i++)
                {
                    gaps.Add((tors[i].formS - tors[i - 1].EndS) / 60);
                    pairs++; if (tors[i].ef < tors[i - 1].ef) weaker++; else if (tors[i].ef > tors[i - 1].ef) stronger++;
                }
                for (int i = 1; i < nc; i++) { upPairs++; if (st.cycles[i].updraft < st.cycles[i - 1].updraft) smaller++; }
            }
            gaps.Sort();
            double medGap = gaps.Count > 0 ? gaps[gaps.Count / 2] : 0;
            int withTor = torHist.Skip(1).Sum();
            double two = torHist.Skip(2).Sum() / (double)Math.Max(withTor, 1);
            string d = $"cycles 1-4: {string.Join("/", hist.Skip(1).Take(4))}; storms with 1/2/3/4 tornadoes {string.Join("/", torHist.Skip(1).Take(4))} (2+ share {F(two)}, record ~0.34 at STP 1.5, more at this preset's STP 4.6); "
                + $"median gap {medGap:0} min (record 15); next tornado weaker {weaker}/{pairs}, stronger {stronger}/{pairs}; next updraft smaller {smaller}/{upPairs}";
            if (weaker < 0.15 * pairs || stronger < 0.1 * pairs) return "FAIL later cycles not varied; " + d;
            if (smaller < 0.15 * upPairs) return "FAIL updraft always grows; " + d;
            if (medGap < 6 || medGap > 30) return "FAIL gap between tornadoes out of range; " + d;
            if (two < 0.25 || two > 0.8) return "FAIL share of multi-tornado storms out of range; " + d;
            return d;
        }

        static string Geometry()
        {
            var src = new FixedEnvironment(Preset("plains"));
            var xs = new List<double>(); var ys = new List<double>(); var P = new SkinParams();
            for (int s = 0; s < 300; s++)
            {
                var st = new DesignedStorm(src, new Vec2(0, 0), s, ClimoRegion.Plains);
                foreach (var c in st.cycles)
                {
                    if (c.tornado == null) continue;
                    double t = c.tornado.formS + 0.5 * c.tornado.lifeS;
                    var tor = st.TornadoAt(t);
                    if (!tor.on || tor.cycle != c.index) continue;
                    st.Evaluate(t, t, new Vec2(0, 0), P);
                    xs.Add(c.tornado.groundKm); ys.Add(P[Slot.Rfd].y);
                }
            }
            double rho = Spearman(xs, ys);
            // the biggest fifth vs the smallest fifth
            var idx = Enumerable.Range(0, xs.Count).OrderBy(i => xs[i]).ToList(); int q = Math.Max(1, xs.Count / 5);
            double small = idx.Take(q).Average(i => ys[i]), big = idx.Skip(xs.Count - q).Average(i => ys[i]);
            string d = $"{xs.Count} tornadoes: rank correlation of slot radius with funnel size {F(rho)}; slot radius {F(small)} km for the smallest fifth, {F(big)} km for the widest";
            return rho > 0.35 && big > 1.3 * small ? d : "FAIL " + d;
        }

        static double Spearman(List<double> a, List<double> b)
        {
            double[] R(List<double> v) { var o = Enumerable.Range(0, v.Count).OrderBy(i => v[i]).ToArray(); var r = new double[v.Count]; for (int i = 0; i < o.Length; i++) r[o[i]] = i; return r; }
            var ra = R(a); var rb = R(b); double ma = ra.Average(), mb = rb.Average(), sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < ra.Length; i++) { sab += (ra[i] - ma) * (rb[i] - mb); saa += (ra[i] - ma) * (ra[i] - ma); sbb += (rb[i] - mb) * (rb[i] - mb); }
            return sab / Math.Sqrt(saa * sbb);
        }

        static string RealTime()
        {
            var E = Preset("plains"); var src = new FixedEnvironment(E);
            var st = new DesignedStorm(src, new Vec2(0, 0), 4, ClimoRegion.Plains);
            double bunk = Mag(E.bk.rm);
            var p0 = st.UpdraftAt(3600); var p1 = st.UpdraftAt(4200);
            double v = Mag(new Vec2(p1.x - p0.x, p1.y - p0.y)) * 1000 / 600;
            if (Math.Abs(v / bunk - 1) > 0.02) return $"FAIL storm speed {F(v)} m/s vs Bunkers {F(bunk)}";
            // the tornado in its mature phase moves with the storm
            var tg = st.cycles.Select(c => c.tornado).FirstOrDefault(x => x != null && x.lifeS > 300);
            string torD = "no long-lived tornado for this seed";
            if (tg != null)
            {
                double ta = tg.formS + 0.3 * tg.lifeS;
                var a = st.TornadoAt(ta); var b = st.TornadoAt(ta + 30);
                double vt = Mag(new Vec2(b.ground.x - a.ground.x, b.ground.y - a.ground.y)) * 1000 / 30;
                if (Math.Abs(vt / bunk - 1) > 0.15) return $"FAIL tornado moves at {F(vt)} m/s, storm at {F(bunk)}";
                torD = $"tornado {F(vt)} m/s, peak wind {F(a.vmax)} m/s (EF{a.ef}), funnel turning {F(a.spin)} rad/s";
            }
            var P = new SkinParams(); double minP = 1e9, maxP = 0;
            foreach (var c in st.cycles)
            {
                st.Evaluate(c.wallS + 600, 0, new Vec2(0, 0), P);
                double w = P[Slot.Wall2].y; if (w <= 0) continue;
                double per = 2 * Math.PI / w / 60; minP = Math.Min(minP, per); maxP = Math.Max(maxP, per);
            }
            string d = $"storm {F(v)} m/s = Bunkers {F(bunk)}; {torD}; wall cloud turns once every {F(minP)}-{F(maxP)} min";
            return minP >= 2.5 && maxP <= 12 ? d : "FAIL " + d;
        }

        sealed class Zones : IEnvironmentSource
        {
            readonly SEnv a, b; readonly double x0;
            public Zones(SEnv a, SEnv b, double x0) { this.a = a; this.b = b; this.x0 = x0; }
            public SEnv At(double x, double y, double t) => x < x0 ? a : b;
        }

        static string AlongTrack()
        {
            // An LP storm on the dry side of the dryline moves east into deep Gulf moisture
            var lp = Env(90, 57, 0, 8.5, 0.5, 30, 48, 95);
            var rich = Env(86, 68, 4000, 7.8, 0.5, 44, 55, 80);
            var st = new DesignedStorm(new Zones(lp, rich, 25), new Vec2(0, 0), 7, ClimoRegion.Plains);
            double h0 = st.CharacterAt(600).hpness, h1 = st.CharacterAt(Math.Min(st.lifeS, 7200)).hpness;
            double z0 = st.CharacterAt(600).zb, z1 = st.CharacterAt(Math.Min(st.lifeS, 7200)).zb;
            // and a classic storm that runs into a strong cap
            var capped = Preset("capped");
            var free = new DesignedStorm(new FixedEnvironment(rich), new Vec2(0, 0), 7, ClimoRegion.Plains);
            var dies = new DesignedStorm(new Zones(rich, capped, 40), new Vec2(0, 0), 7, ClimoRegion.Plains);
            string d = $"LP to classic: hpness {F(h0)} -> {F(h1)}, base {F(z0)} -> {F(z1)} km; into a cap: life {dies.lifeS / 60:0} min vs {free.lifeS / 60:0} min in open air";
            if (!(h1 > h0 + 0.5 && z1 < z0 - 0.5)) return "FAIL storm did not turn classic in richer air; " + d;
            if (!(dies.lifeS < free.lifeS - 1800)) return "FAIL cap did not end the storm early; " + d;
            return d;
        }

        static string KinematicWind()
        {
            var st = new DesignedStorm(new FixedEnvironment(Preset("plains")), new Vec2(0, 0), 4, ClimoRegion.Plains);
            var f = new KinematicWindField(st); var d = new byte[f.Bytes];
            double t = st.cycles.Count > 0 ? st.cycles[0].wallS + 900 : 3600;
            f.Compute(t, d);
            var E = st.EnvironmentAt(t); var up = st.UpdraftAt(t); var ch = st.CharacterAt(t);
            UnityEngine.Vector3 At(double xKm, double yKm, double zKm) => f.Sample(d, new UnityEngine.Vector2((float)((xKm - f.originKm.x) * 1000), (float)((yKm - f.originKm.y) * 1000)), (float)(zKm * 1000));
            double Env(double z, bool v) => WindProfile.At(v ? E.w.v : E.w.u, z * 1000);
            // radial component (minus the environment) on a ring 6 km out at 0 km, and tangential at 4 km
            double inflow = 0, outflow = 0, swirl = 0; int n = 16;
            for (int k = 0; k < n; k++)
            {
                double a = 2 * Math.PI * k / n, ex = Math.Cos(a), ey = Math.Sin(a);
                var w0 = At(up.x + 6 * ex, up.y + 6 * ey, 0);
                inflow += (w0.x - Env(0, false)) * ex + (w0.y - Env(0, true)) * ey;
                double z4 = 4, cx = up.x + ch.tilt.x * (z4 - ch.zb), cy = up.y + ch.tilt.y * (z4 - ch.zb);
                var w4 = At(cx + 3 * ex, cy + 3 * ey, z4);
                swirl += -(w4.x - Env(z4, false)) * ey + (w4.y - Env(z4, true)) * ex;
                double zt = Math.Round(ch.el) - 1, tx = up.x + ch.tilt.x * (zt - ch.zb), ty = up.y + ch.tilt.y * (zt - ch.zb);
                var wt = At(tx + 8 * ex, ty + 8 * ey, zt);
                outflow += (wt.x - Env(zt, false)) * ex + (wt.y - Env(zt, true)) * ey;
            }
            inflow /= n; outflow /= n; swirl /= n;
            double zm = Math.Round(ch.zb + 0.4 * (ch.el - ch.zb));
            double wUp = At(up.x + ch.tilt.x * (zm - ch.zb), up.y + ch.tilt.y * (zm - ch.zb), zm).z;
            string dd = $"inflow {F(-inflow)} m/s at 6 km, updraft {F(wUp)} m/s at {zm} km, mesocyclone {F(swirl)} m/s at 3 km radius, anvil outflow {F(outflow)} m/s; {f.NBX}x{f.NBY}x{f.NL} blocks";
            return inflow < -1 && wUp > 10 && swirl > 3 && outflow > 2 ? dd : "FAIL " + dd;
        }

        static string Presets()
        {
            var P = new SkinParams(); var sb = new List<string>();
            foreach (var pr in StormPreset.All)
            {
                var st = new DesignedStorm(new FixedEnvironment(SEnv.Make(pr.ToEnv())), new Vec2(0, 0), 1, ClimoRegion.Plains);
                int n = 0;
                for (double t = 0; t <= st.lifeS; t += 45)
                {
                    st.Evaluate(t, t, st.UpdraftAt(t), P); n++;
                    for (int i = 0; i < SkinParams.NSLOT; i++)
                    {
                        var v = P.v[i];
                        if (float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsNaN(v.w) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z) || float.IsInfinity(v.w))
                            return $"FAIL {pr.key}: slot {i} not finite at t = {t} s";
                    }
                }
                sb.Add($"{pr.key} {st.kind} {st.lifeS / 60:0} min {st.cycles.Count}c");
            }
            return string.Join(", ", sb);
        }
    }
}
