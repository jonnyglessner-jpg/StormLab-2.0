using System;
using System.Linq;
using System.Reflection;
using System.IO;
using System.Text;
using StormLab.Supercell;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

// Batch entry point: checks the production skin clock, designed updraft, and GPU transport in physical units.
public static class CloudMotionChecks
{
    static void Require(bool ok, string message) { if (!ok) throw new Exception(message); }
    static readonly (string name, Action run)[] Checks =
    {
        ("Model clock: pause, timelapse and reset", Clock),
        ("Updraft: cloud-base continuity and speeds", Updraft),
        ("Recenter: all target slices use the new origin", Recenter),
        ("Envelope: vacated cloud erodes and new cloud condenses together", EnvelopeResponse),
        ("GPU transport: eastward and rising", () => Transport(new Vector3(20.2f, -9.8f, 10.2f))),
        ("GPU transport: westward and descending", () => Transport(new Vector3(-19.8f, 10.2f, -9.8f)))
    };
    static bool running;
    static int nextCheck;
    static double nextTick;
    static readonly StringBuilder result = new StringBuilder();
    static string ResultPath => Path.GetFullPath(Path.Combine(Application.dataPath, "../Library/CloudMotionChecks.last-run.txt"));

    static void SaveResult() => File.WriteAllText(ResultPath, result.ToString());

    [MenuItem("Supercell Lab/Run Cloud Motion Checks")]
    public static void RunInEditor()
    {
        if (running) return;
        running = true; nextCheck = 0;
        result.Clear(); result.AppendLine("Cloud motion checks started " + DateTimeOffset.Now.ToString("O"));
        try
        {
            SaveResult();
            Debug.Log("CLOUD MOTION START: six checks. A result dialog will appear when finished.");
            EditorUtility.DisplayProgressBar("Cloud Motion Checks", Checks[0].name, 0);
            // Yield before each check so the editor has a chance to show its progress window.
            nextTick = EditorApplication.timeSinceStartup + 0.15;
            EditorApplication.update += RunNextCheck;
        }
        catch (Exception e) { FinishInEditor(e); }
    }

    [MenuItem("Supercell Lab/Run Cloud Motion Checks", true)]
    public static bool CanRunInEditor() => !running && !EditorApplication.isCompiling && !EditorApplication.isPlayingOrWillChangePlaymode;

    static void RunNextCheck()
    {
        if (EditorApplication.timeSinceStartup < nextTick) return;
        try
        {
            var check = Checks[nextCheck];
            result.AppendLine("RUN: " + check.name); SaveResult();
            check.run();
            result.AppendLine("PASS: " + check.name); SaveResult(); Debug.Log("Cloud motion PASS: " + check.name);
            nextCheck++;
            if (nextCheck == Checks.Length) { FinishInEditor(null); return; }
            EditorUtility.DisplayProgressBar("Cloud Motion Checks", Checks[nextCheck].name, nextCheck / (float)Checks.Length);
            nextTick = EditorApplication.timeSinceStartup + 0.15;
        }
        catch (Exception e) { FinishInEditor(e); }
    }

    static void FinishInEditor(Exception error)
    {
        EditorApplication.update -= RunNextCheck; running = false;
        EditorUtility.ClearProgressBar();
        string message = error == null ? "CLOUD MOTION PASS: all six checks passed." : "CLOUD MOTION FAILED: " + error.Message;
        result.AppendLine(message);
        if (error != null) result.AppendLine(error.ToString());
        try { SaveResult(); } catch (Exception writeError) { Debug.LogException(writeError); }
        if (error == null) Debug.Log(message); else Debug.LogException(error);
        EditorUtility.DisplayDialog("Cloud Motion Checks", message + "\n\n" + nextCheck + "/" + Checks.Length + " checks passed.\n\nThese checks validate motion numerically; they do not start the storm scene.\n\nResult saved to:\n" + ResultPath, "OK");
    }

