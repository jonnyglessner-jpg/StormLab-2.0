// The designed storm's wind as the skin reads it (IWindSource): KinematicWindField computed every `interval` storm
// seconds, on demand, and the two snapshots around the playback time uploaded to the GPU and blended there, as the
// brain's WindHistory does. Grid (0, 0) is the field's south-west corner, OriginKm in the environment's frame.
using System.Collections.Generic;
using UnityEngine;

namespace StormLab.Supercell.Rendering
{
    public sealed class KinematicWind : IWindSource
    {
        public readonly KinematicWindField field;
        /// <summary>storm seconds between snapshots</summary>
        public float interval = 10f;
        readonly Dictionary<long, byte[]> cache = new Dictionary<long, byte[]>();
        readonly Queue<long> order = new Queue<long>();
        long upA = long.MinValue, upB = long.MinValue;

        public Texture3D A { get; }
        public Texture3D Bt { get; }
        public float Blend { get; private set; }
        public float Lx => field.NBX * (float)KinematicWindField.BlockKm * 1000f;
        public float Ly => field.NBY * (float)KinematicWindField.BlockKm * 1000f;
        public int NL => field.NL;
        public int Count => 1;
        public Vector2 OriginKm => new Vector2((float)field.originKm.x, (float)field.originKm.y);

        public KinematicWind(DesignedStorm storm)
        {
            field = new KinematicWindField(storm);
            A = NewTex("kinematic wind A"); Bt = NewTex("kinematic wind B");
        }

        Texture3D NewTex(string n) => new Texture3D(field.NBX, field.NBY, field.NL, TextureFormat.RGBA32, false)
        { name = n, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };

        byte[] Snap(long k)
        {
            if (cache.TryGetValue(k, out var d)) return d;
            d = order.Count >= 6 ? cache[order.Peek()] : new byte[field.Bytes];
            if (order.Count >= 6) cache.Remove(order.Dequeue());
            field.Compute(k * (double)interval, d);
            cache[k] = d; order.Enqueue(k);
            return d;
        }

        public bool Upload(float t)
        {
            long ka = (long)Mathf.Floor(Mathf.Max(t, 0f) / interval), kb = ka + 1;
            if (ka != upA) { A.SetPixelData(Snap(ka), 0); A.Apply(false); upA = ka; }
            if (kb != upB) { Bt.SetPixelData(Snap(kb), 0); Bt.Apply(false); upB = kb; }
            Blend = Mathf.Clamp01((t - ka * interval) / interval);
            return true;
        }

        public Vector3 Sample(float t, Vector2 gridPos, float z) => field.Sample(Snap((long)Mathf.Round(t / interval)), gridPos, z);

        public void Dispose()
        {
            if (A) Object.DestroyImmediate(A);
            if (Bt) Object.DestroyImmediate(Bt);
        }
    }
}
