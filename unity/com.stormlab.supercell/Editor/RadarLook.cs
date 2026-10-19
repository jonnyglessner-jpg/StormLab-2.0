// A radar's view of the model storm, measured the way tools/fetch_nexrad.py measures a NEXRAD volume, so the model
// and KDDC can be compared number for number (DynamicsValidation, Dodge City 2016-05-24).
//   - reflectivity from the full-resolution rain, snow and hail with the model's own formula (Dbz in
//     SupercellDynamics.compute), averaged as linear Z onto the tool's 1 km x 1 km x 500 m grid (0 to 18 km), the
//     resolution the radar's Barnes gridding gives at 25-30 km range
//   - a 60 x 60 km box centred on the strongest echo at 500 m (the tool centres on the strongest echo of the lowest
//     sweep, about 0.5 km up at that range)
//   - the same echo tops, core areas and bounded-weak-echo (vault) search as the tool
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using StormLab.Supercell.Dynamics;
using UnityEngine;

namespace StormLab.Supercell.EditorTools
{
    /// <summary>The radar tool's numbers for one volume, model or NEXRAD. Heights in m AGL, areas in km^2, NaN = none.</summary>
    public class RadarVolume
    {
        public static readonly float[] Thresholds = { 40, 50, 55, 60, 65 };
        public float t = float.NaN;
        public float maxDbz = float.NaN, maxDbzZ = float.NaN;
        public float top18 = float.NaN, top30 = float.NaN, top50 = float.NaN;
        /// <summary>area at or above each of Thresholds at 1, 4 and 7 km</summary>
        public float[] area1 = new float[5], area4 = new float[5], area7 = new float[5];
        public bool vault;
        public float vaultBase = float.NaN, vaultTop = float.NaN, vaultDepth = float.NaN, vaultCap = float.NaN;
    }

    public static class RadarLook
    {
        const float DZ = 500f, ZTOP = 18000f, HALF = 30000f, NONE = -32f;
        const double T0 = 273.15, N0R = 8.0e6, N0S = 3.0e6, N0H = 4.0e4, RHOW = 1000, RHOSN = 100, RHOH = 900;

        /// <summary>linear reflectivity (mm^6/m^3) of one cell, as Dbz() in SupercellDynamics.compute</summary>
        static double ZLin(double qr, double qs, double qh, double rho, double T)
        {
            double zl = 0, fs = 0.224 * 0.01, fh = 0.224 * 0.81;
            if (T > T0) { fs = 0.01; fh = 0.81; }
            if (qr > 1e-8) zl += Math.Exp(Math.Log(720.0 * N0R) + 41.44653 - 1.75 * Math.Log(Math.PI * RHOW * N0R / (rho * qr)));
            if (qs > 1e-8) zl += fs * Math.Exp(Math.Log(720.0 * N0S) + 41.44653 - 1.75 * Math.Log(Math.PI * RHOSN * N0S / (rho * qs)));
            if (qh > 1e-8) zl += fh * Math.Exp(Math.Log(720.0 * N0H) + 41.44653 - 1.75 * Math.Log(Math.PI * RHOH * N0H / (rho * qh)));
            return zl;
        }

