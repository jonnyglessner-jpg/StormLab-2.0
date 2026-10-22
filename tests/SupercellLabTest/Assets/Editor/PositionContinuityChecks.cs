using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using StormLab.Supercell.Designer;

public static class PositionContinuityChecks
{
    [MenuItem("Supercell Lab/Run Camera Playback Checks")]
    public static async void Run()
    {
        const string folder = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\outputs\position-fix";
        Directory.CreateDirectory(folder);
        var p = UnityEngine.Object.FindAnyObjectByType<BrainFullCyclePreview>();
        if (!EditorApplication.isPlaying || !p || p.runner.Horizon < 10740)
        { Debug.LogError("Camera checks need the full recorded preview in Play mode."); return; }
        var result = new StringBuilder();
        float worst = 0;
        void Check()
        {
            Vector2 anchor = new Vector2(p.runner.Run.g.Lx * .45f, p.runner.Run.g.Ly * .5f);
            Vector3 expected = BrainFullCyclePreview.CameraGroundAt(anchor, p.skin.groundOrigin,
                p.skin.FrameMotion, p.skin.groundHeight, p.skin.ShownTime) + new Vector3(15000, 2, -15000);
            float error = Vector3.Distance(expected, p.observer.transform.position);
            worst = Mathf.Max(worst, error);
            if (error > .02f) throw new Exception($"camera has {error:F3} m of lag at {p.skin.ShownTime:F2}s");
        }
        try
        {
            p.followStorm = true; p.skin.playing = false;
            var left = new SkinParams(); var right = new SkinParams();
            float worstBoundary = 0, worstLegacyBoundary = 0; int boundaries = 0;
            Vector2 LegacyCore(float time)
            {
                var tl = p.runner.Timeline; float sigma = p.skin.Designer.smoothing;
                int lo = Mathf.FloorToInt(Mathf.Max(0, time - 2 * sigma) / tl.interval);
                int hi = Mathf.CeilToInt(Mathf.Min(tl.Horizon, time + 2 * sigma) / tl.interval);
                Vector2 sum = Vector2.zero; float weights = 0;
                for (int i = lo; i <= hi; i++)
                {
                    var frame = tl.Get(i); if (frame == null || !frame.valid) continue;
                    var feature = p.skin.Designer.Measure(frame); if (!feature.storm) continue;
                    float d = (frame.t - time) / sigma, w = Mathf.Exp(-.5f * d * d);
                    sum += feature.coreLow * w; weights += w;
                }
                return weights > 0 ? sum / weights : Vector2.zero;
            }
            for (float t = 600; t <= 10700; t += 10)
            {
                p.skin.Designer.Evaluate(t - .001f, t - .001f, Vector2.zero, left);
                p.skin.Designer.Evaluate(t + .001f, t + .001f, Vector2.zero, right);
                if (left[Slot.Tower].z < .2f || right[Slot.Tower].z < .2f) continue;
                Vector4 a = left[Slot.Up], b = right[Slot.Up];
                float step = Vector2.Distance(new Vector2(a.x, a.y), new Vector2(b.x, b.y)) * 1000;
                worstLegacyBoundary = Mathf.Max(worstLegacyBoundary, Vector2.Distance(LegacyCore(t - .001f), LegacyCore(t + .001f)));
                worstBoundary = Mathf.Max(worstBoundary, step); boundaries++;
            }
            if (boundaries < 500 || worstBoundary > 2f)
                throw new Exception($"designer core steps {worstBoundary:F3} m across a 2 ms boundary ({boundaries} tested)");
            result.AppendLine($"PASS: {boundaries} active ten-second window boundaries; largest core displacement across 2 ms: {worstBoundary:F6} m.");
            result.AppendLine($"Comparison: original hard-cutoff weighting on the same frames steps by up to {worstLegacyBoundary:F6} m across 2 ms.");
            foreach (float t in new[] { 2700f, 8100f, 2700f, 10740f, 3600f })
            {
                p.skin.playbackTime = t;
                await Task.Delay(100); await RenderedFrame(p.observer); Check();
                Vector3 held = p.observer.transform.position;
                await Task.Delay(150); await RenderedFrame(p.observer); Check();
                if (p.observer.transform.position != held) throw new Exception("camera drifts while playback is paused");
                result.AppendLine($"PASS: seek to {t:F0}s settles in one displayed update; paused camera stays fixed.");
            }
            foreach (float speed in new[] { 1f, 60f })
            {
                p.skin.timelapse = speed; p.skin.playing = true;
                for (int i = 0; i < 30; i++) { await Task.Delay(50); await RenderedFrame(p.observer); Check(); }
                p.skin.playing = false;
                result.AppendLine($"PASS: live {speed:F0}x playback tracks model time without core pursuit or wall-clock lag.");
            }
            result.AppendLine($"PASS: worst observed camera position residual {worst:F6} m.");
            Debug.Log("[Position] Camera playback checks PASS");
        }
        catch (Exception e) { result.AppendLine("FAIL: " + e); Debug.LogException(e); }
        finally
        {
            p.skin.playing = false; p.skin.playbackTime = 3600;
            File.WriteAllText(Path.Combine(folder, "camera-playback.txt"), result.ToString());
        }
    }

    static Task RenderedFrame(Camera observer)
    {
        var ready = new TaskCompletionSource<bool>();
        void Rendered(ScriptableRenderContext context, Camera camera)
        {
            if (camera != observer) return;
            RenderPipelineManager.endCameraRendering -= Rendered;
            ready.SetResult(true);
        }
        RenderPipelineManager.endCameraRendering += Rendered;
        return ready.Task;
    }
}
