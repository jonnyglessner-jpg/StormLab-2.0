// Sampling from the SPC tornado climatology (tables in TornadoClimatology.Data.cs, baked from
// data/derived/tornado_climatology.json). The designed storm draws each tornado's rating, damage width and path length
// here, so the mix of tornadoes a player meets over many days matches the record for the region, shifted toward the
// strong end on days with a high significant-tornado parameter.
using System;

namespace StormLab.Supercell
{
    /// <summary>Climatology region (the SPC states grouped as in tools/spc_tornado_stats.py).</summary>
    public enum ClimoRegion { Plains = 0, Dixie = 1, Midwest = 2 }

    public static partial class TornadoClimatology
    {
        /// <summary>EF-scale 3-s gust bands, mph (lower and upper bound of each rating)</summary>
        public static readonly double[] EfLoMph = { 65, 86, 111, 136, 166, 201 }, EfHiMph = { 85, 110, 135, 165, 200, 250 };

        /// <summary>STP at which the regional rating mix is reproduced unchanged (a typical tornado-day value)</summary>
        public const double StpRef = 1.5;
        /// <summary>how strongly STP shifts the rating mix: weight of EF k is multiplied by exp(Beta k (ln(1+STP) - ln(1+StpRef)))</summary>
        public const double Beta = 0.9;

        /// <summary>Inverse CDF through the tabulated quantiles (Q), linear between them; tails run to 0.5 x p10 and 1.15 x p99.</summary>
        public static double SampleQuantiles(double[] v, double u)
        {
            u = C.Clamp(u, 0, 1);
            if (u <= Q[0]) return C.Lerp(0.5 * v[0], v[0], u / Q[0]);
            for (int i = 1; i < Q.Length; i++)
                if (u <= Q[i]) return C.Lerp(v[i - 1], v[i], (u - Q[i - 1]) / (Q[i] - Q[i - 1]));
            return C.Lerp(v[Q.Length - 1], 1.15 * v[Q.Length - 1], (u - Q[Q.Length - 1]) / (1 - Q[Q.Length - 1]));
        }

        static double Tilt(double stp) => Math.Log(1 + Math.Max(stp, 0)) - Math.Log(1 + StpRef);

        /// <summary>Probability of each rating EF0-EF5 for a tornado in this region on a day with this STP.</summary>
        public static double[] RatingWeights(ClimoRegion r, double stp)
        {
            var s = RatingShare[(int)r]; var w = new double[6]; double sum = 0, k0 = Tilt(stp);
            for (int k = 0; k < 6; k++) { w[k] = s[k] * Math.Exp(Beta * k * k0); sum += w[k]; }
            for (int k = 0; k < 6; k++) w[k] /= sum;
            return w;
        }

        public static int SampleEf(ClimoRegion r, double stp, double u)
        {
            var w = RatingWeights(r, stp); double c = 0;
            for (int k = 0; k < 5; k++) { c += w[k]; if (u < c) return k; }
            return 5;
        }

        /// <summary>Damage-path width, m, for this rating (quantile u).</summary>
        public static double SampleWidthM(ClimoRegion r, int ef, double u) => SampleQuantiles(WidthYd[(int)r][ef], u) * 0.9144;
        /// <summary>Path length, km, for this rating (quantile u).</summary>
        public static double SampleLengthKm(ClimoRegion r, int ef, double u) => SampleQuantiles(LengthMi[(int)r][ef], u) * 1.609344;
        /// <summary>Peak 3-s gust, m/s, within the rating's band (u across the band).</summary>
        public static double SampleVmax(int ef, double u) => C.Lerp(EfLoMph[ef], EfHiMph[ef], C.Clamp(u, 0, 1)) * C.MPH;
        /// <summary>Minutes from one tornado's end to the next in a cyclic family (quantile u).</summary>
        public static double SampleIntervalMin(ClimoRegion r, double u) => SampleQuantiles(IntervalMin[(int)r], u);

        /// <summary>Probability of 1, 2, 3 or 4 (4 = four or more) tornadoes in a family, tilted toward more on high-STP days.</summary>
        public static double[] FamilyWeights(ClimoRegion r, double stp)
        {
            var h = FamilySize[(int)r]; var w = new double[4]; double sum = 0, k0 = Tilt(stp);
            for (int i = 0; i < h.Length; i++) w[Math.Min(i, 3)] += h[i];
            for (int n = 0; n < 4; n++) { w[n] *= Math.Exp(0.45 * n * k0); sum += w[n]; }
            for (int n = 0; n < 4; n++) w[n] /= sum;
            return w;
        }

        public static int SampleFamilySize(ClimoRegion r, double stp, double u)
        {
            var w = FamilyWeights(r, stp); double c = 0;
            for (int n = 0; n < 3; n++) { c += w[n]; if (u < c) return n + 1; }
            return 4;
        }
    }

    /// <summary>Small, platform-stable random generator (SplitMix64), so a seed gives the same storm on every machine.</summary>
    public struct StormRng
    {
        ulong s;
        public StormRng(int seed, int stream = 0)
        {
            // hash seed and stream together first, so neighbouring seeds and streams give unrelated sequences
            s = Mix(Mix(((ulong)(uint)seed << 32) | (uint)stream) ^ 0xD1B54A32D192ED03UL);
        }
        static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        public ulong Next()
        {
            ulong z = s += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL; z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
        /// <summary>uniform in [0, 1)</summary>
        public double U() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        public double Range(double a, double b) => a + (b - a) * U();
        /// <summary>standard normal (Box-Muller)</summary>
        public double N() { double u1 = Math.Max(U(), 1e-12), u2 = U(); return Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
    }
}
