// Procedural presentation model, not an electrification solver. No storm-genome RNG draws.
using System;
using System.Collections.Generic;
using System.Numerics;

namespace StormLab.Supercell.Lightning
{
    public enum FlashKind { InCloud, NegativeGround, PositiveGround }

    [Serializable]
    public sealed class LightningSettings
    {
        public int seed = 1701;
        public float flashesPerMinute = 12;
        public float groundFraction = .25f;
        // Conditional on a ground flash, deliberately configurable rather than tied to severity.
        public float positiveGroundFraction = .08f;
        public float cellRadiusM = 6000;
        public float lowerChargeHeightM = 5500;
        public float upperChargeHeightM = 9500;

        public void Validate()
        {
            if (!Finite(flashesPerMinute) || flashesPerMinute < 0 || flashesPerMinute > 600 ||
                !Finite(groundFraction) || groundFraction < 0 || groundFraction > 1 ||
                !Finite(positiveGroundFraction) || positiveGroundFraction < 0 || positiveGroundFraction > 1 ||
                !Finite(cellRadiusM) || cellRadiusM <= 0 ||
                !Finite(lowerChargeHeightM) || lowerChargeHeightM < 1000 ||
                !Finite(upperChargeHeightM) || upperChargeHeightM <= lowerChargeHeightM)
                throw new ArgumentOutOfRangeException(nameof(LightningSettings), "Invalid lightning rate, fraction or charge geometry.");
        }
        internal static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }

    public readonly struct FlashEvent
    {
        public readonly long id;
        public readonly double time;
        public readonly uint seed;
        public readonly FlashKind kind;
        public FlashEvent(long id, double time, uint seed, FlashKind kind)
        { this.id = id; this.time = time; this.seed = seed; this.kind = kind; }
    }

    // Ten-second bins have independent streams: seeking costs only the requested window,
    // and frame partitioning never changes the flashes. Settings must stay fixed for a replay.
    public static class LightningSchedule
    {
        public static void Query(LightningSettings settings, double after, double through, List<FlashEvent> output)
        {
            settings.Validate();
            if (double.IsNaN(after) || double.IsInfinity(after) || double.IsNaN(through) ||
                double.IsInfinity(through) || through < after || through - after > 3600 || through > 1e9)
                throw new ArgumentOutOfRangeException(nameof(through), "Query a finite window of at most one hour, up to 1e9 seconds.");
            output.Clear();
            if (through < 0 || settings.flashesPerMinute == 0) return;
            long first = Math.Max(0, (long)Math.Floor(after / 10));
            long last = (long)Math.Floor(through / 10);
            for (long bin = first; bin <= last; bin++)
            {
                var rng = new LightningRandom(LightningRandom.Hash((uint)settings.seed, (uint)bin));
                double t = bin * 10;
                int ordinal = 0;
                while (true)
                {
                    t += -Math.Log(rng.Unit()) * 60 / settings.flashesPerMinute;
                    if (t >= (bin + 1) * 10) break;
                    uint flashSeed = rng.Next();
                    var kind = rng.Unit() >= settings.groundFraction ? FlashKind.InCloud :
                        (rng.Unit() < settings.positiveGroundFraction ? FlashKind.PositiveGround : FlashKind.NegativeGround);
                    if (t > after && t <= through) output.Add(new FlashEvent(bin * 65536 + ordinal, t, flashSeed, kind));
                    ordinal++;
                }
            }
        }
    }

    internal struct LightningRandom
    {
        uint state;
        public LightningRandom(uint seed) { state = seed == 0 ? 1u : seed; }
        public uint Next() { uint x = state; x ^= x << 13; x ^= x >> 17; x ^= x << 5; return state = x; }
        public double Unit() => ((Next() >> 8) + .5) / 16777216.0;
        public float Range(float a, float b) => a + (b - a) * (float)Unit();
        public static uint Hash(uint a, uint b)
        {
            uint x = unchecked(a ^ (b * 0x9e3779b9u));
            x ^= x >> 16; x = unchecked(x * 0x7feb352du); x ^= x >> 15;
            x = unchecked(x * 0x846ca68bu); return x ^ (x >> 16);
        }
    }

