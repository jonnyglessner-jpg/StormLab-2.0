using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using StormLab.Supercell.Dynamics;

// Explicit local review: never runs on load or changes the simulation. Restores playback settings.
[InitializeOnLoad]
public static class RainLifecycleReview
{
    static bool running;
    static bool checkedJob, restarting;
    static string RequestPath => Path.Combine(Application.dataPath,"../Library/RainLifecycleReview.request");
    static RainLifecycleReview() { EditorApplication.update += RequestedReview; }

    // Opt-in local test trigger for this repository. Merely importing the script
    // does nothing: an explicit request file or menu command is required.
    static void RequestedReview()
    {
        if (running || EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(RequestPath)) return;
        try
        {
            if(!checkedJob) { StormStructureChecks.Run(); checkedJob=true; }
            var preview=UnityEngine.Object.FindAnyObjectByType<BrainFullCyclePreview>();
            if(!preview) throw new InvalidOperationException("Open BrainFullCycleTest before requesting a review.");
            if(!EditorApplication.isPlaying)
            {
                if(!EditorApplication.isPlayingOrWillChangePlaymode) EditorApplication.EnterPlaymode();
                return;
            }
            // A script domain reload disposes the non-serialized simulation. A fresh
            // Play session is required; do not try to render a stale GPU run.
            if(preview.runner.Run==null)
            {
                if(Time.timeSinceLevelLoad<2) return;
                if(!restarting) { restarting=true; EditorApplication.ExitPlaymode(); }
                return;
            }
            restarting=false;
            if(preview.runner.Horizon<10500) return;
            File.Delete(RequestPath); checkedJob=false;
            Capture();
        }
        catch(Exception e)
        {
            File.WriteAllText(RequestPath+".failure",e.ToString()); File.Delete(RequestPath);
            checkedJob=false; Debug.LogException(e);
        }
    }
    [MenuItem("Supercell Lab/Rainfall/Capture Recorded Lifecycle Review")]
    public static async void Capture()
    {
        var p = UnityEngine.Object.FindAnyObjectByType<BrainFullCyclePreview>();
        if (running || !EditorApplication.isPlaying || !p || p.runner.Horizon < 10500)
        { Debug.LogWarning("Rain lifecycle review needs Play mode and at least 175 recorded model minutes."); return; }
        running = true;
        // Keep captures in this chat's deliverables, never in the reference library.
        string root = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\us\outputs";
        string folder = Path.Combine(root, "rain-lifecycle-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
        Directory.CreateDirectory(folder);
        float oldTime = p.skin.playbackTime, oldHour = p.startSolarHour, oldSky = p.solarHoursPerModelHour;
        bool oldPlaying = p.skin.playing;
        int oldDebug = p.skin.debugFlags;
        var report = new StringBuilder("seconds,height_km,qr_area_km2,qh_area_km2,qr_centroid_grid_x_m,qr_centroid_grid_y_m,qr_peak_kg_kg\n");
        try
        {
            p.skin.playing = false; p.startSolarHour = 14; p.solarHoursPerModelHour = 0;
            foreach (float t in new[] {600f,1800f,3000f,6000f,8100f,10200f})
            {
                p.skin.playbackTime = t;
                var f = p.runner.Timeline.AtTime(t); var tl = p.runner.Timeline;
                if (f == null || f.q3D == null) continue;
                int plane = f.winNX*f.winNY, field = plane*tl.NL;
                foreach (int z in new[] {0,2,5,10})
                {
                    if (z >= tl.NL) continue;
                    double qrArea=0,qhArea=0,mass=0,xsum=0,ysum=0,peak=0;
                    for(int y=0;y<f.winNY;y++) for(int x=0;x<f.winNX;x++)
                    {
                        int i=(z*f.winNY+y)*f.winNX+x;
                        double qr=Q3D.Decode(f.q3D[field+i]), qh=Q3D.Decode(f.q3D[3*field+i]);
                        if(qr>1e-5) qrArea++; if(qh>1e-5) qhArea++;
                        mass+=qr; xsum+=qr*(f.winI0+x+.5)*tl.blockSize; ysum+=qr*(f.winJ0+y+.5)*tl.blockSize;
                        peak=Math.Max(peak,qr);
                    }
                    report.AppendLine(FormattableString.Invariant($"{t},{z},{qrArea*tl.blockSize*tl.blockSize/1e6},{qhArea*tl.blockSize*tl.blockSize/1e6},{(mass>0?xsum/mass:0)},{(mass>0?ysum/mass:0)},{peak}"));
                }
                foreach (bool cloudOnly in new[] {false,true})
                {
                    p.skin.debugFlags = cloudOnly ? oldDebug|16 : oldDebug & ~16;
                    for(int i=0;i<12;i++) await Rendered(p.observer);
                    ScreenCapture.CaptureScreenshot(Path.Combine(folder,$"t{t:00000}-{(cloudOnly?"cloud-only":"rain-on")}.png"));
                    await Task.Delay(200);
                }
            }
            File.WriteAllText(Path.Combine(folder,"recorded-mass.csv"),report.ToString());
            File.WriteAllText(Path.Combine(folder,"review.txt"),"CAPTURED: six life stages, with rain on and cloud-only at identical camera/time/daylight. Inspect PNGs; this is not an automatic realism pass.\n");
            Debug.Log("[RainLifecycle] Review captured: " + folder);
        }
        catch(Exception e) { Debug.LogException(e); File.WriteAllText(Path.Combine(folder,"failure.txt"),e.ToString()); }
        finally
        {
            if(p && p.skin) { p.skin.debugFlags=oldDebug; p.skin.playbackTime=oldTime; p.skin.playing=oldPlaying; p.startSolarHour=oldHour; p.solarHoursPerModelHour=oldSky; }
            running=false;
        }
    }
    static async Task Rendered(Camera observer)
    {
        var done=new TaskCompletionSource<bool>();
        void Finish(ScriptableRenderContext context,Camera cam) { if(cam==observer) done.TrySetResult(true); }
        RenderPipelineManager.endCameraRendering+=Finish;
        try { if(await Task.WhenAny(done.Task,Task.Delay(15000)) != done.Task) throw new TimeoutException("Unity camera stopped rendering during review."); }
        finally { RenderPipelineManager.endCameraRendering-=Finish; }
    }
}
