// The designed storm's wind, for carrying the skin's cloud (the brain's WindHistory in designed mode). An analytic,
// kinematic field on the same 2 km blocks x 1 km layers: the environment's wind profile, plus the storm's own
// circulation from the parameter block at that moment:
//   - the updraft (a Gaussian column, tilted with the shear, peaking mid-way up),
//   - low-level inflow converging on it and outflow spreading from it under the anvil,
//   - the mesocyclones as Rankine vortices (1 km at the wall cloud, 4 km at the tilted updraft), at their real v / r,
//   - the rear-flank surge and the forward-flank outflow spreading at the surface.
// Ground-relative, m/s, byte-quantised like the brain's (-51..51 m/s in 0.4 m/s steps). Pure C#: the Rendering
// KinematicWind wraps it in textures.
using System;
using StormLab.Supercell.Designer;

namespace StormLab.Supercell
{
    public sealed class KinematicWindField
    {
        public const double BlockKm = 2;
        public readonly DesignedStorm storm;
        /// <summary>environment km of grid (0, 0); blocks; layers (layer L centred at L km)</summary>
        public readonly Vec2 originKm;
        public readonly int NBX, NBY, NL;
        /// <summary>how far from the updraft the storm's own circulation is computed, km</summary>
        public double reachKm = 40;
        readonly SkinParams P = new SkinParams();

        public KinematicWindField(DesignedStorm storm, double marginKm = 50, int layers = 18)
        {
            this.storm = storm;
            storm.TrackBounds(marginKm, out var mn, out var mx);
            originKm = new Vec2(Math.Floor(mn.x / BlockKm) * BlockKm, Math.Floor(mn.y / BlockKm) * BlockKm);
            NBX = (int)Math.Ceiling((mx.x - originKm.x) / BlockKm); NBY = (int)Math.Ceiling((mx.y - originKm.y) / BlockKm);
            NL = layers;
        }

        public int Bytes => NBX * NBY * NL * 4;

        static byte Q(double v) => (byte)Math.Max(0, Math.Min(255, Math.Round((v + 51) / 0.4)));
        public static double Dq(byte b) => -51 + b * 0.4;
        static double Ss(double a, double b, double x) { double t = C.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }

        /// <summary>Fill dst (RGBA8, x fastest then y then layer: the Texture3D layout) with the wind at storm time t.</summary>
        public void Compute(double t, byte[] dst)
        {
            var E = storm.EnvironmentAt(t);
            var ch = storm.CharacterAt(t);
            storm.Evaluate(t, 0, new Vec2(0, 0), P);   // positions in environment km
            var up = P[Slot.Up]; var tilt = P[Slot.Tilt]; var tower = P[Slot.Tower]; var bas = P[Slot.Base];
            var wall = P[Slot.Wall]; var wall2 = P[Slot.Wall2]; var rfdb = P[Slot.Rfdb]; var rfd = P[Slot.Rfd]; var ffd = P[Slot.Ffd];
            double zb = bas.x, top = Math.Max(bas.y, zb + 2), R = Math.Max(tower.y, 0.8);
            double wmax = 0.8 * ch.w * C.Clamp((tower.z - 0.25) / 0.75, 0, 1);
            double vMeso4 = tilt.w * R * 1000, vMeso1 = wall2.y * wall.z * 0.6 * 1000 * wall.w;
            int nxy = NBX * NBY;
            for (int L = 0; L < NL; L++)
            {
                double z = L;
                double ue = WindProfile.At(E.w.u, z * 1000), ve = WindProfile.At(E.w.v, z * 1000);
                byte bu = Q(ue), bv = Q(ve), bw = Q(0);
                int o0 = L * nxy * 4;
                for (int k = 0; k < nxy; k++) { int o = o0 + 4 * k; dst[o] = bu; dst[o + 1] = bv; dst[o + 2] = bw; dst[o + 3] = 255; }
                if (tower.z <= 0) continue;
                // the storm's own circulation, near the updraft only
                double cx = up.x + tilt.x * Math.Max(z - zb, 0), cy = up.y + tilt.y * Math.Max(z - zb, 0);
                int i0 = Math.Max(0, (int)((cx - reachKm - originKm.x) / BlockKm)), i1 = Math.Min(NBX - 1, (int)((cx + reachKm - originKm.x) / BlockKm));
                int j0 = Math.Max(0, (int)((cy - reachKm - originKm.y) / BlockKm)), j1 = Math.Min(NBY - 1, (int)((cy + reachKm - originKm.y) / BlockKm));
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        double gx = originKm.x + (i + 0.5) * BlockKm, gy = originKm.y + (j + 0.5) * BlockKm;
                        Wind(gx, gy, z, ue, ve, cx, cy, zb, top, R, wmax, vMeso4, vMeso1, wall, rfdb, rfd, ffd, out double u, out double v, out double w);
                        int o = o0 + 4 * (j * NBX + i);
                        dst[o] = Q(u); dst[o + 1] = Q(v); dst[o + 2] = Q(w);
                    }
            }
        }

