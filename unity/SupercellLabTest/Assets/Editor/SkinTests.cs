// Phase 2 test: builds the skin test scene (URP asset with the StormSkinFeature, a camera, the sun, a SupercellRunner
// on the Dodge City 2016-05-24 HRRR sounding, a StormSkin), runs the storm headless, renders screenshots at several
// times and measures the skin's GPU cost. Batch (never with -nographics, the model and the skin need the GPU):
//   Unity.exe -batchmode -projectPath <SupercellLabTest> -executeMethod SkinTests.RunFromCommandLine -logFile skin.log
// Optional: -skinTimes 1500,2700,4200,6000 (model seconds)  -skinQuick (one time, two views)  -skinViews close,photo17
// (only these views)  -skinNoPhotos (skip the reference-photo framings).
// Writes Screenshots/skin_*.png, Screenshots/compare_*.png (reference photo | render) and Validation/SkinReport.md. The
// report keeps everything below the NotesMarker line, so hand-written notes survive a re-run.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

public static class SkinTests
{
    public const string ScenePath = "Assets/Scenes/SupercellSkinTest.unity";
    const string SoundingAsset = "Assets/StormData/ddc_20160524_hrrr.json";
    static string ProjectDir => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
    static string ShotDir => Path.Combine(ProjectDir, "Screenshots");
    static string OutDir => Path.Combine(ProjectDir, "Validation");
    static string RefDir => Path.Combine(RepoRoot, "data", "reference", "plains_lp");
    public const string NotesMarker = "<!-- Notes below this line are kept when the test rewrites the report. -->";
    /// <summary>an RTX 3060 is taken as 2.5x slower than the RX 7900 XT (compute and bandwidth, not measured)</summary>
    const double Rtx3060Factor = 2.5;
    const double BudgetMs = 8.0;

    [MenuItem("Supercell Lab/Build Skin Test Scene")]
    public static void BuildMenu() => BuildScene();

    [MenuItem("Supercell Lab/Run Skin Screenshots")]
    public static void RunMenu() => Run(null, false, null, true);

    public static void RunFromCommandLine()
    {
        bool ok;
        try
        {
            var args = System.Environment.GetCommandLineArgs();
            float[] times = null; bool quick = args.Contains("-skinQuick");
            int i = Array.IndexOf(args, "-skinTimes");
            if (i >= 0 && i + 1 < args.Length) times = args[i + 1].Split(',').Select(float.Parse).ToArray();
            int v = Array.IndexOf(args, "-skinViews");
            HashSet<string> views = v >= 0 && v + 1 < args.Length ? new HashSet<string>(args[v + 1].Split(',')) : null;
            if (args.Contains("-skinBuildOnly")) { BuildScene(); ok = true; }
            else ok = Run(times, quick, views, !args.Contains("-skinNoPhotos"));
        }
        catch (Exception e) { Debug.LogException(e); ok = false; }
        EditorApplication.Exit(ok ? 0 : 1);
    }