    public sealed class LightningChannel
    {
        public readonly Vector3[] points;
        public readonly float brightness;
        public readonly bool main;
        public LightningChannel(Vector3[] points, float brightness, bool main)
        { this.points = points; this.brightness = brightness; this.main = main; }
    }

    /// <summary>Cloud-validated world-space anchors supplied by an optional renderer adapter.</summary>
    public readonly struct LightningAnchors
    {
        public readonly Vector3 origin, inCloudTarget;
        public LightningAnchors(Vector3 origin, Vector3 inCloudTarget)
        { this.origin = origin; this.inCloudTarget = inCloudTarget; }
    }

    public sealed class LightningFlash
    {
        public readonly FlashEvent Event;
        public readonly LightningChannel[] Channels;
        public readonly float[] StrokeTimes;
        public readonly Vector3 LightPosition;
        public const float LeaderSeconds = .035f;
        public float Duration => StrokeTimes[StrokeTimes.Length - 1] + .25f;

        LightningFlash(FlashEvent e, LightningChannel[] channels, float[] strokes, Vector3 lightPosition)
        { Event = e; Channels = channels; StrokeTimes = strokes; LightPosition = lightPosition; }

        /// <summary>Coordinates are world metres, y up. Ground is a flat plane; a terrain adapter may supply the endpoint height.</summary>
        public static LightningFlash Create(FlashEvent e, LightningSettings settings, Vector3 centre, float groundY,
            Func<Vector3, float> terrainHeight = null, LightningAnchors? anchors = null)
        {
            settings.Validate();
            var rng = new LightningRandom(e.seed);
            Vector3 offset = Disk(ref rng, settings.cellRadiusM * .65f);
            Vector3 start = centre + offset + Vector3.UnitY *
                (e.kind == FlashKind.PositiveGround ? settings.upperChargeHeightM : settings.lowerChargeHeightM);
            Vector3 end;
            if (e.kind == FlashKind.InCloud)
                end = centre + Disk(ref rng, settings.cellRadiusM) + Vector3.UnitY * settings.upperChargeHeightM;
            else
            {
                end = centre + offset + Disk(ref rng, settings.cellRadiusM * (e.kind == FlashKind.PositiveGround ? 1.7f : .65f));
            }
            if (anchors.HasValue)
            {
                var placement = anchors.Value;
                if (!Finite(placement.origin) || !Finite(placement.inCloudTarget) || placement.origin.Y <= groundY)
                    throw new ArgumentOutOfRangeException(nameof(anchors));
                if (e.kind == FlashKind.InCloud) end = placement.inCloudTarget;
                else { end.X += placement.origin.X - start.X; end.Z += placement.origin.Z - start.Z; }
                start = placement.origin;
                if (Vector3.DistanceSquared(start, end) < 1) throw new ArgumentOutOfRangeException(nameof(anchors));
            }
            if (e.kind != FlashKind.InCloud)
            {
                end.Y = terrainHeight == null ? groundY : terrainHeight(end);
                if (!LightningSettings.Finite(end.Y)) throw new ArgumentOutOfRangeException(nameof(terrainHeight));
            }
            var channels = new List<LightningChannel>();
            Vector3[] main = Path(start, end, ref rng, 128, .14f);
            for (int i = 1; i < main.Length - 1; i++)
                main[i].Y = Math.Max(main[i].Y, (terrainHeight == null ? groundY : terrainHeight(main[i])) + 5);
            channels.Add(new LightningChannel(main, 1, true));
            // Branches inherit a local direction; fine and broad bends are correlated at several scales.
            int branches = e.kind == FlashKind.PositiveGround ? 5 : 11;
            for (int b = 0; b < branches; b++)
            {
                int at = (int)rng.Range(12, 105);
                Vector3 direction = Vector3.Normalize(main[Math.Min(at + 5, 128)] - main[at]);
                Vector3 side = Disk(ref rng, 1);
                Vector3 tip = main[at] + (direction * .55f + side) * rng.Range(400, 1900);
                // Side leaders are unconnected: they must not accidentally become extra ground contacts.
                tip.Y = Math.Max(groundY + 80, tip.Y);
                var branch = Path(main[at], tip, ref rng, 32, .12f);
                for (int i = 1; i < branch.Length; i++)
                    branch[i].Y = Math.Max(branch[i].Y, (terrainHeight == null ? groundY : terrainHeight(branch[i])) + 20);
                channels.Add(new LightningChannel(branch, rng.Range(.12f, .42f), false));
            }
            int count = e.kind == FlashKind.NegativeGround ? (int)rng.Range(1, 6) :
                e.kind == FlashKind.InCloud ? (int)rng.Range(2, 5) : 1;
            var strokes = new float[count];
            strokes[0] = LeaderSeconds;
            for (int i = 1; i < count; i++) strokes[i] = strokes[i - 1] + rng.Range(.035f, .095f);
            return new LightningFlash(e, channels.ToArray(), strokes, start);
        }

