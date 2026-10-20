// What the skin's advection and flow-mapped detail read: a ground- or grid-relative wind on 2 km blocks x 1 km layers,
// two snapshots blended on the GPU (StormSkinCommon.hlsl windAt). The brain's WindHistory and the designed storm's
// KinematicWind both provide it.
using System;
using UnityEngine;

namespace StormLab.Supercell.Rendering
{
    public interface IWindSource : IDisposable
    {
        Texture3D A { get; }
        Texture3D Bt { get; }
        /// <summary>blend weight from A to B for the last Upload</summary>
        float Blend { get; }
        /// <summary>domain size, m (the textures cover grid x 0..Lx, y 0..Ly)</summary>
        float Lx { get; }
        float Ly { get; }
        /// <summary>1 km layers, layer L centred at L km</summary>
        int NL { get; }
        /// <summary>snapshots available (0 = calm)</summary>
        int Count { get; }
        /// <summary>Upload the snapshots around time t (s). Returns false (calm wind) when none are available.</summary>
        bool Upload(float t);
        /// <summary>Decoded wind (m/s) at grid position (m) and height (m) near time t (tests, tools).</summary>
        Vector3 Sample(float t, Vector2 gridPos, float z);
    }
}
