// One timeline frame: the compact feature state the brain writes every 10 sim-seconds (format in TIMELINE.md).
// Positions are storm-relative grid coordinates in metres from the domain's south-west corner (+x east, +y north);
// the ground position of a point is (x, y) + frameMotion * t + the grid's ground origin.
using System;
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    [Serializable]
    public struct MesoState
    {
        /// <summary>true when the 1 km block-mean vorticity reaches 0.004 1/s within 15 km of the updraft</summary>
        public bool present;
        /// <summary>centre (vorticity-weighted over the 3 x 3 blocks around the peak), m</summary>
        public Vector2 pos;
        /// <summary>peak 1 km block-mean vertical vorticity, 1/s (the mesocyclone-scale number; CM1 and 1.0 report this at 1 km)</summary>
        public float zeta;
        /// <summary>peak grid-point vertical vorticity in those blocks, 1/s (resolution dependent)</summary>
        public float zetaPeak;
        /// <summary>radius of the region above half the peak, m</summary>
        public float radius;
    }

    [Serializable]
    public class TimelineFrame
    {
        // ---- clock and frame
        public int index;
        /// <summary>model time, s (index * 10)</summary>
        public float t;
        public long step;
        /// <summary>motion of the grid over the ground (the Bunkers right-mover or the given motion), m/s</summary>
        public Vector2 frameMotion;
        /// <summary>ground-relative storm motion: drift of stormCentroid over the last 10 minutes plus frameMotion, m/s</summary>
        public Vector2 stormMotion;
        /// <summary>the tracked storm cell: centroid of the 40 dBZ composite echo, followed from frame to frame, m (NaN before there is one)</summary>
        public Vector2 stormCentroid;

        // ---- updraft
        /// <summary>strongest updraft anywhere in the domain, m/s</summary>
        public float wMax;
        /// <summary>column of the strongest updraft (the updraft core), m</summary>
        public Vector2 updraftPos;
        /// <summary>height of the core's maximum w, m</summary>
        public float updraftZ;
        /// <summary>w at every model face level (NZ+1 values, heights in Timeline.zw) through the core, m/s</summary>
        public float[] wColumn;
        /// <summary>centroid of w &gt; 10 m/s on the 5 km layer within 12 km of the core, m (NaN when there is none)</summary>
        public Vector2 updraftCentroid5km;
        /// <summary>area of w &gt; 10 m/s on the 5 km layer, km^2</summary>
        public float updraftArea5km;

        // ---- mesocyclone
        public MesoState meso1, meso4;
        /// <summary>bottom and top of the column (around the 4 km meso) where 1 km block vorticity stays above 0.01 1/s, m; depth = top - base</summary>
        public float mesoBase, mesoTop, mesoDepth;
        /// <summary>peak grid-point vorticity below 500 m within 10 km of the low-level meso, 1/s (feeds the Phase 3 genesis test)</summary>
        public float zeta0Peak;
        /// <summary>strongest w on the 1 km layer within 5 km of the low-level meso, m/s (low-level stretching)</summary>
        public float w1UnderMeso;

        // ---- cold pool and gust front
        /// <summary>coldest lowest-level potential temperature perturbation, K</summary>
        public float coldPoolMin;
        /// <summary>coldest 1 km block-mean lowest-level theta' (K): the resolution-independent number, comparable with 1 km runs</summary>
        public float coldPoolMin1km;
        /// <summary>area colder than -1 K at the lowest level, km^2</summary>
        public float coldPoolArea;
        public Vector2 coldPoolCentroid;
        /// <summary>bounding box of the -1 K region (min x, min y, max x, max y), m</summary>
        public Vector4 coldPoolBox;
        /// <summary>gust front as a polar outline: distance (m) from coldPoolCentroid to the outer edge of the -1 K region along bearings 0, 10, ..., 350 degrees (0 = north, clockwise); 0 when there is no cold pool</summary>
        public float[] gustFront = new float[36];
        /// <summary>signed distance from the 1 km updraft (the low-level meso if present) to the -1 K edge, m: positive in warm inflow air, negative once the cold pool has undercut it</summary>
        public float updraftToGustFront;

        // ---- rear-flank downdraft
        public bool rfdPresent;
        public Vector2 rfdCentroid;
        /// <summary>area of the rear-flank downdraft (w &lt; -2 m/s on the 1 km layer, cold at the ground, not downshear of the meso), km^2</summary>
        public float rfdArea;
        public float rfdMinW, rfdMinTheta, rfdMeanTheta;
        /// <summary>farthest reach of the RFD from the low-level meso, m (grows during a surge)</summary>
        public float rfdReach;
        /// <summary>strongest ground-relative surface wind in the RFD, m/s</summary>
        public float rfdMaxWind;

        // ---- anvil and tops
        /// <summary>ice-weighted mean wind at 8-14 km, relative to the grid and relative to the storm, m/s</summary>
        public Vector2 anvilWindGrid, anvilWindSR;
        /// <summary>highest 18 dBZ and 50 dBZ echo, and highest cloud (qc + qi &gt; 0.01 g/kg), m</summary>
        public float echoTop18, echoTop50, cloudTop;
        public float maxDbz, maxHailMm, maxRainMm, maxHailAccMm;

        // ---- 1 km maps (whole domain), byte-quantised, see Map2D
        /// <summary>[field][j][i], Map2D.Count fields of Timeline.NCX x Timeline.NCY</summary>
        public byte[] map2D;
        // ---- 1 km volumes (storm-centred window), byte-quantised log mixing ratio, see Q3D
        /// <summary>south-west block of the window and its size in blocks</summary>
        public int winI0, winJ0, winNX, winNY;
        /// <summary>[field (qc, qr, qi, qh)][layer][j][i], layers centred on 0, 1, ..., NL-1 km</summary>
        public byte[] q3D;

        public bool valid;

        public long Bytes => 400 + (wColumn?.Length ?? 0) * 4 + (map2D?.Length ?? 0) + (q3D?.Length ?? 0);
    }

    /// <summary>The 2D 1 km maps in a frame: value = offset + byte * scale.</summary>
    public static class Map2D
    {
        public enum Field { CompositeDbz, Dbz1km, ThetaPrimeSfc, WMax, WMin3km, USfc, VSfc, HailSizeMm, RainMm }
        public const int Count = 9;
        public static readonly float[] Offset = { -20f, -20f, -16f, 0f, -51f, -51f, -51f, 0f, 0f };
        public static readonly float[] Scale = { 0.4f, 0.4f, 0.1f, 0.4f, 0.2f, 0.4f, 0.4f, 0.5f, 1f };
        public static byte Encode(Field f, float v) => (byte)Mathf.Clamp(Mathf.RoundToInt((v - Offset[(int)f]) / Scale[(int)f]), 0, 255);
        public static float Decode(Field f, byte b) => Offset[(int)f] + b * Scale[(int)f];
    }

    /// <summary>The 3D 1 km mixing ratios in a frame: 0 = below 1e-6 kg/kg, else q = 1e-6 * 10^((b - 1) / 56) (1e-6 .. 3.4e-2 kg/kg).</summary>
    public static class Q3D
    {
        public enum Field { Qc, Qr, Qi, Qh }
        public const int Count = 4;
        public static byte Encode(float q) => q < 1e-6f ? (byte)0 : (byte)Mathf.Clamp(Mathf.RoundToInt(1 + 56f * Mathf.Log10(q / 1e-6f)), 1, 255);
        public static float Decode(byte b) => b == 0 ? 0f : 1e-6f * Mathf.Pow(10f, (b - 1) / 56f);
    }
}