    static void RunChecks()
    {
        foreach (var check in Checks) { check.run(); Debug.Log("Cloud motion PASS: " + check.name); }
        Debug.Log("CLOUD MOTION PASS: model clock, pause, timelapse, reset, continuous updraft, signed GPU transport.");
    }

    public static void RunFromCommandLine()
    {
        try
        {
            RunChecks();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    static void Clock()
    {
        var env = StormLab.Supercell.Environment.Make(StormPreset.All.First(p => p.key == "plains").ToEnv());
        var storm = new DesignedStorm(new FixedEnvironment(env), new Vec2(0, 0), 42, ClimoRegion.Plains);
        var go = new GameObject("Cloud motion check");
        try
        {
            var skin = go.AddComponent<StormSkin>(); skin.tier = SkinTier.Low; skin.nearField = false; skin.UseStandaloneLook();
            skin.BindDesigned(storm, new KinematicWind(storm));
            Require(skin.StepTo(1800, 2), "initial frame failed");
            Require(skin.GpuBlock[Slot.Base].z == 1800 && skin.GpuParams()[(int)Slot.Base].z == 1800,
                "shader motion must use model seconds even when wall clock differs");
            float phase = skin.GpuBlock[Slot.Tilt].w * 1800;
            Require(skin.StepTo(1800, 12), "paused frame failed");
            Require(Mathf.Abs(skin.GpuBlock[Slot.Tilt].w * 1800 - phase) < 0.0001f, "rotation moved while paused");
            Require(skin.StepTo(1810, 13), "timelapse frame failed");
            float rate = skin.Params[Slot.Tilt].w;
            Require(Mathf.Abs(skin.GpuBlock[Slot.Tilt].w * 1810 - phase - rate * 10) < 0.0001f,
                "rotation must integrate ten model seconds, including steps above five seconds");
            skin.ResetVolume(); skin.StepTo(1800, 99);
            Require(Mathf.Abs(skin.GpuBlock[Slot.Tilt].w - skin.Params[Slot.Tilt].w) < 0.000001f,
                "reset must discard previous integrated phase");
        }
        finally { Object.DestroyImmediate(go); }
    }

    static double W(double z)
    {
        var method = typeof(KinematicWindField).GetMethod("Wind", BindingFlags.NonPublic | BindingFlags.Static);
        object[] args = { 0d, 0d, z, 0d, 0d, 0d, 0d, 1d, 10d, 2d, 40d, 0d, 0d,
            Vector4.zero, Vector4.zero, Vector4.zero, Vector4.zero, 0d, 0d, 0d };
        method.Invoke(null, args); return (double)args[19];
    }

    static void Updraft()
    {
        Require(Math.Abs(W(1 - 1e-6) - W(1 + 1e-6)) < 0.001, "updraft discontinuity at cloud base");
        Require(Math.Abs(W(1) - 12) < 0.001, "cloud base must retain subcloud upward velocity");
        Require(Math.Abs(W(10)) < 0.001, "updraft must vanish at cloud top");
        double peak = Enumerable.Range(0, 1001).Max(i => W(1 + 9d * i / 1000));
        Require(Math.Abs(peak - 40) < 0.001, "updraft peak changed");
    }

    static void Recenter()
    {
        var env = StormLab.Supercell.Environment.Make(StormPreset.All.First(p => p.key == "plains").ToEnv());
        var storm = new DesignedStorm(new FixedEnvironment(env), new Vec2(0, 0), 42, ClimoRegion.Plains);
        var go = new GameObject("Recenter regression");
        try
        {
            var skin = go.AddComponent<StormSkin>(); skin.tier = SkinTier.Low; skin.nearField = false; skin.UseStandaloneLook();
            skin.BindDesigned(storm, new KinematicWind(storm)); skin.StepTo(1800, 0);
            var volume = skin.Volume;
            var block = new SkinParams(); skin.GpuBlock.CopyTo(block);
            var up = block[Slot.Up]; up.x += 4.2f; block[Slot.Up] = up;
            var noise = CloudNoise.Make();
            try
            {
                using (var cmd = new CommandBuffer())
                {
                    volume.Frame(cmd, block, noise, null, false, skin.Centre + Vector2.right * 4200,
                        Vector2.right * 4.2f, 0, 30, skin.KeyDirVol, false, 8, 8);
                    Graphics.ExecuteCommandBuffer(cmd);
                    int[] slices = { 4, volume.NZ / 2, volume.NZ - 8 };
                    Color32[] Read(int z)
                    {
                        var r = AsyncGPUReadback.Request(volume.Target, 0, 0, volume.NV, 0, volume.NV, z, 1);
                        r.WaitForCompletion(); Require(!r.hasError, "target slice readback failed");
                        return r.GetData<Color32>().ToArray();
                    }
                    var actual = slices.Select(Read).ToArray();
                    cmd.Clear(); volume.Build(cmd, block, noise, 0, volume.NZ); Graphics.ExecuteCommandBuffer(cmd);
                    for (int i = 0; i < slices.Length; i++)
                    {
                        var expected = Read(slices[i]);
                        Require(actual[i].SequenceEqual(expected), $"recenter left stale target data in slice {slices[i]}");
                    }
                }
            }
            finally { Object.DestroyImmediate(noise); }
        }
        finally { Object.DestroyImmediate(go); }
    }

    static void EnvelopeResponse()
    {
        const int n = 4;
        var old = new Texture3D(n, n, n, TextureFormat.RGBAFloat, false);
        var target = new Texture3D(n, n, n, TextureFormat.RGBAFloat, false);
        var wind = new Texture3D(1, 1, 1, TextureFormat.RGBAFloat, false);
        var output = new RenderTexture(n, n, 0, RenderTextureFormat.ARGBFloat)
            { dimension = TextureDimension.Tex3D, volumeDepth = n, enableRandomWrite = true };
        try
        {
            var a = new Color[n*n*n]; var b = new Color[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                a[i] = new Color(i % n < 2 ? 1 : 0, 0, 0, 1);
                b[i] = new Color(i % n < 2 ? 0 : 1, 0, 0, 1);
            }
            old.SetPixels(a); old.Apply(); target.SetPixels(b); target.Apply(); output.Create();
            var cs = Resources.Load<ComputeShader>("StormSkinVolume"); int kernel = cs.FindKernel("Advect");
            var sp = new Vector4[SkinParams.NSLOT]; sp[(int)Slot.Grid] = new Vector4(n, n, 1, 2);
            cs.SetVectorArray("_SP", sp); cs.SetVector("_VolC", Vector4.zero);
            cs.SetVector("_AdvP", new Vector4(8, 1f - Mathf.Exp(-8f / 60f), 0, 0));
            cs.SetTexture(kernel, "_DensIn", old); cs.SetTexture(kernel, "_Target", target);
            cs.SetTexture(kernel, "_WindA", wind); cs.SetTexture(kernel, "_WindB", wind); cs.SetTexture(kernel, "_OutD", output);
            cs.Dispatch(kernel, 1, 1, 1);
            var request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBAFloat); request.WaitForCompletion();
            Require(!request.hasError, "Envelope readback failed");
            var pixels = request.GetData<Color>(0);
            Require(pixels[0].r < .4f && pixels[3].r > .6f,
                $"Old/new envelope out of sync after 8 model seconds: {pixels[0].r}, {pixels[3].r}");
            Debug.Log($"Envelope response: old {pixels[0].r:F3}, new {pixels[3].r:F3} after 8 model seconds");
        }
        finally
        {
            output.Release(); Object.DestroyImmediate(output); Object.DestroyImmediate(old);
            Object.DestroyImmediate(target); Object.DestroyImmediate(wind);
        }
    }

    static void Transport(Vector3 velocity)
    {
        const int n = 32; const float dt = 10;
        var input = new Texture3D(n, n, n, TextureFormat.RGBAFloat, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var wind = new Texture3D(1, 1, 1, TextureFormat.RGBAFloat, false);
        var output = new RenderTexture(n, n, 0, RenderTextureFormat.ARGBFloat)
            { dimension = TextureDimension.Tex3D, volumeDepth = n, enableRandomWrite = true };
        try
        {
            var data = new Color[n * n * n];
            for (int z = 0; z < n; z++) for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                var p = new Vector3(-1 + (x + 0.5f) * 2 / n, -1 + (y + 0.5f) * 2 / n, (z + 0.5f) * 2 / n - 1);
                float density = Mathf.Exp(-p.sqrMagnitude / 0.02f);
                data[(z * n + y) * n + x] = new Color(density, 0, 0, 1);
            }
            input.SetPixels(data); input.Apply();
            wind.SetPixel(0, 0, 0, new Color((velocity.x + 51) / 102, (velocity.y + 51) / 102, (velocity.z + 51) / 102, 1)); wind.Apply();
            output.Create();
            var cs = Resources.Load<ComputeShader>("StormSkinVolume"); int kernel = cs.FindKernel("Advect");
            var sp = new Vector4[SkinParams.NSLOT]; sp[(int)Slot.Grid] = new Vector4(n, n, 1, 2);
            cs.SetVectorArray("_SP", sp); cs.SetVector("_WindP", new Vector4(0, 2000, 2000, 2));
            cs.SetVector("_VolC", new Vector4(1000, 1000, 7, 1)); cs.SetVector("_AdvP", new Vector4(dt, 0, 0, 0));
            cs.SetTexture(kernel, "_DensIn", input); cs.SetTexture(kernel, "_Target", input);
            cs.SetTexture(kernel, "_WindA", wind); cs.SetTexture(kernel, "_WindB", wind); cs.SetTexture(kernel, "_OutD", output);
            cs.Dispatch(kernel, n / 4, n / 4, n / 4);
            var request = AsyncGPUReadback.Request(output, 0, TextureFormat.RGBAFloat); request.WaitForCompletion();
            Require(!request.hasError, "GPU readback failed");
            // GetData defaults to layer zero, not the complete Texture3D. Each slice normally has n*n pixels.
            // Also accept backends that expose the full depth in a single contiguous layer.
            var firstSlice = request.GetData<Color>(0);
            bool packed = request.layerCount == 1 && firstSlice.Length == n * n * n;
            Require(packed || (request.layerCount == n && firstSlice.Length == n * n),
                $"Unexpected GPU readback layout: {request.layerCount} layers, {firstSlice.Length} pixels per layer");
            Vector3 sum = Vector3.zero; double mass = 0;
            for (int z = 0; z < n; z++)
            {
                var pixels = packed ? firstSlice : request.GetData<Color>(z);
                int sliceOffset = packed ? z * n * n : 0;
                Require(packed || pixels.Length == n * n, $"Incomplete GPU readback slice {z}: {pixels.Length} pixels");
                for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
                {
                    float d = pixels[sliceOffset + y * n + x].r;
                    sum += d * new Vector3(-1 + (x + 0.5f) * 2 / n, -1 + (y + 0.5f) * 2 / n, (z + 0.5f) * 2 / n - 1); mass += d;
                }
            }
            Require(mass > 0 && !double.IsNaN(mass) && !double.IsInfinity(mass), "GPU transport returned invalid cloud mass");
            var displacement = sum / (float)mass;
            Require((displacement - velocity * dt / 1000).magnitude < 0.001f,
                $"GPU displacement {displacement} km does not match {velocity} m/s for {dt} s");
            Debug.Log($"GPU cloud displacement PASS: {velocity} m/s -> {displacement} km in {dt} s");
        }
        finally { output.Release(); Object.DestroyImmediate(output); Object.DestroyImmediate(input); Object.DestroyImmediate(wind); }
    }
}
