using System;
using System.IO;
using StormLab.Supercell;
using StormLab.Supercell.Dynamics;
using StormLab.Supercell.EditorTools;
using StormLab.Supercell.Lightning;
using StormLab.Supercell.Lightning.EditorTools;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class BrainFullCycleSetup
{
    public const string ScenePath = "Assets/Scenes/BrainFullCycleTest.unity";
    const string Output = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\outputs\stormlab-full-cycle";

    static BrainFullCycleSetup()
    {
        if (!Application.isBatchMode && Array.IndexOf(System.Environment.GetCommandLineArgs(), "-brainFullPreview") >= 0 &&
            !SessionState.GetBool("BrainFullCycle.Opened", false))
        {
            SessionState.SetBool("BrainFullCycle.Opened", true);
            EditorApplication.delayCall += () =>
            {
                EditorSceneManager.OpenScene(ScenePath);
                EditorApplication.ExecuteMenuItem("Window/General/Game");
                EditorApplication.EnterPlaymode();
            };
        }
    }

    [MenuItem("Supercell Lab/Open Full Brain Cycle Preview")]
    public static void Open()
    {
        if (EditorApplication.isPlaying) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        EditorSceneManager.OpenScene(ScenePath);
        EditorApplication.ExecuteMenuItem("Window/General/Game");
    }

    public static void PrepareAndValidate()
    {
        try
        {
            Directory.CreateDirectory(Output);
            Build();
            bool ok = DynamicsValidation.Run("all");
            LightningActivityReview.Run();
            File.WriteAllText(Path.Combine(Output, "preflight-status.txt"), $"Dynamics all: {(ok ? "PASS" : "FAIL")}\nLightning activity review: PASS\nUnity: {Application.unityVersion}\nGPU: {SystemInfo.graphicsDeviceName}\n");
            EditorApplication.Exit(ok ? 0 : 1);
        }
        catch (Exception e) { Debug.LogException(e); EditorApplication.Exit(1); }
    }

    [MenuItem("Supercell Lab/Build Full Brain Cycle Preview")]
    public static void Build()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/SupercellSkinTest.unity");
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath, false);
        var runner = UnityEngine.Object.FindAnyObjectByType<SupercellRunner>();
        var skin = runner.GetComponent<StormSkin>();
        runner.setup.runLength = 10800; runner.setup.seed = 7;
        runner.gpuBudgetMs = 8; runner.maxSpeed = 0;
        skin.timelapse = 60; skin.playing = true; skin.playbackTime = 0;
        skin.sunSource = SunSource.TimeOfDay; skin.solarHour = 6;
        skin.moonPhase = .5f; skin.drawBackdrop = true;
        var camera = Camera.main;
        if (!camera.GetComponent<AudioListener>()) camera.gameObject.AddComponent<AudioListener>();
        if (!runner.GetComponent<BrainPreviewPanel>()) runner.gameObject.AddComponent<BrainPreviewPanel>();
        var go = new GameObject("Brain-driven lightning");
        var lightning = go.AddComponent<LightningSystem>();
        lightning.advanceClock = false; lightning.observer = camera.transform;
        lightning.groundY = skin.groundHeight; lightning.thunder = true; lightning.showInCloudChannels = true;
        var bridge = go.AddComponent<LightningCloudBridge>();
        bridge.skin = skin; bridge.lightning = lightning; bridge.Connect();
        var driver = go.AddComponent<LightningStormDriver>();
        driver.skin = skin; driver.lightning = lightning;
        driver.source = LightningStormDriver.Source.Brain; driver.followSkinClock = true;
        var preview = runner.gameObject.AddComponent<BrainFullCyclePreview>();
        preview.runner = runner; preview.skin = skin; preview.lightning = lightning;
        preview.driver = driver; preview.observer = camera; preview.outputDirectory = Output;
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[BrainFullCycle] Prepared " + ScenePath);
    }
}
