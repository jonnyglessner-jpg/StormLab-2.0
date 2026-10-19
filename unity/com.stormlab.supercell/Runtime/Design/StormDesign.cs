// C# port of StormLab's presets (src/presets.ts, src/units.ts) and designed storm (src/storm3d/design.ts).
// StormLab: https://github.com/jonnyglessner-jpg/StormLab (MIT). Coordinates are km in the storm's frame (it moves
// with the storm): x east, y north, z up.
using System;

namespace StormLab.Supercell
{
    [Serializable]
    public class StormPreset
    {
        public string key, name, blurb, watch;
        public double T0F, TdF, moistFt, lapse, cap, llj, shear, upper;   // winds in knots

        public EnvInput ToEnv() => new EnvInput
        {
            T0F = T0F, TdF = Math.Min(TdF, T0F), lapse = lapse, cap = cap, moist = moistFt * C.FT,
            llj = llj * C.KT, shear = shear * C.KT, upper = upper * C.KT
        };

        public static readonly StormPreset[] All =
        {
            new StormPreset { key = "plains", name = "Plains tornado outbreak", T0F = 84, TdF = 68, moistFt = 4000, lapse = 7.8, cap = 1.0, llj = 44, shear = 55, upper = 80,
                blurb = "Deep Gulf moisture, a strong low-level jet and south-west flow aloft.",
                watch = "A classic supercell: a rock-hard updraft, a hook on radar and a tornado-producing wall cloud. Storms move north-east at 30-35 mph." },
            new StormPreset { key = "hail", name = "Big hail day", T0F = 92, TdF = 66, moistFt = 2000, lapse = 8.8, cap = 1.0, llj = 22, shear = 55, upper = 70,
                blurb = "Steep mid-level lapse rates and big instability, but weak low-level winds.",
                watch = "Huge updrafts and large hail with a high, dry cloud base. Plenty of rotation aloft, little near the ground, so tornadoes are unlikely." },
            new StormPreset { key = "dixie", name = "Dixie Alley HP", T0F = 75, TdF = 68, moistFt = 4000, lapse = 6.0, cap = 0, llj = 50, shear = 60, upper = 58,
                blurb = "Low clouds, modest instability and very strong wind shear.",
                watch = "A heavy-precipitation supercell. Rain wraps around the mesocyclone, so tornadoes are hidden in rain and hard to see coming." },
            new StormPreset { key = "ohio", name = "Ohio Valley spring", T0F = 82, TdF = 66, moistFt = 3000, lapse = 6.8, cap = 0.5, llj = 34, shear = 50, upper = 75,
                blurb = "Moderate instability with strong shear ahead of a spring system.",
                watch = "Supercells that rotate well but run on less fuel than out west. Watch whether the low-level rotation is enough for a tornado." },
            new StormPreset { key = "lp", name = "High Plains LP", T0F = 90, TdF = 57, moistFt = 0, lapse = 8.5, cap = 0.5, llj = 22, shear = 48, upper = 95,
                blurb = "Dry air, a high cloud base and fierce winds aloft.",
                watch = "A low-precipitation supercell: a sculpted, corkscrew updraft with little rain, the kind photographers chase. Large hail, few tornadoes." },
            new StormPreset { key = "capped", name = "Cap holds (bust)", T0F = 90, TdF = 68, moistFt = 0, lapse = 8.0, cap = 4.0, llj = 36, shear = 50, upper = 80,
                blurb = "Every ingredient for big storms, under a strong inversion.",
                watch = "Huge CAPE and shear, and nothing happens. The warm layer aloft stops surface air from rising: the classic bust day." },
            new StormPreset { key = "pulse", name = "Summer pulse storm", T0F = 90, TdF = 72, moistFt = 1500, lapse = 6.0, cap = 0, llj = 8, shear = 10, upper = 24,
                blurb = "Hot, humid and almost no wind.",
                watch = "Without shear the storm rains into its own updraft: it towers up, dumps heavy rain and small hail, and collapses within the hour." },
        };
    }

    public enum StormKind { Supercell, Pulse, Capped, None }

