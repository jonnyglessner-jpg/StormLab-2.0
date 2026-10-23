using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Rendering;
using UnityEngine;

namespace StormLab.Supercell.Rainfall
{
    /// <summary>Read-only bridge: recorded qr, base-state air density and ground-relative wind.
    /// Does not create precipitation from reflectivity or modify the dynamics.</summary>
    [DefaultExecutionOrder(130)]
    public sealed class BrainRainSource : MonoBehaviour, IRainWeatherSource
    {
        public StormSkin skin;
        public LocalRainfall rainfall;
        [Tooltip("Zero follows run.p.n0rFall (or the model's 8e6 default). Positive overrides N0 in m^-4.")]
        [Min(0)] public float rainIntercept;
        float previousTime = float.NaN;
        LocalRainfall bound;

        void OnEnable() { Bind(); }
        void Bind()
        {
            if (!rainfall) rainfall = GetComponent<LocalRainfall>();
            if (bound != rainfall)
            {
                if (bound && ReferenceEquals(bound.SourceOverride, this)) bound.SourceOverride = null;
                bound = rainfall;
                if (bound) bound.SourceOverride = this;
            }
        }
        void LateUpdate()
        {
            Bind();
            if (!skin || !bound) return;
            float expectedStep = skin.playing ? Mathf.Max(0, Time.deltaTime * skin.timelapse) : 0;
            if (PlaybackJump(previousTime, skin.playbackTime, expectedStep)) bound.ResetDrops();
            previousTime = skin.playbackTime;
            bound.groundHeight = skin.groundHeight;
        }

        /// <summary>A normal timelapse frame must not continually clear the local drop population.</summary>
        public static bool PlaybackJump(float previous, float current, float expectedStep)
        {
            return !float.IsNaN(previous) && (current < previous || current - previous > Mathf.Max(1, expectedStep + .25f));
        }
        void OnDisable()
        {
            if (bound && ReferenceEquals(bound.SourceOverride, this)) bound.SourceOverride = null;
            bound = null; previousTime = float.NaN;
        }

        public RainWeather Sample(Vector3 worldPosition, float timeSeconds)
        {
            var dry = new RainWeather { useMixingRatio = true, airDensity = 1.225f, interceptPerM4 = rainIntercept };
            var run = skin ? skin.Run : null;
            if (run == null || skin.Designed != null) return dry;
            float t = skin.playbackTime;
            var tl = run.timeline;
            if (!tl.Bracket(t, out var a, out var b, out float blend) || !a.valid || b == null || !b.valid) return dry;
            // Ground-relative world -> translating model grid. Add frame motion to grid-relative wind exactly once.
            Vector2 grid = new Vector2(worldPosition.x, worldPosition.z) - skin.groundOrigin - tl.frameMotion * t;
            float z = worldPosition.y - skin.groundHeight;
            if (z < 0 || z > run.g.depth || grid.x < 0 || grid.y < 0 || grid.x >= run.g.Lx || grid.y >= run.g.Ly) return dry;
            float qr = Mathf.Lerp(SampleQr(a, tl, grid, z), SampleQr(b, tl, grid, z), blend);
            Vector3 wind = skin.ActiveWind != null && skin.ActiveWind.Count > 0 ? skin.ActiveWind.Sample(t, grid, z) :
                new Vector3(ProfileAt(run.g.zc, run.b.u0, z), ProfileAt(run.g.zc, run.b.v0, z), 0);
            float rho = DensityAt(run, z);
            return new RainWeather {
                useMixingRatio = true, qrKgPerKg = qr, airDensity = rho,
                interceptPerM4 = rainIntercept > 0 ? rainIntercept : run.p.n0rFall > 0 ? run.p.n0rFall : 8e6f,
                windMps = new Vector3(wind.x + tl.frameMotion.x, wind.z, wind.y + tl.frameMotion.y)
            };
        }

        public static float DensityAt(StormRun run, float z)
        {
            return ProfileAt(run.g.zc, run.b.rho0, z);
        }

        static float ProfileAt(float[] h, float[] values, float z)
        {
            if (z <= h[0]) return values[0];
            for (int k = 1; k < h.Length; k++)
                if (z < h[k]) return Mathf.Lerp(values[k-1], values[k], Mathf.InverseLerp(h[k-1], h[k], z));
            return values[values.Length-1];
        }

        /// <summary>Trilinear interpolation of decoded mass, with zero outside the recorded horizontal
        /// window. Vertical layer zero is at the surface, as in HydrometeorHistory.</summary>
        public static float SampleQr(TimelineFrame f, Timeline tl, Vector2 grid, float height)
        {
            if (f == null || !f.valid || f.q3D == null || f.winNX <= 0 || f.winNY <= 0 ||
                f.q3D.Length != Q3D.Count * tl.NL * f.winNX * f.winNY) return 0;
            float x = grid.x / tl.blockSize - f.winI0 - .5f;
            float y = grid.y / tl.blockSize - f.winJ0 - .5f;
            float z = Mathf.Max(0, height / 1000);
            int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
            float sum = 0;
            for (int dz = 0; dz <= 1; dz++) for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++)
            {
                int i = ix+dx, j = iy+dy, k = iz+dz;
                if (i < 0 || i >= f.winNX || j < 0 || j >= f.winNY || k < 0 || k >= tl.NL) continue;
                int field = f.winNX * f.winNY * tl.NL;
                int at = field + (k*f.winNY+j)*f.winNX+i;
                sum += Q3D.Decode(f.q3D[at]) * (dx == 0 ? 1-(x-ix) : x-ix) *
                    (dy == 0 ? 1-(y-iy) : y-iy) * (dz == 0 ? 1-(z-iz) : z-iz);
            }
            return sum;
        }
    }
}
