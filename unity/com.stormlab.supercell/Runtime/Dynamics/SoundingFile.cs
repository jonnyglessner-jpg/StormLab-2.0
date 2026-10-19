// Real soundings as model input (DESIGN.md 2.1). Reads the JSON written by tools/fetch_sounding.py
// (data/raw/soundings/*.json: a list of levels with p hPa, z m MSL, T C, Td C, dir deg, spd_kt) and puts it on the
// environment layer's 100 m grid so the existing CAPE/SRH/Bunkers code (StormPhysics.cs) and the model's base state
// both read it the same way.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    [Serializable]
    public class SoundingLevel { public float p, z, T, Td, dir, spd_kt; }

    [Serializable]
    public class SoundingDocument
    {
        public string source;
        public string @case;
        public string valid_utc;
        public SoundingLevel[] levels;
    }

    public static class SoundingFile
    {
        public static SoundingDocument Parse(string json) => JsonUtility.FromJson<SoundingDocument>(json);

        /// <summary>
        /// Interpolate a sounding to the 100 m environment grid (heights AGL, the first level is the surface).
        /// Above the top reported level the temperature is held constant and the humidity and wind are held at
        /// their last values; the model's 18 km top is usually inside the data (HRRR goes to 50 hPa).
        /// </summary>
        public static void ToEnvironmentGrid(SoundingDocument doc, out Sounding s, out Winds w)
        {
            var lv = new List<SoundingLevel>(doc.levels);
            lv.Sort((a, b) => b.p.CompareTo(a.p));
            // drop duplicate / non-monotonic heights
            var clean = new List<SoundingLevel>();
            foreach (var l in lv) if (clean.Count == 0 || l.z > clean[clean.Count - 1].z + 0.5f) clean.Add(l);
            if (clean.Count < 3) throw new ArgumentException("sounding needs at least 3 levels");
            int n = clean.Count;
            double z0 = clean[0].z;
            var zz = new double[n]; var th = new double[n]; var qq = new double[n]; var uu = new double[n]; var vv = new double[n]; var lnp = new double[n];
            for (int i = 0; i < n; i++)
            {
                var l = clean[i];
                double p = l.p * 100.0, T = l.T + 273.15, Td = Math.Min(l.Td, l.T) + 273.15;
                zz[i] = l.z - z0; lnp[i] = Math.Log(p);
                th[i] = T * Math.Pow(C.P00 / p, C.RD / C.CP);
                qq[i] = Thermo.Qsat(p, Td);
                var V = WindProfile.FromDir(l.dir, l.spd_kt * C.KT);
                uu[i] = V.x; vv[i] = V.y;
            }
            s = new Sounding(); w = new Winds();
            for (int k = 0; k < C.NZ; k++)
            {
                double z = k * C.DZ; s.z[k] = z;
                double theta = Interp(zz, th, z), q = Interp(zz, qq, z);
                s.q[k] = q; w.u[k] = Interp(zz, uu, z); w.v[k] = Interp(zz, vv, z);
                double p = z <= zz[n - 1] ? Math.Exp(Interp(zz, lnp, z)) : 0;
                if (z > zz[n - 1])
                {
                    // above the data: hold temperature, integrate pressure hydrostatically from the last level
                    double Ttop = (th[n - 1]) * Math.Pow(Math.Exp(lnp[n - 1]) / C.P00, C.RD / C.CP);
                    p = Math.Exp(lnp[n - 1]) * Math.Exp(-C.G * (z - zz[n - 1]) / (C.RD * Ttop));
                    theta = Ttop * Math.Pow(C.P00 / p, C.RD / C.CP);
                    s.q[k] = Math.Min(q, Thermo.Qsat(p, Ttop) * 0.3);
                }
                s.p[k] = p;
                s.T[k] = theta * Math.Pow(p / C.P00, C.RD / C.CP);
            }
            RemoveSuperadiabatic(s);
            // tropopause: coldest level
            int kt = 0; for (int k = 1; k < C.NZ; k++) if (s.T[k] < s.T[kt]) kt = k;
            s.kt = kt; s.ztrop = s.z[kt];
        }

        /// <summary>height below which superadiabatic layers are mixed out, m</summary>
        public const double MixDepth = 1500;

        /// <summary>
        /// Model and analysis soundings start at the 2 m temperature, a skin value that is often several K warmer than
        /// the air 100-200 m up (the HRRR point for Dodge City 2016-05-24 is 3.6 K superadiabatic in its lowest 170 m).
        /// Used as a model base state, that layer is statically unstable everywhere and its surface parcels carry
        /// hundreds of J/kg of CAPE the storm's inflow does not have. As in usual CM1 practice with real soundings, any
        /// layer below 1.5 km where theta falls with height is mixed to its mean theta and mixing ratio (a convective
        /// adjustment, repeated until theta never decreases upward). Returns the number of levels changed.
        /// </summary>
        public static int RemoveSuperadiabatic(Sounding s)
        {
            int kTop = Math.Min(C.NZ - 1, (int)Math.Round(MixDepth / C.DZ));
            var th = new double[kTop + 1];
            for (int k = 0; k <= kTop; k++) th[k] = s.T[k] * Math.Pow(C.P00 / s.p[k], C.RD / C.CP);
            var orig = (double[])th.Clone();
            bool changed = true;
            for (int pass = 0; pass < 100 && changed; pass++)
            {
                changed = false;
                for (int k = 1; k <= kTop; k++)
                {
                    if (th[k] >= th[k - 1] - 1e-9) continue;
                    // grow the layer downward and upward until its mean is stable against both neighbours
                    int a = k - 1, b = k;
                    double mt, mq;
                    while (true)
                    {
                        double w = 0; mt = 0; mq = 0;
                        for (int j = a; j <= b; j++) { double wj = s.p[j]; w += wj; mt += wj * th[j]; mq += wj * s.q[j]; }
                        mt /= w; mq /= w;
                        if (a > 0 && th[a - 1] > mt) { a--; continue; }
                        if (b < kTop && th[b + 1] < mt) { b++; continue; }
                        break;
                    }
                    for (int j = a; j <= b; j++) { th[j] = mt; s.q[j] = mq; }
                    changed = true;
                }
            }
            int n = 0;
            for (int k = 0; k <= kTop; k++)
            {
                if (Math.Abs(th[k] - orig[k]) > 1e-6) n++;
                s.T[k] = th[k] * Math.Pow(s.p[k] / C.P00, C.RD / C.CP);
                s.q[k] = Math.Min(s.q[k], Thermo.Qsat(s.p[k], s.T[k]));
            }
            return n;
        }

        static double Interp(double[] x, double[] y, double v)
        {
            int n = x.Length;
            if (v <= x[0]) return y[0];
            if (v >= x[n - 1]) return y[n - 1];
            int i = 0; while (i < n - 2 && x[i + 1] <= v) i++;
            double t = (v - x[i]) / (x[i + 1] - x[i]);
            return y[i] + (y[i + 1] - y[i]) * t;
        }

        /// <summary>Environment diagnostics (CAPE, CIN, SRH, Bunkers motion, STP ...) for a real sounding.</summary>
        public static Environment ToEnvironment(SoundingDocument doc)
        {
            ToEnvironmentGrid(doc, out var s, out var w);
            return Environment.FromProfiles(s, w);
        }
    }
}
