// Base state for the cloud model (StormLab 1.0 src/sim/base.ts): a horizontally uniform, hydrostatic environment on
// the model's levels, with the storm motion subtracted from the winds so the storm stays near the middle of the domain.
using System;
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    /// <summary>Functions of height z (m AGL) describing the environment.</summary>
    public interface IProfile
    {
        double Theta(double z);
        double Qv(double z);
        double U(double z);
        double V(double z);
        /// <summary>surface pressure, Pa</summary>
        double Psfc { get; }
    }

    public class BaseState
    {
        /// <summary>at cell-centre levels k = 0..NZ-1</summary>
        public float[] th0, qv0, pi0, rho0, p0, u0, v0;
        /// <summary>density at w levels k = 0..NZ</summary>
        public float[] rhoW;
        /// <summary>storm motion removed from the winds (m/s): the grid moves over the ground at (cu, cv)</summary>
        public float cu, cv;
        public SimGrid g;

        public const double RD = 287.04, CP = 1005.7, G = 9.81, P00 = 1e5;

        /// <summary>Integrate the hydrostatic Exner equation with virtual temperature at 20 m resolution.</summary>
        public static BaseState FromProfile(SimGrid g, IProfile prof, double cu, double cv)
        {
            int NZ = g.NZ;
            var b = new BaseState
            {
                g = g, th0 = new float[NZ], qv0 = new float[NZ], pi0 = new float[NZ], rho0 = new float[NZ], p0 = new float[NZ],
                u0 = new float[NZ], v0 = new float[NZ], rhoW = new float[NZ + 1], cu = (float)cu, cv = (float)cv
            };
            double pi = Math.Pow(prof.Psfc / P00, RD / CP), z = 0;
            Func<double, double> thv = zz => prof.Theta(zz) * (1 + 0.608 * prof.Qv(zz));
            Func<double, double> piAt = zt =>
            {
                while (z < zt - 1e-6) { double h = Math.Min(20, zt - z); pi -= G / (CP * thv(z + 0.5 * h)) * h; z += h; }
                return pi;
            };
            Func<double, double, double> rhoFrom = (zz, p) =>
            {
                double T = prof.Theta(zz) * Math.Pow(p / P00, RD / CP);
                return p / (RD * T * (1 + 0.608 * prof.Qv(zz)));
            };
            for (int k = 0; k <= NZ; k++)
            {
                double zw = g.zw[k], piw = piAt(zw), pw = P00 * Math.Pow(piw, CP / RD);
                b.rhoW[k] = (float)rhoFrom(zw, pw);
                if (k < NZ)
                {
                    double zc = g.zc[k], pic = piAt(zc), pc = P00 * Math.Pow(pic, CP / RD);
                    b.th0[k] = (float)prof.Theta(zc); b.qv0[k] = (float)prof.Qv(zc); b.pi0[k] = (float)pic; b.p0[k] = (float)pc;
                    b.rho0[k] = (float)rhoFrom(zc, pc);
                    b.u0[k] = (float)(prof.U(zc) - cu); b.v0[k] = (float)(prof.V(zc) - cv);
                }
            }
            return b;
        }

        /// <summary>Base state from a 1.0-style environment (preset), moving with its Bunkers right-mover.</summary>
        public static BaseState FromEnvironment(SimGrid g, Environment E) =>
            FromProfile(g, new EnvironmentProfile(E.s, E.w), E.bk.rm.x, E.bk.rm.y);

        /// <summary>Base state from a sounding/wind pair on the 100 m environment grid, moving with a given motion.</summary>
        public static BaseState FromSounding(SimGrid g, Sounding s, Winds w, Vec2 motion) =>
            FromProfile(g, new EnvironmentProfile(s, w), motion.x, motion.y);

        /// <summary>Saturation mixing ratio over water (Tetens), as the model's shader uses it.</summary>
        public static double QsatW(double T, double p)
        {
            double es = 610.78 * Math.Exp(17.27 * (T - 273.16) / (T - 35.86));
            return 0.622 * es / Math.Max(p - es, 1);
        }
    }

    /// <summary>Profile sampled from the environment layer's 100 m arrays (StormPhysics.cs Sounding + Winds).</summary>
    public class EnvironmentProfile : IProfile
    {
        readonly Sounding s; readonly Winds w; readonly double[] theta;
        public EnvironmentProfile(Sounding s, Winds w)
        {
            this.s = s; this.w = w;
            theta = new double[s.z.Length];
            for (int k = 0; k < s.z.Length; k++) theta[k] = s.T[k] * Math.Pow(BaseState.P00 / s.p[k], BaseState.RD / BaseState.CP);
        }
        double ZTop => s.z[s.z.Length - 1];
        /// <summary>Above the 16 km top of the environment grid: isothermal (theta grows as exp(g dz / (cp T))), dry.</summary>
        public double Theta(double z)
        {
            int n = s.z.Length - 1;
            if (z <= ZTop) return WindProfile.At(theta, z);
            return theta[n] * Math.Exp(BaseState.G * (z - ZTop) / (BaseState.CP * s.T[n]));
        }
        public double Qv(double z) => z <= ZTop ? WindProfile.At(s.q, z) : Math.Min(s.q[s.z.Length - 1], 5e-6);
        public double U(double z) => WindProfile.At(w.u, z);
        public double V(double z) => WindProfile.At(w.v, z);
        public double Psfc => s.p[0];
    }

    /// <summary>Simple analytic profiles for tests.</summary>
    public class FuncProfile : IProfile
    {
        public Func<double, double> theta, qv, u, v; public double psfc = 1e5;
        public double Theta(double z) => theta(z);
        public double Qv(double z) => qv(z);
        public double U(double z) => u(z);
        public double V(double z) => v(z);
        public double Psfc => psfc;
    }

    /// <summary>
    /// Weisman and Klemp (1982) analytic sounding with a quarter-circle hodograph (Weisman and Rotunno 2000),
    /// the standard idealised supercell environment, as in 1.0's base.ts.
    /// </summary>
    public class WeismanKlempProfile : IProfile
    {
        readonly double qvPbl, shear, R, U6;
        const double zt = 12000, tht = 343, Ttr = 213, ths = 300;
        public WeismanKlempProfile(double qvPbl, double shear)
        {
            this.qvPbl = qvPbl; this.shear = shear;
            R = 7 * shear / 31.8; U6 = Math.Sqrt(Math.Max(shear * shear - R * R, 0));
        }
        public double Theta(double z) => z < zt ? ths + (tht - ths) * Math.Pow(z / zt, 1.25) : tht * Math.Exp(BaseState.G / (BaseState.CP * Ttr) * (z - zt));
        double Rh(double z) => z < zt ? 1 - 0.75 * Math.Pow(z / zt, 1.25) : 0.25;
        double PAt(double z)
        {
            double pi = 1, zz = 0;
            while (zz < z) { double h = Math.Min(50, z - zz); pi -= BaseState.G / (BaseState.CP * Theta(zz + h / 2)) * h; zz += h; }
            return BaseState.P00 * Math.Pow(pi, BaseState.CP / BaseState.RD);
        }
        public double Qv(double z)
        {
            double p = PAt(z), T = Theta(z) * Math.Pow(p / BaseState.P00, BaseState.RD / BaseState.CP);
            double es = 611.2 * Math.Exp(17.67 * (T - 273.15) / (T - 29.65));
            return Math.Min(Rh(z) * 0.622 * es / (p - es), qvPbl);
        }
        public double U(double z) => z <= 2000 ? R - R * Math.Cos(Math.PI / 2 * z / 2000) : R + (Math.Min(z, 6000) - 2000) / 4000 * (U6 - R);
        public double V(double z) => z <= 2000 ? R * Math.Sin(Math.PI / 2 * z / 2000) : R;
        public double Psfc => 1e5;

        /// <summary>Bunkers right-mover motion of this hodograph (0-6 km mean wind + 7.5 m/s to the right of the shear).</summary>
        public Vec2 BunkersRightMover()
        {
            var w = new Winds();
            for (int k = 0; k < C.NZ; k++) { w.u[k] = U(k * C.DZ); w.v[k] = V(k * C.DZ); }
            return WindProfile.MakeBunkers(w).rm;
        }

        /// <summary>The profile on the environment layer's 100 m grid, for CAPE/CIN and Bunkers motion.</summary>
        public void ToEnvironmentGrid(out Sounding s, out Winds w)
        {
            s = new Sounding(); w = new Winds();
            for (int k = 0; k < C.NZ; k++)
            {
                double z = k * C.DZ, p = PAt(z);
                s.z[k] = z; s.p[k] = p; s.T[k] = Theta(z) * Math.Pow(p / BaseState.P00, BaseState.RD / BaseState.CP); s.q[k] = Qv(z);
                w.u[k] = U(z); w.v[k] = V(z);
            }
            s.kt = (int)Math.Round(zt / C.DZ); s.ztrop = zt;
        }
    }
}
