using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using StormLab.Supercell;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit local GPU/render review. Importing this file alone never runs a review.
[InitializeOnLoad]
public static class DesignedRainReview
{
    static bool running;
    static string Request => Path.Combine(Application.dataPath, "../Library/DesignedRainReview.request");
    static string Result => Path.Combine(Application.dataPath, "../Library/DesignedRainReview.result.txt");
    static DesignedRainReview() { EditorApplication.update += Requested; }
    static void Requested()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(Request)) return;
        File.Delete(Request); Capture();
    }
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }

    public static void Check()
    {
        foreach (string name in new[] { "StormSkinVolume", "StormSkinMarch", "StormSkinNear", "StormLightningLight" })
        {
            var shader = Resources.Load<ComputeShader>(name);
            Require(shader && !ShaderUtil.GetComputeShaderMessages(shader).Any(m => m.severity == UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error), name + " shader errors");
        }
        var active = StormSkin.Active;
        var noise = CloudNoise.Make();
        var cmd = new CommandBuffer();
        using (var volume = new SkinVolume(SkinTier.Low, Resources.Load<ComputeShader>("StormSkinVolume")))
        try
        {
            foreach (string key in new[] { "plains", "lp", "dixie" })
            {
                var storm = new DesignedStorm(new FixedEnvironment(StormLab.Supercell.Environment.Make(StormPreset.Resolve(key).ToEnv())),
                    new Vec2(0, 0), 7, StormTestBench.PresetRegion(key));
                var p = new SkinParams();
                storm.Evaluate(2700, 2700, storm.UpdraftAt(2700), p);
                // Isolate the FFD from hook/hail to measure its attachment and trajectory.
                p.Set(Slot.Hail, 0, 0, 1, 0); p.Set(Slot.Rfd, 0, 0, 0, 0); p.Set(Slot.RfdCore, 0, 0, 1, 0);
                float zb = p[Slot.Base].x;
                float canopy = Mathf.Lerp(zb + 1.2f, Mathf.Max(zb + 1.2f, p[Slot.Anvil2].y - .55f), Mathf.Clamp01(p[Slot.Anvil].w));
                float source = Mathf.Max(zb + .4f, Mathf.Min(p[Slot.Tower].x - .35f, canopy));
                cmd.Clear(); volume.Build(cmd, p, noise, 0, volume.NZ); Graphics.ExecuteCommandBuffer(cmd);
                var read = AsyncGPUReadback.Request(volume.Target, 0, TextureFormat.RGBAFloat); read.WaitForCompletion();
                Require(!read.hasError, "Designed rain GPU readback failed");
                (float mass, Vector2 centre, float inCloud) Layer(float height)
                {
                    int z = Mathf.Clamp(Mathf.FloorToInt(height / volume.ZT * volume.NZ), 0, volume.NZ - 1);
                    var data = read.GetData<Color>(z); float mass = 0, covered = 0; var centre = Vector2.zero;
                    for (int y = 0; y < volume.NV; y++) for (int x = 0; x < volume.NV; x++)
                    {
                        var c = data[y * volume.NV + x]; float r = c.g * c.g;
                        if (r < .02f) continue;
                        var pos = new Vector2(((x + .5f) / volume.NV * 2 - 1) * volume.L, ((y + .5f) / volume.NV * 2 - 1) * volume.L);
                        mass += r; centre += pos * r;
                        if (c.r * c.r > .02f) covered += r;
                    }
                    return (mass, centre / Mathf.Max(mass, .0001f), covered / Mathf.Max(mass, .0001f));
                }
                // Thin LP curtains can evaporate before reaching the ground (virga).
                var ground = Layer(Mathf.Max(.5f, zb * .8f)); var middle = Layer(zb + 2); var upper = Layer(source - .65f); var sky = Layer(source + .6f);
                Require(ground.mass > 1 && middle.mass > 1 && upper.mass > 1, $"{key}: curtain detached between below-base, LCL and source ({ground.mass}/{middle.mass}/{upper.mass})");
                Require(sky.mass < .01f, key + ": precipitation extends above the authored source");
                Vector2 root = (Vector2)p[Slot.Up] + (Vector2)p[Slot.Tilt] * (source - zb)
                    + (Vector2)p[Slot.Anvil] * Mathf.Min(p[Slot.Anvil].z * .12f, p[Slot.Tower].y * 1.5f) * Mathf.Clamp01(p[Slot.Anvil].w);
                Require((upper.centre - root).magnitude < .8f * (ground.centre - (Vector2)p[Slot.Up]).magnitude,
                    key + ": upper rain does not bend toward the cloud root");
                Require(upper.inCloud > .4f, key + ": upper rain fails to overlap the cloud/anvil");
                Debug.Log($"[DesignedRain] {key}: ground/middle/source mass {ground.mass:0}/{middle.mass:0}/{upper.mass:0}, source in cloud {upper.inCloud:P0}, above source {sky.mass:0.000}");
            }
        }
        finally { cmd.Release(); UnityEngine.Object.DestroyImmediate(noise); if (active) active.Activate(); }
    }

    [MenuItem("Supercell Lab/Capture Designed Rain Review")]
    public static async void Capture()
    {
        if (running) return;
        running = true;
        var bench = UnityEngine.Object.FindAnyObjectByType<StormTestBench>();
        string saved = bench ? JsonUtility.ToJson(bench.s) : null;
        bool panel = bench && bench.showPanel;
        try
        {
            Check();
            Require(EditorApplication.isPlaying && bench && bench.cam, "Open the designed preview in Play to capture rain.");
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Screenshots/DesignedRain"));
            Directory.CreateDirectory(folder);
            bench.StructureComparison(); bench.showPanel = false;
            foreach (string key in new[] { "plains", "lp", "dixie" })
            {
                bench.s.preset = key; bench.s.precipitation = DesignedPrecipitation.Auto;
                bench.s.region = StormTestBench.PresetRegion(key); bench.s.seed = 7;
                bench.s.distance = 36000; bench.s.bearing = 140; bench.s.elevation = 6; bench.s.orbitTargetHeight = 4500;
                bench.s.showRain = true; bench.Respawn();
                for (int i = 0; i < 24; i++) await Rendered(bench.cam);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, key + "-rain.png"));
                await Rendered(bench.cam); await Rendered(bench.cam);
            }
            File.WriteAllText(Result, "PASS: designed rain GPU support, curved source attachment, cloud overlap, source cutoff, shader compilation. Captures: " + folder);
            Debug.Log("[DesignedRain] " + File.ReadAllText(Result));
        }
        catch (Exception e) { File.WriteAllText(Result, "FAIL: " + e); Debug.LogException(e); }
        finally
        {
            if (bench && saved != null) { JsonUtility.FromJsonOverwrite(saved, bench.s); bench.showPanel = panel; bench.Respawn(); }
            running = false;
        }
    }
    static async Task Rendered(Camera observer)
    {
        var done = new TaskCompletionSource<bool>();
        void Finish(ScriptableRenderContext context, Camera camera) { if (camera == observer) done.TrySetResult(true); }
        RenderPipelineManager.endCameraRendering += Finish;
        try { Require(await Task.WhenAny(done.Task, Task.Delay(15000)) == done.Task, "Review camera stopped rendering"); }
        finally { RenderPipelineManager.endCameraRendering -= Finish; }
    }
}
