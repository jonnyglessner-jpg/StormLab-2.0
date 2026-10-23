using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using StormLab.Supercell;
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Lightning;
using StormLab.Supercell.Rainfall;
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
    public LocalRainfall rainfall;
    public BrainRainSource rainSource;
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

    Vector3 cameraTarget, lastCameraAnchor;
    float cameraYaw, cameraPitch, cameraDistance;
    bool cameraReady;
    int cameraDragButton = -1, cameraControlId;
    BrainPreviewPanel diagnosticsPanel;
    Vector2 panelScroll;
    Rect PreviewPanelRect => new Rect(Mathf.Max(425, Screen.width - 390), 12, 375, Mathf.Min(570, Screen.height - 24));

    void OnEnable() => HideRainVisuals();

    void HideRainVisuals()
    {
        // Temporarily suppress volumetric rain, shafts and rain haze; keep model water intact.
        if (skin) skin.debugFlags |= 16;
        if (rainfall) rainfall.enabled = false;
    }

    void Awake()
    {
        ConfigureRainfall();
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
        var panel = diagnosticsPanel = runner.GetComponent<BrainPreviewPanel>();
        if (panel) panel.allowCameraControl = false;
    }

    // Also used by the scene builder. Handles an already-open copy of the scene after a script refresh.
    public void ConfigureRainfall()
    {
        if (!rainfall) rainfall = GetComponent<LocalRainfall>();
        if (!rainfall) rainfall = gameObject.AddComponent<LocalRainfall>();
        if (!rainSource) rainSource = GetComponent<BrainRainSource>();
        if (!rainSource) rainSource = gameObject.AddComponent<BrainRainSource>();
        rainfall.targetCamera = observer;
        rainfall.rainMmPerHour = 0;
        rainfall.groundHeight = skin.groundHeight;
        rainfall.weatherSource = rainSource;
        rainSource.skin = skin;
        rainSource.rainfall = rainfall;
        HideRainVisuals();
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
        UpdateCamera();
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

    Vector3 CameraAnchor()
    {
        var anchor = new Vector2(runner.Run.g.Lx * .45f, runner.Run.g.Ly * .5f);
        return CameraGroundAt(anchor, skin.groundOrigin, skin.FrameMotion, skin.groundHeight, skin.ShownTime);
    }

    void ResetCamera()
    {
        if (!observer || runner.Run == null) return;
        lastCameraAnchor = CameraAnchor();
        cameraTarget = lastCameraAnchor + Vector3.up * 4000;
        var offset = new Vector3(15000, -3998, -15000);
        cameraDistance = offset.magnitude;
        cameraYaw = -45;
        cameraPitch = Mathf.Asin(offset.y / cameraDistance) * Mathf.Rad2Deg;
        cameraReady = true;
        ApplyCamera();
    }

    void UpdateCamera()
    {
        if (!observer) return;
        if (!cameraReady) ResetCamera();
        Vector3 anchor = CameraAnchor();
        // Preserve orbit and pan through playback, pause, and explicit timeline seeks.
        if (followStorm) cameraTarget += anchor - lastCameraAnchor;
        lastCameraAnchor = anchor;
        ApplyCamera();
    }

    void ApplyCamera()
    {
        if (!cameraReady || !observer) return;
        Vector3 position = cameraTarget + Quaternion.Euler(cameraPitch, cameraYaw, 0) * Vector3.back * cameraDistance;
        position.y = Mathf.Max(skin.groundHeight + 2, position.y);
        observer.transform.SetPositionAndRotation(position, Quaternion.LookRotation(cameraTarget - position));
    }

    void HandleCameraInput()
    {
        // IMGUI events work with the project's new Input System without a legacy Input dependency.
        var e = Event.current;
        cameraControlId = GUIUtility.GetControlID("BrainOrbitCamera".GetHashCode(), FocusType.Passive);
        if (!cameraReady || !observer) return;
        bool overPanel = PreviewPanelRect.Contains(e.mousePosition) ||
            (diagnosticsPanel && diagnosticsPanel.PanelRect.Contains(e.mousePosition));
        if (e.type == EventType.MouseDown && (e.button == 1 || e.button == 2) &&
            !overPanel && GUIUtility.hotControl == 0)
        {
            cameraDragButton = e.button;
            GUIUtility.hotControl = cameraControlId;
            e.Use();
        }
        if (e.type == EventType.MouseDrag && GUIUtility.hotControl == cameraControlId)
        {
            if (cameraDragButton == 2 || e.shift)
            {
                float metresPerPixel = 2 * cameraDistance * Mathf.Tan(observer.fieldOfView * Mathf.Deg2Rad * .5f) /
                    Mathf.Max(1, Screen.height);
                cameraTarget += (-observer.transform.right * e.delta.x + observer.transform.up * e.delta.y) * metresPerPixel;
                cameraTarget.y = Mathf.Max(skin.groundHeight + 2, cameraTarget.y);
            }
            else
            {
                cameraYaw = Mathf.Repeat(cameraYaw + e.delta.x * .25f, 360);
                cameraPitch = Mathf.Clamp(cameraPitch + e.delta.y * .25f, -80, 89);
            }
            ApplyCamera();
            e.Use();
        }
        if (e.type == EventType.MouseUp && GUIUtility.hotControl == cameraControlId && e.button == cameraDragButton)
        {
            GUIUtility.hotControl = 0;
            cameraDragButton = -1;
            e.Use();
        }
        if (e.type == EventType.ScrollWheel && !overPanel && GUIUtility.hotControl == 0)
        {
            cameraDistance = Mathf.Clamp(cameraDistance * Mathf.Exp(e.delta.y * .08f), 100, 150000);
            ApplyCamera();
            e.Use();
        }
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) ReleaseCameraDrag();
    }

    void OnDisable() => ReleaseCameraDrag();

    void ReleaseCameraDrag()
    {
        if (cameraDragButton >= 0 && GUIUtility.hotControl == cameraControlId) GUIUtility.hotControl = 0;
        cameraDragButton = -1;
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
        HandleCameraInput();
        GUILayout.BeginArea(PreviewPanelRect, GUI.skin.box);
        panelScroll = GUILayout.BeginScrollView(panelScroll);
        GUILayout.Label("BRAIN + LIGHTNING + DAY / NIGHT");
        GUILayout.Label($"Solar time {skin.solarHour:00.00} | 8 sky hours / model hour");
        GUILayout.Label($"Lightning: {driver.Bound} | shown events: {flashes}");
        GUILayout.Label(driver.Current.HasValue ? $"Activity: {driver.Current.Value.flashesPerMinute:0.0} flashes/min" : "Waiting for brain lightning diagnostics");
        followStorm = GUILayout.Toggle(followStorm, "Follow moving storm (keep viewing angle)");
        GUILayout.Label("Right-drag: orbit 360 degrees | Wheel: zoom");
        GUILayout.Label("Middle-drag / Shift + right-drag: pan");
        if (GUILayout.Button("Reset camera to storm")) ResetCamera();
        GUILayout.Label("Rain visuals disabled.");
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
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        Application.logMessageReceived -= OnLog;
        if (lightning) lightning.FlashStarted -= OnFlash;
    }
}