        static void Radial(double x, double y, double cx, double cy, out double ex, out double ey, out double r)
        {
            double dx = x - cx, dy = y - cy; r = Math.Sqrt(dx * dx + dy * dy);
            if (r < 1e-6) { ex = ey = 0; return; }
            ex = dx / r; ey = dy / r;
        }

        /// <summary>Wind (m/s) at (x, y, z) km.</summary>
        static void Wind(double x, double y, double z, double ue, double ve, double cx, double cy, double zb, double top, double R, double wmax,
            double vMeso4, double vMeso1, UnityEngine.Vector4 wall, UnityEngine.Vector4 rfdb, UnityEngine.Vector4 rfd, UnityEngine.Vector4 ffd,
            out double u, out double v, out double w)
        {
            u = ue; v = ve; w = 0;
            Radial(x, y, cx, cy, out double ex, out double ey, out double r);
            double g = Math.Exp(-(r / R) * (r / R));
            // updraft: a column peaking 40 % of the way up the cloud; air rises toward the base below it
            if (z >= zb)
            {
                // Join the sub-cloud inflow's 0.3 wmax at the LCL continuously, peak within the cloud,
                // then go to zero at the top. Starting a sine at zero here stopped rising air at cloud base.
                double phase = Math.Asin(0.3);
                w = wmax * g * Math.Sin(phase + (Math.PI - phase) * C.Clamp((z - zb) / (top - zb), 0, 1));
            }
            else w = 0.3 * wmax * g * z / Math.Max(zb, 0.3);
            // inflow converging on the updraft below the base (continuity for the 0.3 wmax at the base), and outflow
            // spreading under the anvil
            double fr = r > 1e-3 ? R * R / r * (1 - g) : 0;   // km
            if (z < zb + 0.5) { double vr = -0.3 * wmax / (2 * Math.Max(zb, 0.5)) * fr * (1 - Ss(zb - 0.5, zb + 0.5, z)); u += vr * ex; v += vr * ey; }
            if (z > top - 3) { double vr = wmax / 6 * fr * Ss(top - 3, top - 1, z); u += vr * ex; v += vr * ey; }
            // mid-level mesocyclone round the tilted updraft (counter-clockwise), from the base to 3 km below the top
            if (vMeso4 > 0 && z > zb && z < top - 1)
            {
                double vt = vMeso4 * (r < R ? r / R : R / r) * Ss(zb, zb + 1.5, z) * (1 - Ss(top - 3, top - 1, z));
                u += -vt * ey; v += vt * ex;
            }
            // low-level mesocyclone at the wall cloud
            if (vMeso1 > 0 && z < zb + 1.5 && wall.z > 0)
            {
                Radial(x, y, wall.x, wall.y, out double wx, out double wy, out double rw);
                double Rw = wall.z, vt = vMeso1 * (rw < Rw ? rw / Rw : Rw / rw) * (1 - Ss(zb, zb + 1.5, z));
                u += -vt * wy; v += vt * wx;
            }
            // surface outflow: the rear-flank surge (up to ~14 m/s) and the forward-flank outflow (~6 m/s)
            if (z < 1.5)
            {
                double low = 1 - Ss(0.3, 1.5, z);
                if (rfd.z > 0 && rfdb.z > 0)
                {
                    Radial(x, y, rfdb.x, rfdb.y, out double rx, out double ry, out double rr);
                    double s = rr / Math.Max(rfdb.z * 1.5, 0.5), vr = 14 * rfd.z * s * Math.Exp(1 - s) * low;
                    u += vr * rx; v += vr * ry;
                }
                if (ffd.w > 0)
                {
                    Radial(x, y, ffd.x, ffd.y, out double fx, out double fy, out double rf);
                    double s = rf / (6 * Math.Max(ffd.z, 0.5)), vr = 6 * Math.Min(ffd.w, 1.5) * s * Math.Exp(1 - s) * low;
                    u += vr * fx; v += vr * fy;
                }
            }
        }

        /// <summary>Decoded wind at grid position (m) and height (m) from a computed snapshot.</summary>
        public UnityEngine.Vector3 Sample(byte[] d, UnityEngine.Vector2 gridPos, float z)
        {
            int i = Math.Max(0, Math.Min(NBX - 1, (int)Math.Floor(gridPos.x / 1000 / BlockKm))), j = Math.Max(0, Math.Min(NBY - 1, (int)Math.Floor(gridPos.y / 1000 / BlockKm)));
            int L = Math.Max(0, Math.Min(NL - 1, (int)Math.Round(z / 1000)));
            int o = ((L * NBY + j) * NBX + i) * 4;
            return new UnityEngine.Vector3((float)Dq(d[o]), (float)Dq(d[o + 1]), (float)Dq(d[o + 2]));
        }
    }
}
