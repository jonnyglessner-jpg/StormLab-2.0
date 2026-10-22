using System;
using System.IO;
using System.Text;
using System.Globalization;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using StormLab.Supercell.Designer;

// Measures feature motion over atmospheric seconds, rather than only at frame boundaries.
public static class FeatureMotionReview
{
    const string Folder = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\outputs\feature-motion";
    const string Frames = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\work\motion-review";
    [MenuItem("Supercell Lab/Run Feature Motion Review")]
    public static async void Run()
    {
        var p = UnityEngine.Object.FindAnyObjectByType<BrainFullCyclePreview>();
        if (!EditorApplication.isPlaying || !p || p.runner.Horizon < 10790)
        { Debug.LogError("Feature review needs the full recorded preview in Play mode."); return; }
        Directory.CreateDirectory(Folder); Directory.CreateDirectory(Frames);
        var report = new StringBuilder();
        float oldHour = p.startSolarHour, oldSkyRate = p.solarHoursPerModelHour;
        try
        {
            p.skin.playing = false;
            var designer = p.skin.Designer; var tl = p.runner.Timeline;
            var a = new SkinParams(); var b = new SkinParams();
            var csv = new StringBuilder("seconds,raw_low_x_m,raw_low_y_m,display_low_x_m,display_low_y_m,updraft_speed_ms,wall_speed_ms,mid_meso_speed_ms,old_blended_speed_ms\n");
            Vector2 XY(Vector4 v) => new Vector2(v.x, v.y);
            Vector2 OldCore(float t)
            {
                Vector2 sum = Vector2.zero; float sw = 0, sigma = designer.smoothing;
                for (int i = Mathf.Max(0, Mathf.FloorToInt((t-2*sigma)/tl.interval)); i <= Mathf.Min(tl.Latest, Mathf.CeilToInt((t+2*sigma)/tl.interval)); i++)
                {
                    var f = tl.Get(i); if (f == null || !f.valid) continue;
                    var m = designer.Measure(f); if (!m.storm) continue;
                    float d = (f.t-t)/sigma;
                    float w = Mathf.Exp(-.5f*d*d)*Mathf.SmoothStep(0,1,Mathf.Clamp01((2-Mathf.Abs(d))/.5f));
                    sum += m.coreLow*w; sw += w;
                }
                return sw > 0 ? sum/sw : Vector2.zero;
            }
            float upMax = 0, wallMax = 0, midMax = 0, beforeMax = 0, clipBefore = 0, clipAfter = 0;
            int count = 0;
            designer.Evaluate(599,599,Vector2.zero,a); var old = OldCore(599);
            for (float t = 600; t < 10800; t++)
            {
                designer.Evaluate(t,t,Vector2.zero,b); var raw = tl.AtTime(t);
                var legacy = OldCore(t);
                float u = Vector2.Distance(XY(a[Slot.Up]),XY(b[Slot.Up]))*1000;
                float w = Vector2.Distance(XY(a[Slot.Wall]),XY(b[Slot.Wall]))*1000;
                float m = Vector2.Distance(XY(a[Slot.Meso4]),XY(b[Slot.Meso4]))*1000;
                float previous = Vector2.Distance(old,legacy);
                if (a[Slot.Tower].z > .2f && b[Slot.Tower].z > .2f)
                {
                    upMax = Mathf.Max(upMax,u); midMax = Mathf.Max(midMax,m); beforeMax = Mathf.Max(beforeMax,previous); count++;
                    if (a[Slot.Wall].w > .05f && b[Slot.Wall].w > .05f) wallMax = Mathf.Max(wallMax,w);
                    if (t >= 8200 && t <= 8700) { clipBefore = Mathf.Max(clipBefore,previous); clipAfter = Mathf.Max(clipAfter,u); }
                }
                var low = designer.Measure(raw).coreLow;
                csv.AppendLine(FormattableString.Invariant($"{t},{low.x},{low.y},{b[Slot.Up].x*1000},{b[Slot.Up].y*1000},{u},{w},{m},{previous}"));
                var swap = a; a = b; b = swap; old = legacy;
            }
            File.WriteAllText(Path.Combine(Folder,"feature-paths.csv"),csv.ToString());
            if (count < 5000 || upMax > 20.1f || wallMax > 20.1f || midMax > 30.1f)
                throw new Exception($"Excess feature speed: up {upMax}, wall {wallMax}, mid {midMax}; {count} samples");
            report.AppendLine($"PASS: {count} active one-model-second samples. Peak grid-relative presentation speeds: updraft {upMax:F3}, wall {wallMax:F3}, mid meso {midMax:F3} m/s.");
            report.AppendLine($"Previous 25-second Gaussian core path: peak {beforeMax:F3} m/s. Video interval (8200-8700 s): previous {clipBefore:F3}, corrected {clipAfter:F3} m/s.");
            var fresh = new FeatureDesigner(tl,p.runner.Run.env);
            foreach (float t in new[] { 8700f, 8100f, 8220f, 4500f, 10700f, 8100f })
            {
                designer.Evaluate(t,t,Vector2.zero,a); fresh.Evaluate(t,t,Vector2.zero,b);
                foreach (var slot in new[] {Slot.Up,Slot.Meso1,Slot.Meso4,Slot.Wall})
                    if ((a[slot]-b[slot]).magnitude > 1e-5f) throw new Exception("Motion depends on seek/evaluation order: " + slot);
            }
            report.AppendLine("PASS: a fresh designer evaluated out of order reproduces the same feature tracks.");
            File.WriteAllText(Path.Combine(Folder,"checks.txt"),report.ToString()+"Capturing 30x video review...\n");
            p.followStorm = true; p.startSolarHour = 14; p.solarHoursPerModelHour = 0;
            p.skin.playbackTime = 8200; p.skin.playing = false;
            await Task.Delay(250); await Rendered(p.observer);
            p.skin.timelapse = 30; p.skin.playing = true;
            var times = new StringBuilder("frame,wall_seconds,model_seconds\n");
            double start = EditorApplication.timeSinceStartup;
            for (int i = 0; i < 180; i++)
            {
                await Rendered(p.observer);
                ScreenCapture.CaptureScreenshot(Path.Combine(Frames,$"frame-{i:D4}.png"));
                times.AppendLine(FormattableString.Invariant($"{i},{EditorApplication.timeSinceStartup-start},{p.skin.ShownTime}"));
                await Task.Delay(100);
            }
            File.WriteAllText(Path.Combine(Folder,"review-timing.csv"),times.ToString());
            report.AppendLine("CAPTURED: 180 rendered frames at 30x playback, with constant daylight for visual review. See review-timing.csv for actual timing.");
            Debug.Log("[FeatureMotion] PASS: speed, seek determinism, 30x rendering review captured");
        }
        catch (Exception e) { report.AppendLine("FAIL: "+e); Debug.LogException(e); }
        finally
        {
            p.skin.playing = false; p.skin.playbackTime = 8400;
            p.startSolarHour = oldHour; p.solarHoursPerModelHour = oldSkyRate;
            File.WriteAllText(Path.Combine(Folder,"checks.txt"),report.ToString());
        }
    }
    static Task Rendered(Camera observer)
    {
        var done = new TaskCompletionSource<bool>();
        void Finished(ScriptableRenderContext context, Camera camera)
        {
            if (camera != observer) return;
            RenderPipelineManager.endCameraRendering -= Finished; done.SetResult(true);
        }
        RenderPipelineManager.endCameraRendering += Finished; return done.Task;
    }
}
