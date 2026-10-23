using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using StormLab.Supercell;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit visual review of the designed feeder cloud; does not run automatically.
public static class DesignedInflowReview
{
    static bool running;

    [MenuItem("Supercell Lab/Capture Designed Inflow Review")]
    public static async void Capture()
    {
        if (running) return;
        var bench = UnityEngine.Object.FindAnyObjectByType<StormTestBench>();
        if (!EditorApplication.isPlaying || !bench || !bench.cam)
        { Debug.LogWarning("Open Designed Variant Preview in Play mode first."); return; }
        string saved = JsonUtility.ToJson(bench.s);
        bool panel = bench.showPanel;
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots", "DesignedInflow", DateTime.Now.ToString("yyyyMMdd-HHmmss")));
        Directory.CreateDirectory(folder);
        var errors = new StringBuilder();
        void Log(string message, string stack, LogType type)
        { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.AppendLine(message); }
        running = true;
        Application.logMessageReceived += Log;
        try
        {
            bench.StructureComparison(); bench.showPanel = false;
            foreach (var item in new[] {
                ("plains", DesignedPrecipitation.Classic), ("lp", DesignedPrecipitation.Auto),
                ("plains", DesignedPrecipitation.HP), ("dixie", DesignedPrecipitation.Auto) })
            {
                bench.s.preset = item.Item1; bench.s.precipitation = item.Item2;
                bench.s.region = StormTestBench.PresetRegion(item.Item1); bench.s.seed = 7;
                bench.s.time = 2700; bench.s.distance = 24000; bench.s.bearing = 140;
                bench.s.elevation = 8; bench.s.orbitTargetHeight = 1800;
                bench.s.showRain = true; bench.Respawn();
                for (int i = 0; i < 18; i++) await Rendered(bench.cam);
                string name = item.Item1 + "-" + item.Item2;
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + "-rain-on.png"));
                await Task.Delay(250);
                bench.s.showRain = false;
                for (int i = 0; i < 18; i++) await Rendered(bench.cam);
                ScreenCapture.CaptureScreenshot(Path.Combine(folder, name + "-cloud-only.png"));
                await Task.Delay(250);
            }
            File.WriteAllText(Path.Combine(folder, "review.txt"), "Four designed variants at 45 minutes, seed 7, identical camera/light; rain-on and cloud-only pairs. Appearance requires visual review.\n" + errors);
            Debug.Log("[DesignedInflow] Captured " + folder);
        }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(folder, "failure.txt"), e.ToString()); }
        finally
        {
            Application.logMessageReceived -= Log;
            if (bench) { JsonUtility.FromJsonOverwrite(saved, bench.s); bench.showPanel = panel; bench.Respawn(); }
            running = false;
        }
    }

    static async Task Rendered(Camera observer)
    {
        var done = new TaskCompletionSource<bool>();
        void Finish(ScriptableRenderContext context, Camera camera) { if (camera == observer) done.TrySetResult(true); }
        RenderPipelineManager.endCameraRendering += Finish;
        try
        {
            if (await Task.WhenAny(done.Task, Task.Delay(15000)) != done.Task)
                throw new TimeoutException("The review camera stopped rendering.");
        }
        finally { RenderPipelineManager.endCameraRendering -= Finish; }
    }
}
