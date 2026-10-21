using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rendering
{
    /// <summary>Optional local illumination supplied before a camera's cloud march. Keeps the skin independent of effects.
    /// Return true when a transient is visible; the renderer then bypasses checkerboard and temporal blending.</summary>
    public interface IStormTransientLighting
    {
        bool RecordLighting(CommandBuffer command, StormSkin skin, Camera camera, ComputeShader march, int kernel);
    }
}
