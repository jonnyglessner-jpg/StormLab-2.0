// Storm-scale dynamics grid (DESIGN.md §2.2). Arakawa C staggering as in StormLab 1.0 (src/sim/grid.ts):
//   scalars at cell centres            NX x NY x NZ
//   u at x-faces                       (NX+1) x NY x NZ
//   v at y-faces                       NX x (NY+1) x NZ
//   w at z-faces                       NX x NY x (NZ+1)
// Index order: x fastest, then y, then z. The vertical grid is stretched: dzMin in the lowest zFine metres,
// then a geometric stretch up to dzMax, chosen so NZ levels span exactly the domain depth.
using System;
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    public enum SimTier { Average, High, Test }

    [Serializable]
    public class SimGrid
    {
        public int NX, NY, NZ;
        /// <summary>horizontal spacing, m</summary>
        public float dx, dy;
        /// <summary>domain depth, m</summary>
        public float depth;
        /// <summary>vertical spacing in the lowest zFine metres, m</summary>
        public float dzMin = 100f;
        public float zFine = 1500f;

        /// <summary>heights of cell centres (NZ) and faces (NZ+1), m</summary>
        [NonSerialized] public float[] zc, zw;
        /// <summary>cell heights dzc[k] = zw[k+1]-zw[k] (NZ) and centre spacing across faces dzw[k] = zc[k]-zc[k-1] (NZ+1, ends duplicated)</summary>
        [NonSerialized] public float[] dzc, dzw;
        /// <summary>largest cell height aloft, m (reported)</summary>
        [NonSerialized] public float dzMax;

        public const float ZTabStep = 10f;
        /// <summary>per ZTabStep-metre bin: the highest level at or below the bin's floor, for cell centres and for faces (the shader finishes the lookup exactly)</summary>
        [NonSerialized] public float[] kOfZc, kOfZw;

        public static SimGrid Make(int nx, int ny, int nz, float dx, float depth = 18000f, float dzMin = 100f, float zFine = 1500f, bool uniform = false)
        {
            var g = new SimGrid { NX = nx, NY = ny, NZ = nz, dx = dx, dy = dx, depth = depth, dzMin = dzMin, zFine = zFine };
            g.BuildVertical(uniform);
            return g;
        }

        /// <summary>DESIGN.md §3 tiers. Average: 240x240x72 at 333 m; High: 320x320x90 at 250 m; Test: a quick small grid.</summary>
        public static SimGrid ForTier(SimTier t)
        {
            switch (t)
            {
                case SimTier.High: return Make(320, 320, 90, 250f);
                case SimTier.Test: return Make(64, 64, 36, 1250f, 18000f, 250f, 1500f);
                default: return Make(240, 240, 72, 80000f / 240f);
            }
        }

        public int Cells => NX * NY * NZ;
        public int NU => (NX + 1) * NY * NZ;
        public int NV => NX * (NY + 1) * NZ;
        public int NW => NX * NY * (NZ + 1);
        public int Plane => NX * NY;
        public float Lx => NX * dx;
        public float Ly => NY * dy;

        /// <summary>
        /// Vertical levels: nFine levels of dzMin, then dz grows geometrically by a factor r per level until dzMax,
        /// then dzMax to the top. r is fixed at 1.08; dzMax is solved by bisection so the column is exactly `depth`.
        /// </summary>
        void BuildVertical(bool uniform)
        {
            zw = new float[NZ + 1]; zc = new float[NZ]; dzc = new float[NZ]; dzw = new float[NZ + 1];
            if (uniform)
            {
                float d = depth / NZ;
                for (int k = 0; k <= NZ; k++) zw[k] = k * d;
                dzMax = d;
            }
            else
            {
                double lo = dzMin, hi = depth, r = 1.08;
                for (int it = 0; it < 60; it++)
                {
                    double mid = 0.5 * (lo + hi);
                    if (Column(mid, r, null) > depth) hi = mid; else lo = mid;
                }
                dzMax = (float)(0.5 * (lo + hi));
                Column(dzMax, r, zw);
                // remove the bisection residual by scaling the stretched part so the top is exactly `depth`
                float top = zw[NZ];
                if (Mathf.Abs(top - depth) > 1e-3f)
                {
                    int kf = 0; while (kf < NZ && zw[kf + 1] <= zFine + 1e-3f) kf++;
                    float s = (depth - zw[kf]) / (top - zw[kf]);
                    for (int k = kf + 1; k <= NZ; k++) zw[k] = zw[kf] + (zw[k] - zw[kf]) * s;
                }
            }
            for (int k = 0; k < NZ; k++) { zc[k] = 0.5f * (zw[k] + zw[k + 1]); dzc[k] = zw[k + 1] - zw[k]; }
            for (int k = 1; k < NZ; k++) dzw[k] = zc[k] - zc[k - 1];
            dzw[0] = dzw[1]; dzw[NZ] = dzw[NZ - 1];
            int nt = Mathf.CeilToInt(depth / ZTabStep) + 1;
            kOfZc = new float[nt]; kOfZw = new float[nt];
            for (int n = 0; n < nt; n++)
            {
                float z = Mathf.Min(n * ZTabStep, depth);
                kOfZc[n] = Below(zc, z); kOfZw[n] = Below(zw, z);
            }
        }

        static int Below(float[] levels, float z)
        {
            int k = 0; while (k + 1 < levels.Length && levels[k + 1] <= z) k++;
            return k;
        }

        double Column(double dzMaxTry, double r, float[] outZw)
        {
            double z = 0, dz = dzMin;
            if (outZw != null) outZw[0] = 0;
            for (int k = 0; k < NZ; k++)
            {
                if (z + 1e-6 >= zFine) dz = Math.Min(dz * r, dzMaxTry);
                z += dz;
                if (outZw != null) outZw[k + 1] = (float)z;
            }
            return z;
        }

        static float FracIndex(float[] levels, float z)
        {
            int n = levels.Length;
            if (z <= levels[0]) return 0f;
            if (z >= levels[n - 1]) return n - 1f;
            int k = 0; while (k < n - 2 && levels[k + 1] <= z) k++;
            return k + (z - levels[k]) / (levels[k + 1] - levels[k]);
        }

        /// <summary>Fractional cell-centre level index of a height (CPU twin of the shader's table lookup).</summary>
        public float KcOfZ(float z) => FracIndex(zc, z);
        public float KwOfZ(float z) => FracIndex(zw, z);
        /// <summary>Index of the cell-centre level nearest to z.</summary>
        public int KcNearest(float z) => Mathf.Clamp(Mathf.RoundToInt(FracIndex(zc, z)), 0, NZ - 1);
        public int KwNearest(float z) => Mathf.Clamp(Mathf.RoundToInt(FracIndex(zw, z)), 0, NZ);

        public override string ToString() => $"{NX}x{NY}x{NZ}, dx {dx:0} m, dz {dzMin:0}-{dzMax:0} m, {Lx / 1000:0} x {Ly / 1000:0} x {depth / 1000:0} km, {Cells / 1e6:0.0} M cells";
    }
}