        static bool Finite(Vector3 p) => LightningSettings.Finite(p.X) && LightningSettings.Finite(p.Y) && LightningSettings.Finite(p.Z);

        static Vector3 Disk(ref LightningRandom rng, float radius)
        {
            float angle = rng.Range(0, (float)Math.PI * 2), r = radius * (float)Math.Sqrt(rng.Unit());
            return new Vector3((float)Math.Cos(angle) * r, 0, (float)Math.Sin(angle) * r);
        }

        static Vector3[] Path(Vector3 a, Vector3 b, ref LightningRandom rng, int segments, float roughness)
        {
            var p = new Vector3[segments + 1]; p[0] = a; p[segments] = b;
            Vector3 axis = Vector3.Normalize(b - a);
            Vector3 u = Vector3.Normalize(Vector3.Cross(axis, Math.Abs(axis.Y) > .9f ? Vector3.UnitX : Vector3.UnitY));
            Vector3 v = Vector3.Cross(axis, u);
            float amplitude = Vector3.Distance(a, b) * roughness;
            for (int stride = segments; stride > 1; stride /= 2)
            {
                for (int i = 0; i < segments; i += stride)
                    p[i + stride / 2] = (p[i] + p[i + stride]) * .5f +
                        (u * rng.Range(-1, 1) + v * rng.Range(-1, 1)) * amplitude;
                amplitude *= .57f;
            }
            return p;
        }

        /// <summary>Peak radiance over an exposure interval, so a short stroke survives a dropped render frame.</summary>
        public float Intensity(double ageFrom, double ageThrough)
        {
            if (ageThrough < 0 || ageFrom > Duration || ageThrough < ageFrom) return 0;
            float peak = 0;
            for (int i = 0; i < StrokeTimes.Length; i++)
            {
                double age = Math.Max(ageFrom, StrokeTimes[i]) - StrokeTimes[i];
                if (ageThrough < StrokeTimes[i]) continue;
                float value = (float)Math.Exp(-age / (i == 0 ? .024 : .016)) * (i == 0 ? 1 : .72f);
                peak = Math.Max(peak, value);
            }
            if (Event.kind == FlashKind.PositiveGround && ageThrough >= LeaderSeconds)
                peak = Math.Max(peak, .12f * (float)Math.Exp(-Math.Max(0, ageFrom - LeaderSeconds) / .12));
            return peak;
        }

        public float LeaderProgress(double age) => (float)Math.Max(0, Math.Min(1, age / LeaderSeconds));
    }

    public static class LightningAcoustics
    {
        public static float SoundSpeed(float temperatureC) => 331.3f + .606f * temperatureC;
        public static float ArrivalSeconds(Vector3 channelPoint, Vector3 observer, float temperatureC = 20)
        {
            float speed = SoundSpeed(temperatureC);
            if (!LightningSettings.Finite(speed) || speed <= 0) throw new ArgumentOutOfRangeException(nameof(temperatureC));
            return Vector3.Distance(channelPoint, observer) / speed;
        }
    }
}