    // ================================================================== scene and pipeline
    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    static UniversalRenderPipelineAsset EnsurePipeline()
    {
        if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
        EnsureFolder("Assets/Settings");
        const string rdPath = "Assets/Settings/SkinTest_Renderer.asset", rpPath = "Assets/Settings/SkinTest_URP.asset";
        var rd = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rdPath);
        if (!rd)
        {
            // URP's own factory (internal) sets up the post-processing data the renderer needs
            var create = typeof(UniversalRenderPipelineAsset).GetMethod("CreateRendererAsset", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            rd = (UniversalRendererData)create.Invoke(null, new object[] { rdPath, RendererType.UniversalRenderer, false, "Renderer" });
        }
        if (!rd.rendererFeatures.Any(f => f is StormSkinFeature))
        {
            var feat = ScriptableObject.CreateInstance<StormSkinFeature>();
            feat.name = "StormSkinFeature";
            AssetDatabase.AddObjectToAsset(feat, rd);
            rd.rendererFeatures.Add(feat);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out string _, out long id);
            var so = new SerializedObject(rd);
            var map = so.FindProperty("m_RendererFeatureMap");
            map.arraySize = rd.rendererFeatures.Count;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
            so.ApplyModifiedPropertiesWithoutUndo();
            rd.SetDirty();
            EditorUtility.SetDirty(rd);
        }
        var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
        if (!rp)
        {
            rp = UniversalRenderPipelineAsset.Create(rd);
            AssetDatabase.CreateAsset(rp, rpPath);
        }
        rp.supportsCameraDepthTexture = true; rp.supportsHDR = true; rp.msaaSampleCount = 1; rp.renderScale = 1f;
        EditorUtility.SetDirty(rp);
        GraphicsSettings.defaultRenderPipeline = rp;
        int q = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
        QualitySettings.SetQualityLevel(q, false);
        AssetDatabase.SaveAssets();
        return rp;
    }

    public static void BuildScene()
    {
        EnsurePipeline();
        EnsureFolder("Assets/StormData"); EnsureFolder("Assets/Scenes");
        string src = Path.Combine(RepoRoot, "data", "raw", "soundings", "ddc_20160524_hrrr.json");
        File.Copy(src, Path.Combine(ProjectDir, SoundingAsset), true);
        AssetDatabase.ImportAsset(SoundingAsset);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 1f; cam.farClipPlane = 250000f; cam.fieldOfView = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        camGo.transform.position = new Vector3(15000f, 2f, -15000f);
        camGo.transform.rotation = Quaternion.LookRotation(new Vector3(-1f, 0.12f, 1f));

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.color = new Color(1f, 0.85f, 0.65f);
        Vector3 v = StormSkin.DefaultSunVol;   // 1.0's evening sun (volume frame x east, y north, z up)
        sunGo.transform.rotation = Quaternion.LookRotation(-new Vector3(v.x, v.z, v.y));
        RenderSettings.sun = sun;

        var stormGo = new GameObject("Supercell");
        var runner = stormGo.AddComponent<SupercellRunner>();
        runner.source = SupercellRunner.Source.SoundingJson;
        runner.sounding = AssetDatabase.LoadAssetAtPath<TextAsset>(SoundingAsset);
        runner.setup = new RunSetup { tier = SimTier.Average, runLength = 7200f };
        runner.gpuBudgetMs = 4f; runner.maxSpeed = 10f;
        var skin = stormGo.AddComponent<StormSkin>();
        skin.runner = runner; skin.sun = sun;
        skin.groundOrigin = new Vector2(-40000f, -40000f);

        // a tall object for the scene-depth check (inactive unless the test turns it on)
        var mast = GameObject.CreatePrimitive(PrimitiveType.Cube);
        mast.name = "Depth test mast"; mast.transform.localScale = new Vector3(120f, 600f, 120f); mast.SetActive(false);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log($"[Skin] scene saved: {ScenePath}");
    }

    // ================================================================== screenshots
    static void Sync() { var b = new ComputeBuffer(1, 4); var a = new int[1]; b.GetData(a); b.Release(); }

    // 12 frames: the temporal resolve (new-frame weight 0.25) has converged, as it has after 0.2 s in a game
    static Texture2D Shot(Camera cam, string name, int w = 1920, int h = 1080, int frames = 12)
    {
        var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 1 };
        rt.Create();
        for (int f = 0; f < frames; f++)
        {
            var req = new RenderPipeline.StandardRequest { destination = rt };
            if (RenderPipeline.SupportsRenderRequest(cam, req)) RenderPipeline.SubmitRenderRequest(cam, req);
            else { cam.targetTexture = rt; cam.Render(); cam.targetTexture = null; }
        }
        var prev = RenderTexture.active; RenderTexture.active = rt;
        var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
        RenderTexture.active = prev;
        if (name != null) File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), tex.EncodeToPNG());
        rt.Release(); Object.DestroyImmediate(rt);
        return tex;
    }

    static float Contrast(Texture2D t)
    {
        var px = t.GetPixels32(); double s = 0, s2 = 0;
        for (int i = 0; i < px.Length; i += 7) { double l = 0.3 * px[i].r + 0.59 * px[i].g + 0.11 * px[i].b; s += l; s2 += l * l; }
        double n = Math.Ceiling(px.Length / 7.0), m = s / n;
        return (float)Math.Sqrt(Math.Max(0, s2 / n - m * m));
    }

    /// <summary>horizontal unit vector toward a compass bearing (0 = north, 90 = east)</summary>
    static Vector3 Bearing(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));

    static void Pose(Camera cam, Vector3 from, Vector3 at, float fov)
    {
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.LookRotation(at - from, Vector3.up);
        cam.fieldOfView = fov;
    }

    /// <summary>
    /// A ground camera that copies a reference photo's framing: dist km from the low-level updraft U, looking at it with the
    /// forward-flank precipitation `rainRight` degrees to the right of the view (as in the photos: the photographer stands
    /// in the inflow with the rain to one side), pitched up `pitch` degrees, vertical field of view vfov, in the photo's
    /// aspect. `facing` is the photo's own compass facing, for the report.
    /// </summary>
    sealed class PhotoView
    {
        public string name, photo, note; public float dist, facing, rainRight, pitch, vfov; public int w, h;
    }
    static readonly PhotoView[] PhotoViews =
    {
        // 01: portrait, facing WSW about 8 pm, dark stacked-plates meso over grassland, thin precipitation to the right
        new PhotoView { name = "photo01", photo = "plains_lp_01.jpg", dist = 10f, facing = 250f, rainRight = 30f, pitch = 17f, vfov = 72f, w = 1080, h = 1620,
                        note = "portrait, 10 km from the meso, rain 30 degrees right" },
        // 17: portrait, very wide, facing W at sunset from a dirt road, the saucer base filling the frame
        new PhotoView { name = "photo17", photo = "plains_lp_17.jpg", dist = 7f, facing = 270f, rainRight = 45f, pitch = 24f, vfov = 96f, w = 1080, h = 1620,
                        note = "portrait, 7 km from the meso, rain 45 degrees right, 96 degree lens" },
        // 20: landscape 4:3, facing NW, the dark striated barrel against the sunset, precipitation to the right
        new PhotoView { name = "photo20", photo = "plains_lp_20.jpg", dist = 14f, facing = 315f, rainRight = 35f, pitch = 16f, vfov = 76f, w = 1600, h = 1200,
                        note = "4:3, 14 km from the meso, rain 35 degrees right" },
    };
    /// <summary>compass bearing the photo camera faces in this storm: the forward-flank rain rainRight degrees to the right</summary>
    static float Facing(SkinParams P, PhotoView v)
    {
        var d = (Vector2)P[Slot.Ffd] - (Vector2)P[Slot.Up];
        if (d.sqrMagnitude < 0.01f) d = (Vector2)P[Slot.F];
        return (Mathf.Atan2(d.x, d.y) * Mathf.Rad2Deg - v.rainRight + 360f) % 360f;
    }
    static void PosePhoto(Camera cam, Vector3 U, PhotoView v, SkinParams P)
    {
        float facing = Facing(P, v);
        Vector3 from = U - Bearing(facing) * v.dist * 1000f + Vector3.up * 2f;
        cam.transform.position = from;
        cam.transform.rotation = Quaternion.LookRotation(Bearing(facing), Vector3.up) * Quaternion.Euler(-v.pitch, 0f, 0f);
        cam.fieldOfView = v.vfov;
    }

    /// <summary>reference photo | render, both scaled to `height`, written to Screenshots/name.png</summary>
    static bool SideBySide(string photoFile, Texture2D render, string name, int height = 1080)
    {
        string path = Path.Combine(RefDir, photoFile);
        if (!File.Exists(path) || render == null) return false;
        var photo = new Texture2D(2, 2);
        if (!photo.LoadImage(File.ReadAllBytes(path))) { Object.DestroyImmediate(photo); return false; }
        int wp = Mathf.RoundToInt(height * photo.width / (float)photo.height), wr = Mathf.RoundToInt(height * render.width / (float)render.height), gap = 16;
        var o = new Texture2D(wp + gap + wr, height, TextureFormat.RGB24, false);
        var px = new Color[o.width * height];
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < wp; x++) px[y * o.width + x] = photo.GetPixelBilinear((x + 0.5f) / wp, v);
            for (int x = 0; x < gap; x++) px[y * o.width + wp + x] = Color.black;
            for (int x = 0; x < wr; x++) px[y * o.width + wp + gap + x] = render.GetPixelBilinear((x + 0.5f) / wr, v);
        }
        o.SetPixels(px); o.Apply();
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), o.EncodeToPNG());
        Object.DestroyImmediate(o); Object.DestroyImmediate(photo);
        return true;
    }

    /// <summary>
    /// Debug: plan views of the storm volume (cloud white, rain blue) at three heights, the near box (green), the low-level
    /// updraft (red) and the photo cameras (yellow), written to Screenshots/debug_map_*.png. 60 x 60 km, north up.
    /// </summary>
    static void DebugMap(StormSkin skin, string name, Vector3 U)
    {
        var vol = skin.Volume; int n = vol.NV; var P = skin.Params;
        float[] zs = { P[Slot.Base].x + 0.4f, 4f, 11f };
        var o = new Texture2D(n * 3 + 20, n, TextureFormat.RGB24, false);
        var px = new Color32[o.width * n];
        for (int panel = 0; panel < 3; panel++)
        {
            int k = Mathf.Clamp(Mathf.RoundToInt(zs[panel] / vol.ZT * vol.NZ), 0, vol.NZ - 1);
            var req = UnityEngine.Rendering.AsyncGPUReadback.Request(vol.Dens, 0, 0, n, 0, n, k, 1, TextureFormat.RGBA32);
            req.WaitForCompletion();
            if (req.hasError) continue;
            var data = req.GetData<Color32>();
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++)
            {
                var c = data[j * n + i];
                px[j * o.width + panel * (n + 10) + i] = new Color32((byte)(c.r * 0.8f), (byte)(c.r * 0.8f), (byte)Mathf.Min(255, c.r * 0.8f + c.g), 255);
            }
        }
        void Mark(Vector2 vkm, Color32 col, int r)
        {
            int ci = Mathf.RoundToInt((vkm.x + vol.L) / (2 * vol.L) * n), cj = Mathf.RoundToInt((vkm.y + vol.L) / (2 * vol.L) * n);
            for (int panel = 0; panel < 3; panel++)
                for (int dj = -r; dj <= r; dj++) for (int di = -r; di <= r; di++)
                {
                    int i = ci + di, j = cj + dj; if (i < 0 || j < 0 || i >= n || j >= n) continue;
                    px[j * o.width + panel * (n + 10) + i] = col;
                }
        }
        var nb = P[Slot.Near];
        if (nb.w > 0) for (float s = -nb.w; s <= nb.w; s += 0.1f)
        {
            Mark(new Vector2(nb.x + s, nb.y - nb.w), new Color32(0, 255, 0, 255), 0); Mark(new Vector2(nb.x + s, nb.y + nb.w), new Color32(0, 255, 0, 255), 0);
            Mark(new Vector2(nb.x - nb.w, nb.y + s), new Color32(0, 255, 0, 255), 0); Mark(new Vector2(nb.x + nb.w, nb.y + s), new Color32(0, 255, 0, 255), 0);
        }
        var up = P[Slot.Up]; Mark(new Vector2(up.x, up.y), new Color32(255, 0, 0, 255), 2);
        foreach (var v in PhotoViews)
        {
            var w = skin.WorldToVol(U - Bearing(Facing(P, v)) * v.dist * 1000f);
            Mark(new Vector2(w.x, w.y), new Color32(255, 255, 0, 255), 2);
        }
        o.SetPixels32(px); o.Apply();
        File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), o.EncodeToPNG());
        Object.DestroyImmediate(o);
    }

    /// <summary>pose the skin's sun: 1.0's evening sun (the scene's Light), or the low sunset sun of the reference photos</summary>
    static void SetSun(StormSkin skin, bool low, Quaternion lightRot, float exposure)
    {
        if (low) { skin.sunSource = SunSource.TimeOfDay; skin.solarHour = LowSunHour; skin.exposure = exposure * LowSunExposure; }
        else { skin.sunSource = SunSource.Light; if (skin.sun) skin.sun.transform.rotation = lightRot; skin.exposure = exposure; }
    }
    /// <summary>the low-sun variant: 18.8 h local solar time at Dodge City on 24 May (about 8:30 pm CDT): sun 3-4 degrees up in the WNW</summary>
    const float LowSunHour = 18.8f;
    /// <summary>the photographer's exposure for sunset light (the sun is 4x dimmer than 1.0's evening sun at this height)</summary>
    const float LowSunExposure = 1.8f;

    public static bool Run(float[] times, bool quick, HashSet<string> only, bool photos)
    {
        if (!File.Exists(Path.Combine(ProjectDir, ScenePath))) BuildScene();
        else EnsurePipeline();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ShaderUtil.allowAsyncCompilation = false;
        Directory.CreateDirectory(ShotDir); Directory.CreateDirectory(OutDir);
        var runner = Object.FindAnyObjectByType<SupercellRunner>();
        var skin = Object.FindAnyObjectByType<StormSkin>();
        var cam = Camera.main;
        var mast = GameObject.Find("Depth test mast") ?? Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(g => g.name == "Depth test mast");
        times = times ?? (quick ? new[] { 3600f } : new[] { 1500f, 2700f, 4200f, 6000f });
        float tEnd = times.Max() + 150f;
        var rep = new StringBuilder();
        rep.AppendLine("# Skin test report (Phase 2: storm volume and near field)").AppendLine();
        rep.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}, {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}, Unity {Application.unityVersion}.").AppendLine();
        bool ok = true;

        var setup = runner.setup; setup.runLength = Mathf.Max(setup.runLength, tEnd + 60f);
        using (var run = StormRun.FromSoundingJson(runner.sounding.text, setup))
        {
            skin.Activate();
            skin.Bind(run);
            Debug.Log($"[Skin] storm: {run.g}; {run.plan}");
            rep.AppendLine($"Storm: DDC 2016-05-24 HRRR, {run.g}, {run.plan}. Skin volume {skin.Volume.NV} x {skin.Volume.NV} x {skin.Volume.NZ} " +
                           $"({skin.Volume.VoxelKm * 1000:0} m), {skin.Volume.VramBytes / 1e6:0} MB.").AppendLine();
            if (skin.Near != null)
                rep.AppendLine($"Near field: {skin.Near.NV} x {skin.Near.NV} x {skin.Near.NZ} at {skin.Near.Dx * 1000:0} m ({2 * skin.Near.HalfW:0.0} x {2 * skin.Near.HalfW:0.0} x {skin.Near.H:0.00} km), " +
                               $"light {skin.Near.LN} x {skin.Near.LN} x {skin.Near.LZ}, {skin.Near.VramBytes / 1e6:0} MB.").AppendLine();

            // ---- run the brain ahead, capturing the wind at every timeline frame
            var sw = Stopwatch.StartNew(); int frames = 0;
            while (run.Time < tEnd - 1e-3f)
            {
                run.Advance(run.StepsToNextFrame);
                skin.Wind.Capture(run, run.Time);
                if (++frames % 30 == 0) { run.Pump(true); skin.Wind.Pump(true); }
            }
            run.Pump(true); skin.Wind.Pump(true);
            Debug.Log($"[Skin] simulated {run.Time / 60:0} min in {sw.Elapsed.TotalSeconds:0.0} s; {run.timeline.Latest + 1} frames, {skin.Wind.Count} wind snapshots ({skin.Wind.Bytes / 1e6:0.0} MB)");
            rep.AppendLine($"Simulated {run.Time / 60:0} min in {sw.Elapsed.TotalSeconds:0} s wall; {run.timeline.Latest + 1} timeline frames; {skin.Wind.Count} wind snapshots, {skin.Wind.Bytes / 1e6:0.0} MB.").AppendLine();

            // ---- screenshots
            rep.AppendLine("## Screenshots").AppendLine();
            rep.AppendLine("| Time | Stage | From the sim | From 1.0's fallback | Base / plates / curtain | Shots |").AppendLine("| --- | --- | --- | --- | --- | --- |");
            bool warmed = false;
            // -skinCloudOnly: no rain, light shafts or haze, to judge the cloud structure; -skinNearOnly: the near field alone
            var cl = System.Environment.GetCommandLineArgs();
            int dbg0 = (cl.Contains("-skinCloudOnly") ? 16 : 0) | (cl.Contains("-skinNearOnly") ? 48 : 0);
            skin.debugFlags = dbg0;
            float exposure0 = skin.exposure; var lightRot = skin.sun ? skin.sun.transform.rotation : Quaternion.identity;
            var photoTimes = new HashSet<float> { 2700f, 4200f, 6000f };
            var compares = new List<string>();
            bool Want(string view) => only == null || only.Contains(view);
            foreach (float T in times)
            {
                bool doPhotos = photos && !quick && photoTimes.Contains(T);
                // the photo framings get the 1.0 sun and, at 70 min, the low sunset sun as well
                var suns = new List<bool> { false };
                if (doPhotos && T == 4200f) suns.Add(true);
                foreach (bool low in suns)
                {
                    SetSun(skin, low, lightRot, exposure0);
                    skin.ResetVolume();
                    float t0 = Mathf.Max(0, T - 90f);
                    skin.StepTo(t0, t0);
                    for (float t = t0 + 2f; t <= T + 1e-3f; t += 2f) skin.StepTo(t, t);
                    Sync();
                    var P = skin.Params;
                    var up = P[Slot.Up]; var fw = P[Slot.F];
                    Vector3 U = skin.VolToWorld(new Vector3(up.x, up.y, 0));
                    Vector3 fwd = new Vector3(fw.x, 0, fw.y).normalized, right = new Vector3(fw.y, 0, -fw.x).normalized;   // right of the motion
                    Debug.Log($"[Skin] t={T / 60:0} min{(low ? " low sun" : "")}: {P.stage}; sim [{P.fromSim}] fallback [{P.fromFallback}]; tower {P[Slot.Tower]}, anvil {P[Slot.Anvil]}, wall {P[Slot.Wall]}, " +
                              $"rfdb {P[Slot.Rfdb]}, ffd {P[Slot.Ffd]}, plates {P[Slot.Plates]}, curtain {P[Slot.Curtain]}, near {P[Slot.Near]} (active {skin.NearActive}), sun {skin.SunDirVol}, volume centre {skin.Centre} suggested {P.suggestedCentre}, wind snapshot ok {skin.Wind.Upload(T)}");
                    if (!warmed) { Pose(cam, U + Bearing(160f) * 24000f + Vector3.up * 3f, U + Vector3.up * 2500f, 60f); Shot(cam, null); warmed = true; }
                    string tag = $"{T / 60:000}min";
                    if (!low && System.Environment.GetCommandLineArgs().Contains("-skinMap")) DebugMap(skin, $"debug_map_{tag}", U);
                if (!low && System.Environment.GetCommandLineArgs().Contains("-skinProfile"))
                {
                    // steps per pixel (red), detail evaluations (green), transmittance left (blue), as a fraction of maxSteps
                    skin.debugFlags = 64; bool tm = skin.toneMap; skin.toneMap = false; float ex = skin.exposure; skin.exposure = 1f;
                    Pose(cam, U + Bearing(160f) * 24000f + Vector3.up * 3f, U + Vector3.up * 2500f, 60f); skin.ResetHistory(cam);
                    Object.DestroyImmediate(Shot(cam, $"debug_steps_{tag}_chaser", 1920, 1080, 1));
                    skin.debugFlags = dbg0; skin.toneMap = tm; skin.exposure = ex;
                }
                    var names = new List<string>();
                    Texture2D Take(string view, int w = 1920, int h = 1080, bool keep = false)
                    {
                        skin.ResetHistory(cam);
                        string nm = $"skin_{tag}_{view}";
                        var tex = Shot(cam, nm, w, h);
                        float c = Contrast(tex);
                        names.Add(nm);
                        if (c < 4f) { ok = false; Debug.LogError($"[Skin] {tag} {view}: flat image (contrast {c:0.0})"); }
                        if (!keep) { Object.DestroyImmediate(tex); return null; }
                        return tex;
                    }
                    if (!low)
                    {
                        // views by compass bearing from the low-level updraft: a chaser south-south-east of the storm (off the
                        // anvil, looking north-north-west at the base), the whole storm from the south, a close look at the base
                        if (Want("chaser")) { Pose(cam, U + Bearing(160f) * 24000f + Vector3.up * 3f, U + Vector3.up * 2500f, 60f); Take("chaser"); }
                        if (Want("wide")) { Pose(cam, U + Bearing(185f) * 60000f + Vector3.up * 3f, U + Bearing(90f) * 8000f + Vector3.up * 6000f, 55f); Take("wide"); }
                        if (!quick)
                        {
                            if (Want("close")) { Pose(cam, U + Bearing(150f) * 12000f + Vector3.up * 3f, U + Vector3.up * 1200f, 70f); Take("close"); }
                            if (Want("aerial")) { Pose(cam, U - fwd * 10000f + right * 38000f + Vector3.up * 22000f, U + Vector3.up * 7000f + fwd * 8000f, 55f); Take("aerial"); }
                        }
                    }
                    if (doPhotos)
                    {
                        // ground cameras in the reference photos' framings (data/reference/plains_lp/NOTES.md)
                        foreach (var v in PhotoViews)
                        {
                            string view = v.name + (low ? "_lowsun" : "");
                            if (!Want(v.name) && !Want(view)) continue;
                            PosePhoto(cam, U, v, P);
                            var tex = Take(view, v.w, v.h, true);
                            if (T == 4200f && SideBySide(v.photo, tex, $"compare_{view}")) compares.Add($"compare_{view}");
                            Object.DestroyImmediate(tex);
                        }
                    }
                    var pl = P[Slot.Plates]; var cu = P[Slot.Curtain];
                    rep.AppendLine($"| {T / 60:0} min{(low ? ", low sun" : "")} | {P.stage} | {P.fromSim} | {P.fromFallback} | base {P[Slot.Base].x:0.00} km, {pl.x:0} plates over {pl.z:0.0} km (amount {pl.w:0.00}), curtain x{cu.x:0.00} (hpness {cu.w:0.00}) | {string.Join(", ", names)} |");
                }
            }
            SetSun(skin, false, lightRot, exposure0);
            rep.AppendLine();
            if (compares.Count > 0) rep.AppendLine($"Reference photo | render, 70 min: {string.Join(", ", compares.Select(c => "`" + c + ".png`"))}. Views: " +
                                                  string.Join("; ", PhotoViews.Select(v => $"{v.name} = {v.photo} ({v.note}; the photo faces {v.facing:0}, this storm's camera {Facing(skin.Params, v):0})")) + ". The low-sun variant puts the sun at " +
                                                  $"{StormSkin.SolarPosition(LowSunHour, skin.latitude, skin.dayOfYear).y:0.0} degrees, bearing {StormSkin.SolarPosition(LowSunHour, skin.latitude, skin.dayOfYear).x:0}.").AppendLine();
            rep.AppendLine();

            // ---- scene depth: a 600 m mast 4 km in front of the chaser must hide the storm behind it
            if (mast != null)
            {
                var P = skin.Params; var up = P[Slot.Up]; var fw = P[Slot.F];
                Vector3 U = skin.VolToWorld(new Vector3(up.x, up.y, 0));
                Vector3 fwd = new Vector3(fw.x, 0, fw.y).normalized, right = new Vector3(fw.y, 0, -fw.x).normalized;
                Vector3 from = U + Bearing(160f) * 24000f + Vector3.up * 3f;
                mast.SetActive(true);
                mast.transform.position = from + (U - from).normalized * 4000f + Vector3.up * 300f;
                Pose(cam, from, U + Vector3.up * 2500f, 60f); skin.ResetHistory(cam);
                var tex = Shot(cam, "skin_depth_test"); Object.DestroyImmediate(tex);
                mast.SetActive(false);
                rep.AppendLine("Scene depth: `skin_depth_test.png` has a 600 m mast 4 km in front of the camera, standing in front of the storm.").AppendLine();
            }

            // ---- GPU cost
            rep.AppendLine("## GPU cost (this PC)").AppendLine();
            skin.debugFlags = 0;
            var cost = MeasureCost(skin, cam, out double total);
            foreach (var kv in cost) rep.AppendLine($"- {kv.Key}: **{kv.Value:0.00} ms**");
            double est = total * Rtx3060Factor;
            rep.AppendLine($"- estimate on an RTX 3060 ({Rtx3060Factor}x, from compute and bandwidth, not measured): **{est:0.0} ms** of the {BudgetMs:0} ms storm budget");
            rep.AppendLine();
            Debug.Log("[Skin] cost: " + string.Join("; ", cost.Select(kv => $"{kv.Key} {kv.Value:0.00} ms")) + $"; RTX 3060 estimate {est:0.0} ms");
            if (est > BudgetMs) { ok = false; Debug.LogError($"[Skin] RTX 3060 estimate {est:0.0} ms is over the {BudgetMs} ms budget"); }
            skin.Release();
        }
        // keep the hand-written notes below the marker
        string repPath = Path.Combine(OutDir, "SkinReport.md");
        string notes = File.Exists(repPath) ? File.ReadAllText(repPath) : "";
        int mk = notes.IndexOf(NotesMarker, StringComparison.Ordinal);
        rep.AppendLine(NotesMarker).AppendLine();
        if (mk >= 0) rep.Append(notes.Substring(mk + NotesMarker.Length).TrimStart('\r', '\n'));
        File.WriteAllText(repPath, rep.ToString());
        Debug.Log($"[Skin] {(ok ? "PASS" : "FAIL")}; screenshots in {ShotDir}, report in {OutDir}");
        return ok;
    }

    static double Time(Action a, int n)
    {
        a(); Sync();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < n; i++) a();
        Sync();
        return sw.Elapsed.TotalMilliseconds / n;
    }

    static Dictionary<string, double> MeasureCost(StormSkin skin, Camera cam, out double total)
    {
        var d = new Dictionary<string, double>();
        var cmd = new CommandBuffer();
        double empty = Time(() => { cmd.Clear(); Graphics.ExecuteCommandBuffer(cmd); }, 20);
        var P = skin.Params; var vol = skin.Volume; var noise = CloudNoise.Make();
        float clock = skin.ShownTime;
        double build = Time(() => { cmd.Clear(); vol.Build(cmd, P, noise, 0, vol.NZ); Graphics.ExecuteCommandBuffer(cmd); }, 10) - empty;
        double adv = Time(() => { cmd.Clear(); vol.Advect(cmd, P, skin.Wind, true, skin.Centre, Vector2.zero, 2f, 0.03f); Graphics.ExecuteCommandBuffer(cmd); }, 20) - empty;
        double light = Time(() => { cmd.Clear(); vol.Light(cmd, skin.SunDirVol, P[Slot.Base].x, 0, vol.LZ); Graphics.ExecuteCommandBuffer(cmd); }, 10) - empty;
        d["storm volume: full target paint (BUILD, whole volume)"] = build;
        d["storm volume: advection (whole volume)"] = adv;
        d["storm volume: light grid (whole grid)"] = light;
        double frameVol = build / Math.Max(1, skin.buildSlabs) + adv / Math.Max(1, vol.advectEvery) + light / Math.Max(1, skin.lightSlabs);
        d[$"storm volume work per frame (1/{skin.buildSlabs} paint + 1/{vol.advectEvery} advection + 1/{skin.lightSlabs} light)"] = frameVol;
        double frameNear = 0;
        if (skin.NearActive)
        {
            var near = skin.Near; var sp = skin.GpuParams();
            double nb = Time(() => { cmd.Clear(); near.Build(cmd, sp, noise, vol, skin.Wind, true, skin.Centre, 0, near.NZ); Graphics.ExecuteCommandBuffer(cmd); }, 10) - empty;
            double nl = Time(() => { cmd.Clear(); near.Light(cmd, sp, vol, skin.SunDirVol, 0, near.LZ); Graphics.ExecuteCommandBuffer(cmd); }, 10) - empty;
            d["near field: full paint (whole box)"] = nb;
            d["near field: light (whole grid)"] = nl;
            frameNear = nb / Math.Max(1, skin.nearSlabs) + nl / Math.Max(1, skin.nearLightSlabs);
            d[$"near field work per frame (1/{skin.nearSlabs} paint + 1/{skin.nearLightSlabs} light)"] = frameNear;
        }
        var up = P[Slot.Up];
        Vector3 U = skin.VolToWorld(new Vector3(up.x, up.y, 0));
        var poses = new List<(string, Action)>
        {
            ("chaser view, 24 km", () => Pose(cam, U + Bearing(160f) * 24000f + Vector3.up * 3f, U + Vector3.up * 2500f, 60f)),
            ("close view, 12 km", () => Pose(cam, U + Bearing(150f) * 12000f + Vector3.up * 3f, U + Vector3.up * 1200f, 70f)),
        };
        foreach (var v in PhotoViews) { var vv = v; poses.Add(($"{v.name} view, {v.dist:0} km", () => PosePhoto(cam, U, vv, P))); }
        double worst = 0; string worstName = "";
        foreach (var (name, pose) in poses)
        {
            pose();
            skin.ResetHistory(cam);
            double m = Time(() => { cmd.Clear(); skin.RecordMarch(cmd, cam, null, 2560, 1440); Graphics.ExecuteCommandBuffer(cmd); }, 20) - empty;
            d[$"ray-march + resolve, 2560x1440 at {skin.renderScale:0.##} scale, {name}"] = m;
            if (m > worst) { worst = m; worstName = name; }
        }
        if (System.Environment.GetCommandLineArgs().Contains("-skinProfile"))
            foreach (int fl in new[] { 1, 2, 4, 7 })
            {
                poses[0].Item2(); skin.ResetHistory(cam); skin.debugFlags = fl;
                double mm = Time(() => { cmd.Clear(); skin.RecordMarch(cmd, cam, null, 2560, 1440); Graphics.ExecuteCommandBuffer(cmd); }, 20) - empty;
                Debug.Log($"[Skin] profile chaser march with debug flags {fl}: {mm:0.00} ms");
                skin.debugFlags = 0;
            }
        poses[0].Item2(); skin.ResetHistory(cam);
        double m1080 = Time(() => { cmd.Clear(); skin.RecordMarch(cmd, cam, null, 1920, 1080); Graphics.ExecuteCommandBuffer(cmd); }, 20) - empty;
        d[$"ray-march + resolve, 1920x1080 at {skin.renderScale:0.##} scale, chaser view"] = m1080;
        total = frameVol + frameNear + worst;
        d[$"total per frame at 1440p (storm volume + near field + the dearest march, {worstName})"] = total;
        Object.DestroyImmediate(noise);
        cmd.Release();
        return d;
    }
}
