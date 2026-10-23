// The Tornado Viewer scene (Runtime/Viewer/TornadoViewer.cs): a camera, the sun and a designed storm with the viewer on it.
//   Supercell Lab > Build Tornado Viewer Scene   writes Assets/Scenes/TornadoViewer.unity
//   Supercell Lab > Open Tornado Viewer          opens it (building it if missing) and presses Play
// Play-mode check (not batch mode: it needs the Game view), which writes Screenshots/viewer_*.png and quits:
//   Unity.exe -projectPath <SupercellLabTest> -executeMethod TornadoViewerScene.CheckFromCommandLine -logFile viewer.log
using System.IO;
using StormLab.Supercell;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class TornadoViewerScene
{
    public const string ScenePath = "Assets/Scenes/TornadoViewer.unity";
    const string CheckKey = "TornadoViewerScene.check";

    static TornadoViewerScene() { EditorApplication.playModeStateChanged += OnPlayMode; }

    [MenuItem("Supercell Lab/Build Tornado Viewer Scene")]
    public static void Build()
    {
        SkinTests.EnsurePipeline();
        SkinTests.EnsureFolder("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.nearClipPlane = 1f; cam.farClipPlane = 250000f; cam.fieldOfView = 55f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        SkinTests.SetupCamera(camGo);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.color = new Color(1f, 0.85f, 0.65f);
        sunGo.transform.rotation = Quaternion.LookRotation(-StormSkin.VolToWorldDir(StormSkin.DefaultSunVol));
        RenderSettings.sun = sun;

        var go = new GameObject("Tornado Viewer");
        var skin = go.AddComponent<StormSkin>(); skin.sun = sun; skin.UseStandaloneLook();
        var runner = go.AddComponent<DesignedStormRunner>(); runner.skin = skin; runner.spawnOnStart = false;
        var viewer = go.AddComponent<TornadoViewer>(); viewer.cam = cam;

        EditorSceneManager.SaveScene(scene, ScenePath);
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == ScenePath)) list.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Viewer] scene saved: {ScenePath}");
    }

    [MenuItem("Supercell Lab/Open Tornado Viewer")]
    public static void Open()
    {
        if (!File.Exists(ScenePath)) Build();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    // ------------------------------------------------------------------ play-mode check
    public static void CheckFromCommandLine()
    {
        Build();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        SessionState.SetBool(CheckKey, true);
        EditorApplication.EnterPlaymode();
    }

    static void OnPlayMode(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(CheckKey, false)) return;
        SessionState.SetBool(CheckKey, false);
        double t0 = EditorApplication.timeSinceStartup; int step = 0;
        string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
        Directory.CreateDirectory(dir);
        // each step: change the viewer, wait for the skin to settle, capture the Game view (panel included)
        var plan = new (float wait, string name, System.Action<TornadoViewer> set)[]
        {
            (10f, "viewer_cone", v => { }),
            (8f, "viewer_rope_sunlit", v => { v.shape = TornadoShape.Rope; v.ef = 1; v.widthM = 90; v.yaw = v.sunAzimuth; v.distance = 1800; Dirty(v); }),
            (8f, "viewer_wedge_backlit", v => { v.shape = TornadoShape.Wedge; v.ef = 4; v.widthM = 1400; v.yaw = v.sunAzimuth + 180f; v.distance = 6000; Dirty(v); }),
            (8f, "viewer_own_storm", v => { v.force = false; v.preset = "dixie"; v.region = ClimoRegion.Dixie; v.seed = 5; v.yaw = 140f; v.distance = 4000; v.life = 0.5f; DirtyStorm(v); }),
        };
        float next = plan[0].wait; bool captured = false;
        EditorApplication.CallbackFunction tick = null;
        tick = () =>
        {
            var v = Object.FindAnyObjectByType<TornadoViewer>();
            if (v == null) return;
            if (EditorApplication.timeSinceStartup - t0 < next) return;
            // capture, then change the viewer only after the capture has been written (it is taken at the end of a frame)
            if (!captured) { ScreenCapture.CaptureScreenshot(Path.Combine(dir, plan[step].name + ".png")); captured = true; next += 1.0f; return; }
            captured = false;
            Debug.Log($"[Viewer] {plan[step].name}: {Object.FindAnyObjectByType<DesignedStormRunner>()?.Stage}");
            step++;
            if (step >= plan.Length)
            {
                EditorApplication.update -= tick;
                // one more frame so the last capture is written
                EditorApplication.delayCall += () => EditorApplication.delayCall += () => { Debug.Log("[Viewer] check done"); EditorApplication.Exit(0); };
                return;
            }
            plan[step].set(v);
            next = (float)(EditorApplication.timeSinceStartup - t0) + plan[step].wait;
        };
        EditorApplication.update += tick;
    }

    static void Dirty(TornadoViewer v) => v.MarkTornadoDirty();
    static void DirtyStorm(TornadoViewer v) => v.MarkStormDirty();
}
