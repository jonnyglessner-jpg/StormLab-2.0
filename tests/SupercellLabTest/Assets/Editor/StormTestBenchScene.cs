// The Storm Test Bench scene (Runtime/Viewer/StormTestBench.cs): a camera, the sun and a designed storm with the bench on it.
//   Supercell Lab > Open Test Bench (Ctrl+Shift+T)   opens the scene (building it if missing) and presses Play
//   Supercell Lab > Build Test Bench Scene           rebuilds Assets/Scenes/StormTestBench.unity
//   Supercell Lab > Reset Test Bench Memory          forgets the last session's storm, time and camera
// The loop for testing a change: edit the package, come back to Unity (it recompiles), Ctrl+Shift+T. The bench comes
// back on the same storm, time and view as last time, so before and after are directly comparable (F12 saves a shot).
using System.IO;
using StormLab.Supercell;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class StormTestBenchScene
{
    public const string ScenePath = "Assets/Scenes/StormTestBench.unity";
    public const string VariantScenePath = "Assets/Scenes/DesignedVariantPreview.unity";

    static StormTestBenchScene() { StormTestBench.Capture = path => ScreenCapture.CaptureScreenshot(path); }

    [MenuItem("Supercell Lab/Build Test Bench Scene")]
    public static void Build() => BuildScene(ScenePath, false);

    /// <summary>Rebuild every lab scene from its builder (after a pipeline change). Batch:
    /// -executeMethod StormTestBenchScene.RebuildAll -quit</summary>
    [MenuItem("Supercell Lab/Rebuild All Lab Scenes")]
    public static void RebuildAll()
    {
        SkinTests.BuildScene();
        Build();
        BuildScene(VariantScenePath, true);
        TornadoViewerScene.Build();
        BrainFullCycleSetup.Build();
    }

    static void BuildScene(string path, bool comparison)
    {
        SkinTests.EnsurePipeline();
        SkinTests.EnsureFolder("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        camGo.AddComponent<AudioListener>();
        cam.nearClipPlane = 1f; cam.farClipPlane = 250000f; cam.fieldOfView = 60f;
        cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
        SkinTests.SetupCamera(camGo);

        var sunGo = new GameObject("Sun");
        var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f; sun.color = new Color(1f, 0.85f, 0.65f);
        sunGo.transform.rotation = Quaternion.LookRotation(-StormSkin.VolToWorldDir(StormSkin.DefaultSunVol));
        RenderSettings.sun = sun;

        var go = new GameObject("Storm Test Bench");
        var skin = go.AddComponent<StormSkin>(); skin.sun = sun; skin.UseStandaloneLook();
        var runner = go.AddComponent<DesignedStormRunner>(); runner.skin = skin; runner.spawnOnStart = false;
        var bench = go.AddComponent<StormTestBench>(); bench.cam = cam;
        if (comparison) { bench.remember = false; bench.StructureComparison(); }

        EditorSceneManager.SaveScene(scene, path);
        var list = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
        if (!list.Exists(s => s.path == path)) list.Add(new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = list.ToArray();
        AssetDatabase.SaveAssets();
        Debug.Log($"[Bench] scene saved: {path}");
    }

    [MenuItem("Supercell Lab/Open Designed Variant Preview")]
    public static void OpenVariantPreview()
    {
        if (EditorApplication.isPlaying)
        {
            EditorApplication.ExitPlaymode();
            return; // Use the menu again once Play mode has ended.
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(VariantScenePath)) BuildScene(VariantScenePath, true);
        else EditorSceneManager.OpenScene(VariantScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Supercell Lab/Open Test Bench %#t")]
    public static void Open()
    {
        if (EditorApplication.isPlaying) { EditorApplication.ExitPlaymode(); return; }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (!File.Exists(ScenePath)) Build();
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        EditorApplication.EnterPlaymode();
    }

    [MenuItem("Supercell Lab/Reset Test Bench Memory")]
    public static void ResetMemory()
    {
        PlayerPrefs.DeleteKey("StormLab.TestBench");
        PlayerPrefs.Save();
        Debug.Log("[Bench] last session forgotten; the next Play starts from the defaults");
    }
}
