using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using StormLab.Supercell;
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Lightning;
using StormLab.Supercell.Rendering;
using UnityEngine;

// Test-scene presentation only. Does not alter the sounding, model physics, or storm lifetime.
[DefaultExecutionOrder(90)]
public sealed class BrainFullCyclePreview : MonoBehaviour
{
    public SupercellRunner runner;
    public StormSkin skin;
    public LightningSystem lightning;
    public LightningStormDriver driver;
    public Camera observer;
    public float startSolarHour = 6, solarHoursPerModelHour = 8;
    public bool followStorm = true;
    public string outputDirectory;
    readonly StringBuilder csv = new StringBuilder("model_seconds,solar_hour,updraft_ms,cloud_top_m,cold_pool_K,max_dbz,flashes_per_minute,rendered_flash_count\n");
    int flashes, captureIndex;
    float nextSample, lastReport;
    float peakW, peakCloud, peakRate;
    bool complete, nightFlashCaptured;
    readonly float[] captureTimes = { 60, 2700, 6000, 8100, 10800 };
    readonly string[] captureNames = { "01_morning", "02_noon", "03_sunset", "04_midnight", "05_next_morning" };

    void Awake()
    {
        Directory.CreateDirectory(outputDirectory);
        lightning.advanceClock = false;
        lightning.CellCentreAtTime = CentreAt;
        lightning.FlashStarted += OnFlash;
        Application.runInBackground = true;
        Application.targetFrameRate = 60;
        QualitySettings.vSyncCount = 0;
        File.WriteAllText(Path.Combine(outputDirectory, "preview-errors.txt"), "");
        Application.logMessageReceived += OnLog;
        // This scene owns the observer. The diagnostics panel must not also move it.
        var panel = runner.GetComponent<BrainPreviewPanel>();
        if (panel) panel.allowCameraControl = false;
    }

    Vector3 CentreAt(double seconds)
    {
        var f = runner.Timeline?.AtTime((float)seconds);
        if (f == null) return new Vector3(0, skin.groundHeight, 0);
        Vector2 grid = f.updraftPos;
        Vector2 world = grid + skin.groundOrigin + runner.Timeline.frameMotion * (float)seconds;
        return new Vector3(world.x, skin.groundHeight, world.y);
    }

    void OnFlash(LightningFlash flash)
    {
        flashes++;
        if (!nightFlashCaptured && skin.Lighting.keyIsMoon && skin.playbackTime > 1800 && flash.Event.kind != FlashKind.InCloud)
        {
            nightFlashCaptured = true;
            StartCoroutine(Capture("06_night_lightning_live"));
        }
    }

    void OnLog(string message, string trace, LogType type)
    {
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            File.AppendAllText(Path.Combine(outputDirectory, "preview-errors.txt"), message + "\n" + trace + "\n");
    }

    void Update()
    {
        // Match the time StormSkin will display later in Update (order 100), including pause and horizon limits.
        float next = skin.playbackTime;
        if (runner.Run != null && skin.playing)
            next = Mathf.Min(next + Time.deltaTime * skin.timelapse, runner.Horizon);
        skin.sunSource = SunSource.TimeOfDay;
        skin.solarHour = Mathf.Repeat(startSolarHour + next / 3600 * solarHoursPerModelHour, 24);
    }

