// Storm initiation (DESIGN.md 2.2): a forced updraft in the CM1 style (Naylor and Gilmore 2012, iinit = 12, as in
// StormLab 1.0's start.ts) whose onset waits on the environment's convective inhibition, so a capped day really waits.
using System;

namespace StormLab.Supercell.Dynamics
{
    public static class Initiation
    {
        /// <summary>CIN the forcing is assumed to erode per hour before the cap breaks (afternoon heating plus mesoscale lift), J/kg/h.</summary>
        public const double CinErosionPerHour = 60;
        /// <summary>CIN weaker than this breaks at once, J/kg.</summary>
        public const double CinFree = 25;
        /// <summary>CIN stronger than this holds for the whole run: no storm, J/kg.</summary>
        public const double CinHolds = 250;
        /// <summary>CAPE below this makes nothing worth forcing, J/kg.</summary>
        public const double CapeMin = 100;

        public struct Plan
        {
            public bool fires;
            /// <summary>model time at which the forced updraft switches on, s</summary>
            public float onset;
            /// <summary>target updraft of the forcing, m/s</summary>
            public float wmax;
            public double cape, cin;
            public string reason;
            public override string ToString() => fires
                ? $"fires at {onset / 60:0} min (MLCAPE {cape:0}, MLCIN {cin:0} J/kg), forced w {wmax:0.0} m/s"
                : $"no initiation: {reason} (MLCAPE {cape:0}, MLCIN {cin:0} J/kg)";
        }

        /// <summary>
        /// Onset from mixed-layer CIN (negative, J/kg): immediate when |CIN| &lt;= 25; otherwise after the time the
        /// forcing needs to erode the excess at 60 J/kg per hour (|CIN| 100 waits 75 min); never when |CIN| &gt; 250.
        /// The forced updraft is stronger under a stronger cap: wmax = max(10, 0.8 sqrt(2 |CIN|)) m/s.
        /// </summary>
        public static Plan FromEnvironment(double mlcape, double mlcin, float runLength)
        {
            var p = new Plan { cape = mlcape, cin = mlcin };
            double c = Math.Max(0, -mlcin);
            if (mlcape < CapeMin) { p.reason = "too little CAPE"; return p; }
            if (c > CinHolds) { p.reason = "the cap holds"; return p; }
            double onset = c <= CinFree ? 0 : (c - CinFree) / CinErosionPerHour * 3600;
            if (onset >= runLength) { p.reason = "the cap would break after the end of the run"; return p; }
            p.fires = true; p.onset = (float)onset; p.wmax = (float)Math.Max(10, 0.8 * Math.Sqrt(2 * c));
            return p;
        }

        public static Plan FromEnvironment(Environment E, float runLength) => FromEnvironment(E.pc.cape, E.pc.cin, runLength);

        /// <summary>The CM1-style forced updraft: one broad tower at 45 % across the domain, 1.5 km up, for 15 minutes.</summary>
        public static Nudge MakeNudge(SimGrid g, RunSetup s, Plan plan)
        {
            var n = new Nudge { tau = s.nudgeTau };
            if (!plan.fires || !s.nudgeEnabled) return n;
            n.towers = new[]
            {
                new Tower
                {
                    x = g.Lx * 0.45f, y = g.Ly * 0.5f, z = s.nudgeCentreZ, rh = s.nudgeRadius, rz = s.nudgeDepth,
                    wmax = Math.Max(s.nudgeWmax, plan.wmax), t0 = plan.onset, t1 = plan.onset + s.nudgeDuration
                }
            };
            return n;
        }
    }
}