        public static RadarVolume Measure(SupercellModel m, float t)
        {
            var g = m.g; var b = m.b;
            var qr = m.Download(m.QR, Layout.Centre); var qs = m.Download(m.QS, Layout.Centre);
            var qh = m.Download(m.QH, Layout.Centre); var th = m.Download(m.TH, Layout.Centre);
            int F = Mathf.Max(1, Mathf.RoundToInt(1000f / g.dx)), NBX = g.NX / F, NBY = g.NY / F, NR = Mathf.RoundToInt(ZTOP / DZ) + 1;
            float bx = F * g.dx;
            // linear Z, block and 500 m bin averages weighted by each cell's vertical overlap with the bin
            var sz = new double[NR * NBY * NBX]; var sw = new double[NR];
            for (int k = 0; k < g.NZ; k++)
            {
                float lo = g.zw[k], hi = g.zw[k + 1];
                for (int r = 0; r < NR; r++)
                {
                    float blo = Mathf.Max(0f, r * DZ - 0.5f * DZ), bhi = r * DZ + 0.5f * DZ;
                    float ov = Mathf.Min(hi, bhi) - Mathf.Max(lo, blo); if (ov <= 0) continue;
                    double rho = b.rho0[k], pi = b.pi0[k];
                    for (int jb = 0; jb < NBY; jb++) for (int ib = 0; ib < NBX; ib++)
                    {
                        double s = 0;
                        for (int j = jb * F; j < jb * F + F; j++) for (int i = ib * F; i < ib * F + F; i++)
                        {
                            int c = (k * g.NY + j) * g.NX + i;
                            if (qr[c] > 1e-8f || qs[c] > 1e-8f || qh[c] > 1e-8f) s += ZLin(qr[c], qs[c], qh[c], rho, th[c] * pi);
                        }
                        sz[(r * NBY + jb) * NBX + ib] += s / (F * F) * ov;
                    }
                    sw[r] += ov;
                }
            }
            var Z = new float[NR * NBY * NBX];
            for (int r = 0; r < NR; r++) for (int q = 0; q < NBY * NBX; q++)
            {
                double z = sw[r] > 0 ? sz[r * NBY * NBX + q] / sw[r] : 0;
                Z[r * NBY * NBX + q] = z > 1e-3 ? (float)(10.0 * Math.Log10(z)) : NONE;
            }
            Func<int, int, int, float> At = (r, jb, ib) => Z[(r * NBY + jb) * NBX + ib];

            // box: 60 x 60 km around the strongest echo at 500 m
            int cr = 1, ci = 0, cj = 0; float best = float.MinValue;
            for (int jb = 0; jb < NBY; jb++) for (int ib = 0; ib < NBX; ib++) if (At(cr, jb, ib) > best) { best = At(cr, jb, ib); ci = ib; cj = jb; }
            int hb = Mathf.RoundToInt(HALF / bx);
            int i0 = Math.Max(0, ci - hb), i1 = Math.Min(NBX - 1, ci + hb), j0 = Math.Max(0, cj - hb), j1 = Math.Min(NBY - 1, cj + hb);
            var v = new RadarVolume { t = t };
            // column of the strongest composite echo
            int mi = ci, mj = cj; float comp = float.MinValue;
            for (int jb = j0; jb <= j1; jb++) for (int ib = i0; ib <= i1; ib++) for (int r = 0; r < NR; r++) if (At(r, jb, ib) > comp) { comp = At(r, jb, ib); mi = ib; mj = jb; }
            v.maxDbz = comp;
            { int kb = 0; float cm = float.MinValue; for (int r = 0; r < NR; r++) if (At(r, mj, mi) > cm) { cm = At(r, mj, mi); kb = r; } v.maxDbzZ = kb * DZ; }
            Func<float, float> Top = thr =>
            {
                for (int r = NR - 1; r >= 0; r--) for (int jb = j0; jb <= j1; jb++) for (int ib = i0; ib <= i1; ib++) if (At(r, jb, ib) >= thr) return r * DZ;
                return float.NaN;
            };
            v.top18 = Top(18); v.top30 = Top(30); v.top50 = Top(50);
            Action<float[], float> Areas = (a, z) =>
            {
                int r = Mathf.RoundToInt(z / DZ);
                for (int n = 0; n < a.Length; n++)
                {
                    int cnt = 0; for (int jb = j0; jb <= j1; jb++) for (int ib = i0; ib <= i1; ib++) if (At(r, jb, ib) >= RadarVolume.Thresholds[n]) cnt++;
                    a[n] = cnt * bx * bx / 1e6f;
                }
            };
            Areas(v.area1, 1000); Areas(v.area4, 4000); Areas(v.area7, 7000);
            // vault / BWER: within 12 km of the strongest column, weak echo (< 30 dBZ) at 2-8 km at least 15 dB below the
            // strongest echo above it, with at least 35 dBZ below it; the cap with the highest top wins (fetch_nexrad.py)
            int rad = Mathf.CeilToInt(12000f / bx);
            for (int jb = Math.Max(0, mj - rad); jb <= Math.Min(NBY - 1, mj + rad); jb++)
                for (int ib = Math.Max(0, mi - rad); ib <= Math.Min(NBX - 1, mi + rad); ib++)
                {
                    float dx = (ib - mi) * bx, dy = (jb - mj) * bx; if (dx * dx + dy * dy > 12000f * 12000f) continue;
                    for (int k = Mathf.RoundToInt(2000f / DZ); k < Mathf.RoundToInt(8000f / DZ); k++)
                    {
                        float above = NONE, below = NONE;
                        for (int r = k + 1; r < NR; r++) above = Mathf.Max(above, At(r, jb, ib));
                        for (int r = 0; r < k; r++) below = Mathf.Max(below, At(r, jb, ib));
                        float c = At(k, jb, ib);
                        if (!(above - c >= 15f && c < 30f && below >= 35f)) continue;
                        float depth = 0; int kk = k;
                        while (kk < NR - 1 && At(kk, jb, ib) < above - 10f) { depth += DZ; kk++; }
                        float top = k * DZ + depth;
                        if (!v.vault || top > v.vaultTop) { v.vault = true; v.vaultBase = k * DZ; v.vaultTop = top; v.vaultDepth = depth; v.vaultCap = above; }
                        break;
                    }
                }
            return v;
        }

        /// <summary>The same numbers from a tools/fetch_nexrad.py JSON file.</summary>
        public static RadarVolume FromJson(string json)
        {
            var v = new RadarVolume
            {
                maxDbz = Num(json, null, "max_dbz"), maxDbzZ = Num(json, null, "max_dbz_height_m"),
                top18 = Num(json, "echo_top_m", "18dBZ"), top30 = Num(json, "echo_top_m", "30dBZ"), top50 = Num(json, "echo_top_m", "50dBZ"),
            };
            for (int n = 0; n < 5; n++)
            {
                string key = $"{RadarVolume.Thresholds[n]:0}dBZ";
                v.area1[n] = Num(json, "core_area_km2_at_1000m", key); v.area4[n] = Num(json, "core_area_km2_at_4000m", key); v.area7[n] = Num(json, "core_area_km2_at_7000m", key);
            }
            v.vaultBase = Num(json, "bwer_vault", "base_m"); v.vaultTop = Num(json, "bwer_vault", "top_m");
            v.vaultDepth = Num(json, "bwer_vault", "depth_m"); v.vaultCap = Num(json, "bwer_vault", "cap_dbz");
            v.vault = !float.IsNaN(v.vaultDepth);
            return v;
        }

        static float Num(string json, string section, string key)
        {
            string body = json;
            if (section != null)
            {
                var ms = Regex.Match(json, "\"" + Regex.Escape(section) + "\"\\s*:\\s*\\{([^}]*)\\}");
                if (!ms.Success) return float.NaN;
                body = ms.Groups[1].Value;
            }
            var mk = Regex.Match(body, "\"" + Regex.Escape(key) + "\"\\s*:\\s*(-?[0-9.eE+]+)");
            return mk.Success ? float.Parse(mk.Groups[1].Value, CultureInfo.InvariantCulture) : float.NaN;
        }
    }
}