    /// <summary>Fixed properties of a storm for one setup (StormPlan in design.ts).</summary>
    public class StormPlan
    {
        public StormKind kind;
        public double zb, el;
        public Vec2 motion, fwd, lft, adir, tilt;
        public double R, striation, ffd, hail, rfd, wrap, slot, tail, anvilLen;
        public double Rw, lower, spin, mesoSpin;
        public bool tornadic; public double torType, torR;
        public double antiMeso; public bool antiTornado;
        public double duration;
    }

    /// <summary>One moment of the storm (StormFrame in design.ts).</summary>
    public class StormFrame
    {
        public readonly float[] p = new float[StormDesign.NSLOT * 4];
        public string stage = "", caption = "";
        public float tornado;
        public float Get(int slot, int c) => p[slot * 4 + c];
    }

    public static class StormDesign
    {
        /// <summary>Number of vec4 slots in the parameter block (matches struct SP in StormCommon.hlsl).</summary>
        public const int NSLOT = 21;
        const double CYCLE0 = 30, CYCLE = 38;

        static Vec2 Unit(double u, double v) { double m = Math.Sqrt(u * u + v * v); if (m == 0) m = 1; return new Vec2(u / m, v / m); }
        static double Ramp(double t, double a, double b) { double x = C.Clamp((t - a) / (b - a), 0, 1); return x * x * (3 - 2 * x); }

        public static StormPlan Plan(Environment E, StormDiag S)
        {
            double zb = C.Clamp(E.pc.lcl / 1000, 0.5, 2.6), el = C.Clamp((E.pc.el ?? 12000) / 1000, 8, 15.5);
            var rm = E.bk.rm;
            var fwd = Unit(rm.x, rm.y); var lft = new Vec2(-fwd.y, fwd.x);
            var adir = Unit(WindProfile.At(E.w.u, 10000) - rm.x, WindProfile.At(E.w.v, 10000) - rm.y);
            var sh = Unit(WindProfile.At(E.w.u, 6000) - E.w.u[0], WindProfile.At(E.w.v, 6000) - E.w.v[0]);
            double tiltAmt = 0.1 * C.Clamp(E.shear6 / 25, 0, 1.4);
            StormKind kind = !S.forms ? (S.capped ? StormKind.Capped : StormKind.None)
                : S.mode == StormMode.Pulse || S.mode == StormMode.Multicell ? StormKind.Pulse : StormKind.Supercell;
            bool lp = S.precip == Precip.LP, hp = S.precip == Precip.HP;
            double R = C.Clamp(2.2 + 1.3 * C.Clamp((S.w - 20) / 30, 0, 1), 2.0, 3.5) * (lp ? 0.85 : 1);
            double rcKm = S.ptor > 0 ? S.TornadoRadiusYd(S.vMed) * 0.9144 / 1000 : 0.1;
            double meso = C.Clamp(S.vLow / 20, 0.3, 1.2);
            return new StormPlan
            {
                kind = kind, zb = zb, el = el, motion = rm, fwd = fwd, lft = lft, adir = adir,
                tilt = new Vec2(sh.x * tiltAmt, sh.y * tiltAmt), R = R,
                striation = (lp ? 1 : hp ? 0.3 : 0.65) * C.Clamp(S.vMid / 18, 0.4, 1.2),
                ffd = lp ? 0.35 : hp ? 1.5 : 1, hail = lp ? 0.6 : hp ? 1.1 : 1, rfd = lp ? 0.25 : hp ? 1.4 : 0.9,
                wrap = hp ? 0.75 : 0, slot = hp ? 0.45 : lp ? 0.8 : 1, tail = lp ? 0.6 : hp ? 0.85 : 1,
                anvilLen = C.Clamp(26 + 1.0 * E.srAnvil, 20, 52),
                Rw = C.Clamp(1.4 + 1.1 * meso, 1.3, 2.8) * (hp ? 1.2 : 1),
                lower = C.Clamp(0.25 + 0.3 * meso, 0.2, 0.6) * C.Clamp(1.6 - zb / 2, 0.6, 1.3),
                spin = 0.04 + 0.08 * meso, mesoSpin = 0.025 + 0.03 * meso,
                tornadic = kind == StormKind.Supercell && S.ptor >= 0.3,
                torType = rcKm < 0.07 ? 0.15 : rcKm < 0.18 ? 0.5 : 0.9, torR = C.Clamp(rcKm, 0.03, 0.45),
                antiMeso = kind == StormKind.Supercell && S.ptor >= 0.3 ? C.Clamp(E.srh1 / 300, 0.4, 1) : 0,
                antiTornado = kind == StormKind.Supercell && E.stp >= 4,
                duration = kind == StormKind.Supercell ? 110 : 70
            };
        }

