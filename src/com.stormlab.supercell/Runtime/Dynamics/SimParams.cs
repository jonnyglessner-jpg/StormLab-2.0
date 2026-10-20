// Tunable model parameters and initial-condition descriptions (StormLab 1.0 src/sim/params.ts, start.ts, extended
// with the surface drag layer and the outflow boundary of DESIGN.md sections 2.1-2.2).
using System;
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    [Serializable]
    public class SimParams
    {
        /// <summary>time step, s</summary>
        public float dt = 2f;
        /// <summary>vertical eddy diffusivity for momentum and scalars, m^2/s</summary>
        public float kdiff = 40f;
        /// <summary>4th-order horizontal filter strength: fraction of a 2-gridpoint wave removed per step</summary>
        public float hyper = 0.08f;
        /// <summary>horizontal eddy diffusivity, m^2/s (500 at 1 km, in proportion to dx; set by ForGrid)</summary>
        public float kh = 167f;
        /// <summary>Kessler autoconversion rate (1/s) and threshold (kg/kg), accretion coefficient (1/s)</summary>
        public float k1 = 1e-3f, qcCrit = 1e-3f, k2 = 2.2f;
        /// <summary>lateral sponge width (cells) and strongest relaxation time (s)</summary>
        public float spongeCells = 8f, spongeTau = 300f;
        /// <summary>Rayleigh damping above dampZ (m) with time scale dampTau (s) at the model top</summary>
        public float dampZ = 13000f, dampTau = 300f;
        /// <summary>surface drag coefficient on the ground-relative wind in the lowest layer (0 = free slip, as 1.0)</summary>
        public float cd = 0.003f;
        /// <summary>closed lateral walls (no normal flow) instead of the default open boundary; for closed-box budget tests</summary>
        public bool closedLateral;
        /// <summary>rain intercept used for rain evaporation only, 1/m^4. 1.0 used 8e6 (the scheme value), which ran cold pools 2-4 K too cold against CM1; 1e6 brings the Weisman-Klemp cold pool from -11 to -9..-10 K (sensitivity.md)</summary>
        public float n0rEvap = 1e6f;
        /// <summary>rain intercept used for the rain fall speed, 1/m^4 (0 = the scheme's 8e6)</summary>
        public float n0rFall = 0f;
        /// <summary>diagnostic process switches for the sensitivity suite only (0 in every real run): 1 no rain evaporation, 2 no latent cooling from hail melting, 4 no hail sublimation, 8 snow melting with LFO's vapour term, 16 no latent cooling from snow melting</summary>
        public int microDiag = 0;
        /// <summary>wet-growth limit on hail (Lin et al. 1983): hail freezes only the collected water whose latent heat it can shed, the rest is shed as rain</summary>
        public bool hailWetGrowth = false;
        /// <summary>melting hail exchanges vapour with the air: what condenses on the 0 C stone (or evaporates from it) changes qv instead of being paid for by cooling the air (Rutledge and Hobbs 1983; Morrison et al. 2009)</summary>
        public bool hailMeltVapour = true;
        /// <summary>Smagorinsky-Lilly sub-grid mixing on top of the constant floors kh and kdiff (off = 1.0's constant mixing)</summary>
        public bool smagorinsky = true;
        public float smagCs = 0.25f;   // WRF default; 0.18 (CM1) gives nearly the same storms (sensitivity.md)

        public static SimParams ForGrid(SimGrid g)
        {
            var p = new SimParams();
            // time steps divide the 10 s timeline interval exactly: 1.67 s at 250 m, 2 s at 333 m, 2.5 s at 500 m, 5 s coarser
            p.dt = g.dx <= 260f ? 10f / 6f : g.dx <= 400f ? 2f : g.dx <= 700f ? 2.5f : 5f;
            p.kh = 500f * (g.dx / 1000f);
            p.spongeCells = Mathf.Clamp(Mathf.Round(2500f / g.dx), 4, 10);
            return p;
        }

        public SimParams Clone() => (SimParams)MemberwiseClone();
    }

    /// <summary>Warm bubble: cosine-squared theta excess keeping the environment's relative humidity.</summary>
    [Serializable]
    public struct Bubble
    {
        /// <summary>centre, m from the domain's south-west corner</summary>
        public float x, y, z;
        /// <summary>horizontal and vertical radius, m</summary>
        public float rh, rz;
        /// <summary>potential temperature excess, K</summary>
        public float dth;
        /// <summary>carry boundary-layer moisture up instead of the local humidity</summary>
        public bool moist;
    }

    /// <summary>One forced updraft (Naylor and Gilmore 2012, CM1 iinit = 12): w relaxed toward wmax inside an ellipsoid between t0 and t1.</summary>
    [Serializable]
    public struct Tower
    {
        public float x, y, z, rh, rz, wmax, t0, t1;
    }

    [Serializable]
    public class Nudge
    {
        public const int MaxTowers = 8;
        public Tower[] towers = new Tower[0];
        /// <summary>relaxation time, s</summary>
        public float tau = 2f;
    }

    /// <summary>
    /// Outflow boundary in the initial state (DESIGN.md 2.1): a straight line through (x, y) running in the compass
    /// direction `orientationDeg` (0 = the line runs south to north, 90 = west to east), with a cold pool of `depth` m
    /// and surface theta deficit `dTheta` K on its cold side. The cold side is to the left of the line's direction: for
    /// a 90-degree (west-east) line the cold air lies to the north, which is the usual layout of a leftover outflow
    /// boundary north of a Plains storm. The deficit decays linearly with height to zero at `depth` and blends to zero
    /// across `width` m centred on the line. Rain-cooled air keeps its mixing ratio (capped at 98 % RH).
    /// </summary>
    [Serializable]
    public class OutflowBoundary
    {
        public bool enabled;
        public float x, y;
        public float orientationDeg = 90f;
        public float depth = 1200f;
        public float dTheta = -4f;
        public float width = 3000f;
    }

    /// <summary>Everything that defines a run. Same setup + same seed = same storm.</summary>
    [Serializable]
    public class RunSetup
    {
        public SimTier tier = SimTier.Average;
        public int seed = 7;
        /// <summary>boundary-layer theta noise amplitude (K) and depth (m)</summary>
        public float noiseAmp = 0.1f, noiseTop = 1500f;
        /// <summary>forced updraft at 45 % across the domain, as in 1.0 and CM1. wmax and onset are set from CIN by Initiation.</summary>
        public bool nudgeEnabled = true;
        public float nudgeWmax = 10f, nudgeRadius = 10000f, nudgeDepth = 1500f, nudgeCentreZ = 1500f, nudgeDuration = 900f, nudgeTau = 2f;
        /// <summary>optional instantaneous warm bubble at the same spot (off by default; the nudge is the CM1 way)</summary>
        public bool bubbleEnabled;
        public float bubbleDth = 2f, bubbleRadius = 10000f, bubbleDepth = 1400f, bubbleZ = 1400f;
        public OutflowBoundary outflow = new OutflowBoundary();
        /// <summary>run length, s of model time</summary>
        public float runLength = 10800f;
    }
}