    void LateUpdate()
    {
        if (runner.Run == null) return;
        var f = runner.Timeline.AtTime(skin.playbackTime);
        if (f == null) return;
        if (followStorm && observer)
        {
            // Ride the simulation's translating frame, independent of whichever core is
            // strongest. Position is a function of shown model time: pause is stationary,
            // and an explicit seek lands immediately instead of flying across the map.
            Vector2 anchor = new Vector2(runner.Run.g.Lx * .45f, runner.Run.g.Ly * .5f);
            Vector3 p = CameraGroundAt(anchor, skin.groundOrigin, skin.FrameMotion, skin.groundHeight, skin.ShownTime);
            observer.transform.position = p + new Vector3(15000, 2, -15000);
            observer.transform.rotation = Quaternion.LookRotation(new Vector3(-15000, 4000, 15000));
        }
        peakW = Mathf.Max(peakW, f.wMax); peakCloud = Mathf.Max(peakCloud, f.cloudTop);
        float rate = driver.Current.HasValue ? driver.Current.Value.flashesPerMinute : 0;
        peakRate = Mathf.Max(peakRate, rate);
        if (skin.playbackTime >= nextSample)
        {
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:F2},{1:F3},{2:F3},{3:F1},{4:F3},{5:F2},{6:F2},{7}",
                skin.playbackTime, skin.solarHour, f.wMax, f.cloudTop, f.coldPoolMin, f.maxDbz, rate, flashes));
            nextSample = skin.playbackTime + 60;
        }
        if (captureIndex < captureTimes.Length && skin.playbackTime >= captureTimes[captureIndex] - .1f)
            StartCoroutine(Capture(captureNames[captureIndex++]));
        bool done = runner.Run.Finished && runner.Horizon >= runner.setup.runLength - .1f
            && skin.playbackTime >= runner.setup.runLength - .1f;
        if (Time.unscaledTime - lastReport > 10 || done && !complete)
        {
            complete |= done; lastReport = Time.unscaledTime;
            File.WriteAllText(Path.Combine(outputDirectory, "brain-cycle.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDirectory, "brain-cycle-status.txt"),
                $"{(complete ? "COMPLETED" : "RUNNING")}\nDisplayed seconds: {skin.playbackTime:F1}\nComputed seconds: {runner.Horizon:F1}\nSolar hour: {skin.solarHour:F3}\n" +
                $"Lightning source: {driver.Bound}\nRendered flash events: {flashes}\nPeak sampled updraft m/s: {peakW:F2}\nPeak sampled cloud top m: {peakCloud:F1}\nPeak sampled flash rate/min: {peakRate:F2}\n" +
                $"Saved checkpoints: {runner.Run.checkpoints.All.Count}\nPending readbacks: {runner.Run.PendingReadbacks}\n" +
                $"End updraft m/s: {f.wMax:F2}\nEnd cloud top m: {f.cloudTop:F1}\nEnd flash rate/min: {rate:F2}\n" +
                "Sky advances 8 hours per model hour for visual review; no diurnal atmospheric forcing is added.\nEnd of run is not proof of storm dissipation.\n");
        }
        if (done) skin.playing = false;
    }

    public static Vector3 CameraGroundAt(Vector2 anchor, Vector2 origin, Vector2 motion, float ground, float shownTime)
    {
        Vector2 p = anchor + origin + motion * shownTime;
        return new Vector3(p.x, ground, p.y);
    }

    IEnumerator Capture(string name)
    {
        yield return new WaitForEndOfFrame();
        ScreenCapture.CaptureScreenshot(Path.Combine(outputDirectory, name + ".png"));
    }

    void OnGUI()
    {
        GUILayout.BeginArea(new Rect(Mathf.Max(425, Screen.width - 390), 12, 375, 365), GUI.skin.box);
        GUILayout.Label("BRAIN + LIGHTNING + DAY / NIGHT");
        GUILayout.Label($"Solar time {skin.solarHour:00.00} | 8 sky hours / model hour");
        GUILayout.Label($"Lightning: {driver.Bound} | shown events: {flashes}");
        GUILayout.Label(driver.Current.HasValue ? $"Activity: {driver.Current.Value.flashesPerMinute:0.0} flashes/min" : "Waiting for brain lightning diagnostics");
        followStorm = GUILayout.Toggle(followStorm, "Steady storm camera");
        GUILayout.Label($"Storm time {skin.playbackTime / 60:0.0} / 180 min | playback {skin.timelapse:0}x");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button(skin.playing ? "Pause" : "Play")) skin.playing = !skin.playing;
        if (GUILayout.Button("1x / thunder")) { skin.timelapse = 1; skin.playing = true; }
        if (GUILayout.Button("30x")) { skin.timelapse = 30; skin.playing = true; }
        if (GUILayout.Button("60x")) { skin.timelapse = 60; skin.playing = true; }
        GUILayout.EndHorizontal();
        GUILayout.Label("Scrub recorded storm time:");
        if (runner.Run != null)
            skin.playbackTime = GUILayout.HorizontalSlider(skin.playbackTime, 0, Mathf.Max(.01f, runner.Horizon));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Noon")) { skin.playbackTime = Mathf.Min(2700, runner.Horizon); skin.playing = false; }
        if (GUILayout.Button("Night")) { skin.playbackTime = Mathf.Min(8100, runner.Horizon); skin.playing = false; }
        if (GUILayout.Button("Latest")) { skin.playbackTime = runner.Horizon; skin.playing = false; }
        GUILayout.EndHorizontal();
        GUILayout.Label(complete ? "3-hour run complete. Scrub to review." : "Recording brain diagnostics and day/night views.");
        GUILayout.Label("Thunder at 1x only. Sky clock is visual compression.");
        GUILayout.Label("A sustained storm may remain active at the end.");
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        if (lightning) lightning.FlashStarted -= OnFlash;
    }
}