        /// <summary>Everything at storm time t (min); clock = wall-clock seconds for spin inside features.</summary>
        public static StormFrame At(StormPlan P, double t, double clock, StormFrame into = null)
        {
            var F = into ?? new StormFrame();
            var a = F.p;
            void Set(int slot, double x, double y, double z, double w)
            { a[slot * 4] = (float)x; a[slot * 4 + 1] = (float)y; a[slot * 4 + 2] = (float)z; a[slot * 4 + 3] = (float)w; }
            double fx = P.fwd.x, fy = P.fwd.y, lx = P.lft.x, ly = P.lft.y;
            var up = new Vec2(-P.adir.x * 20, -P.adir.y * 20);
            Vec2 At2(double f, double l, Vec2? o = null) { var b = o ?? up; return new Vec2(b.x + f * fx + l * lx, b.y + f * fy + l * ly); }
            string stage = "", caption = ""; double torOn = 0;

            double towerTop = P.zb + 0.5, towerR = 1, towerAmt = 0, strat = 0, overshoot = 0;
            double anvilAmt = 0, anvilLen = 4, back = 1, mamm = 0;
            double wallAmt = 0, lower = 0, tailCloud = 0, scud = 0; var wc = At2(-1.0, -0.8);
            double tailAmt = 0, ffdAmt = 0, hailAmt = 0, slot = 0, slotR = P.Rw + 1, rfdAmt = 0;
            double flankAmt = 0, flankN = 0, mesoSpin = 0;
            double torAmt = 0, torGround = 0.1, torTop = 0.3, cond = 0, rope = 0, debris = 0;
            Vec2 torBot = wc, torTopP = wc;
            Vec2 rcB = wc; double Rs = 0, horse = 0; Vec2 curlS = wc; double antiAmt = 0, antiTor = 0;

            if (P.kind == StormKind.Supercell)
            {
                towerTop = P.zb + 1 + (P.el + 0.4 - P.zb - 1) * Ramp(t, 0, 14);
                towerR = P.R * (0.8 + 0.2 * Ramp(t, 4, 20) + 0.25 * Ramp(t, 20, 40));
                towerAmt = 1;
                overshoot = Ramp(t, 14, 20) * (0.7 + 0.3 * Math.Sin(t * 0.9));
                anvilAmt = Ramp(t, 10, 20); anvilLen = 5 + P.anvilLen * Ramp(t, 10, 55);
                back = P.R * (0.8 + 2.2 * Ramp(t, 16, 40));
                mamm = 0.8 * Ramp(t, 32, 48);
                strat = P.striation * Ramp(t, 20, 32);
                ffdAmt = P.ffd * Ramp(t, 14, 24); hailAmt = P.hail * Ramp(t, 18, 28);
                tailAmt = P.tail * Ramp(t, 24, 34);
                flankAmt = Ramp(t, 18, 30); flankN = 6;
                mesoSpin = P.mesoSpin * Ramp(t, 16, 30);
                if (t >= CYCLE0)
                {
                    int k = (int)Math.Floor((t - CYCLE0) / CYCLE); double tau = (t - CYCLE0) - k * CYCLE;
                    double occ = Ramp(tau, 24, 34);
                    wc = At2(-1.0 - 2.2 * occ, -0.8 + 1.4 * occ);
                    wallAmt = Ramp(tau, 0, 6) * (1 - Ramp(tau, 29, 35));
                    lower = P.lower * (0.4 + 0.6 * Ramp(tau, 4, 14));
                    tailCloud = wallAmt * Ramp(tau, 5, 12); scud = wallAmt * 0.8;
                    slot = P.slot * Ramp(tau, 10, 16) * (1 - Ramp(tau, 30, 36));
                    slotR = P.Rw + 0.8 + 2.2 * Ramp(tau, 12, 30);
                    rfdAmt = P.rfd * Ramp(tau, 8, 16);
                    Vec2 Dir(double th) => new Vec2(Math.Cos(th) * fx + Math.Sin(th) * lx, Math.Cos(th) * fy + Math.Sin(th) * ly);
                    Rs = P.Rw * (0.95 + 0.55 * Ramp(tau, 9, 24));
                    Vec2 dn = Dir(15 * Math.PI / 180), dsth = Dir(-170 * Math.PI / 180);
                    rcB = new Vec2(wc.x - 0.62 * Rs * dn.x, wc.y - 0.62 * Rs * dn.y);
                    curlS = new Vec2(rcB.x + Rs * dsth.x, rcB.y + Rs * dsth.y);
                    var curlN = new Vec2(wc.x + (-0.35 * fx - 0.35 * lx) * P.Rw, wc.y + (-0.35 * fy - 0.35 * ly) * P.Rw);
                    horse = slot * Ramp(tau, 9, 15);
                    antiAmt = P.antiMeso * Ramp(tau, 12, 17) * (1 - Ramp(tau, 25, 30));
                    if (P.antiTornado && k == 1) antiTor = Ramp(tau, 17, 18.5) * (1 - Ramp(tau, 21, 23));
                    if (P.tornadic && k < 2)
                    {
                        double form = Ramp(tau, 14, 16.5), die = Ramp(tau, 26, 28.5);
                        torAmt = form * (1 - die);
                        rope = Ramp(tau, 21.5, 26);
                        cond = Ramp(tau, 13.5, 16) * (P.zb < 1.4 ? 1 : 0.75) * (1 - 0.45 * Ramp(tau, 25, 27.5));
                        debris = Ramp(tau, 15, 16.5) * (1 - Ramp(tau, 25, 28));
                        double shrink = 1 - 0.75 * rope;
                        torGround = P.torR * shrink * (0.6 + 0.4 * form);
                        torTop = (P.torR * (P.torType > 0.7 ? 1.4 : 2.6) + 0.12) * (1 - 0.6 * rope);
                        torTopP = curlN;
                        double drift = 0.45 * rope;
                        torBot = new Vec2(torTopP.x + (-fx * 0.8 - lx * 0.6) * drift, torTopP.y + (-fy * 0.8 - ly * 0.6) * drift);
                        torOn = torAmt;
                    }
                    if (antiTor > 0.15) { stage = "Anticyclonic tornado"; caption = "Rare: a second tornado at the southern curl of the rear-flank downdraft surge, turning clockwise, while the main tornado turns counter-clockwise at the northern curl."; }
                    else if (torAmt > 0.15)
                    {
                        stage = rope > 0.4 ? "Tornado roping out" : "Tornado on the ground";
                        caption = rope > 0.4 ? "The rear-flank downdraft has wrapped around the circulation and cut off its warm inflow. The tornado thins into a rope and is left behind as the storm occludes."
                            : "The tornado forms at the northern curl of the rear-flank downdraft surge, where the surge meets the warm inflow under the wall cloud." + (antiAmt > 0.3 ? " At the surge's southern curl the air turns the other way: an anti-mesocyclone." : "");
                    }
                    else if (tau < 10) { stage = k > 0 ? "New wall cloud" : "Wall cloud forming"; caption = "Rain-cooled, moist air from the forward flank is pulled into the updraft and condenses lower than the main cloud base: a rotating wall cloud under the rear of the updraft."; }
                    else if (tau < 16) { stage = "Rear-flank downdraft"; caption = "Dry air from behind the storm descends, reaches the ground and surges out around the south side of the wall cloud, carving a horseshoe-shaped clear slot into the base."; }
                    else if (tau < 29)
                    {
                        stage = P.tornadic ? "Mature mesocyclone" : "Mature supercell";
                        caption = "The rear-flank downdraft surges around the wall cloud and the horseshoe-shaped clear slot widens." + (antiAmt > 0.3 ? " Cyclonic rotation curls up at its northern end and anticyclonic rotation at its southern end." : "") + (P.tornadic ? "" : " Strong low-level rotation, but no tornado this cycle.");
                    }
                    else { stage = "Occlusion"; caption = "The gust front wraps around the old circulation, which weakens and drifts to the rear. A new updraft base and wall cloud take over to the east."; }
                }
                else if (t < 12) { stage = "Towering cumulus"; caption = "A boundary-layer thermal breaks the cap and rises as a hard, cauliflower tower."; }
                else if (t < 22) { stage = "Cumulonimbus"; caption = "The tower reaches the tropopause and spreads out as an anvil downwind. Rain and hail start to fall on the forward flank."; }
                else { stage = "Supercell organising"; caption = "Wind shear tilts the inflow's spin into the updraft: the updraft rotates (a mesocyclone), the base flattens and striates, and an inflow tail streams in from the east."; }
            }
            else if (P.kind == StormKind.Pulse)
            {
                double life = 1 - Ramp(t, 34, 52), sink = Ramp(t, 34, 50);
                towerTop = P.zb + 1 + (P.el - P.zb - 1) * Ramp(t, 0, 13) * (1 - 0.6 * sink); towerR = P.R * 0.8 * (0.5 + 0.5 * Ramp(t, 3, 15)) * (1 - 0.35 * sink); towerAmt = 0.25 + 0.75 * life;
                overshoot = 0.5 * Ramp(t, 13, 18) * life;
                anvilAmt = Ramp(t, 11, 20) * (1 - 0.6 * Ramp(t, 50, 70)); anvilLen = 4 + 10 * Ramp(t, 11, 40); back = 3;
                ffdAmt = 1.4 * Ramp(t, 16, 22) * (1 - Ramp(t, 40, 55)); hailAmt = 0.4 * Ramp(t, 18, 24) * (1 - Ramp(t, 32, 40));
                stage = t < 13 ? "Towering cumulus" : t < 22 ? "Mature storm" : t < 34 ? "Heavy rain" : t < 50 ? "Collapsing" : "Dissipating anvil";
                caption = t < 13 ? "A tower rises in hot, humid air with almost no wind." : t < 34 ? "Without shear, the rain falls straight back down through the updraft: a short burst of torrential rain and small hail." : "The rain-cooled downdraft chokes off the updraft. The tower collapses and only the anvil is left.";
            }
            else if (P.kind == StormKind.Capped)
            {
                double pulse = Math.Abs(Math.Sin(t / 7));
                towerTop = P.zb + 0.8 + 1.4 * pulse; towerR = 1.6; towerAmt = 0.9;
                stage = "Capped"; caption = "Cumulus try to grow but the warm layer aloft stops them: they bubble up and fall back all afternoon.";
            }
            else { stage = "Clear"; caption = "Not enough instability for storms."; }

            double sx = P.zb;
            var flank = Unit(-fx - 0.3 * lx, -fy - 0.3 * ly);
            var tdir = Unit(fx - 0.7 * lx, fy - 0.7 * ly);
            var ffdC = P.kind == StormKind.Pulse ? up : At2(P.ffd < 0.5 ? 9 : 6, P.ffd < 0.5 ? 6 : 4.5);
            var hailC = P.kind == StormKind.Pulse ? up : At2(2.8, 2.2);
            Set(0, fx, fy, lx, ly);
            Set(1, sx, P.el, clock, t);
            Set(2, towerTop, towerR, towerAmt, strat);
            Set(3, P.tilt.x, P.tilt.y, overshoot, mesoSpin);
            Set(4, P.adir.x, P.adir.y, anvilLen, anvilAmt);
            Set(5, P.el - 2.4, P.el + 0.25, mamm, back);
            Set(6, wc.x, wc.y, P.Rw, wallAmt);
            Set(7, lower, P.spin, tailCloud, scud);
            Set(8, tdir.x, tdir.y, 11, tailAmt);
            Set(9, ffdC.x, ffdC.y, P.kind == StormKind.Pulse ? 0.6 : P.ffd > 1.2 ? 1.35 : 1, ffdAmt);
            Set(10, hailC.x, hailC.y, 2.0, hailAmt);
            Set(11, slot, slotR, rfdAmt, P.wrap);
            Set(12, flank.x, flank.y, flankAmt, flankN);
            Set(13, torBot.x, torBot.y, torAmt, P.torType);
            Set(14, torGround, torTop, cond, rope);
            Set(15, torTopP.x, torTopP.y, debris, 0);
            Set(16, up.x, up.y, 0, 0);
            Set(17, 0, 0, 0, 0);   // grid: filled by the renderer
            Set(18, rcB.x, rcB.y, Rs, horse);
            Set(19, curlS.x, curlS.y, antiAmt, antiTor);
            Set(20, 0, 0, 0, 0);   // slab: filled by the renderer
            F.stage = stage; F.caption = caption; F.tornado = (float)torOn;
            return F;
        }
    }
}
