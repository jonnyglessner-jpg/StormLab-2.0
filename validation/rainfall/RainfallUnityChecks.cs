// Copy into an isolated Unity project's Assets/Editor alongside the standalone rainfall module.
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using StormLab.Supercell.Rainfall;

public static class RainfallUnityChecks
{
    static void Check(bool ok, string text) { if (!ok) throw new Exception(text); Debug.Log("PASS: " + text); }
    public static void Run()
    {
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cam = new GameObject("Camera").AddComponent<Camera>();
            cam.transform.position = new Vector3(0, 2, -8);
            cam.transform.rotation = Quaternion.Euler(3, 0, 0);
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.045f,.07f,.1f);
            cam.nearClipPlane = .1f; cam.farClipPlane = 100;
            var rain = new GameObject("Rain").AddComponent<LocalRainfall>();
            rain.targetCamera = cam; rain.rainMmPerHour = 50; rain.volumeSize = new Vector3(24,16,24);
            rain.maxDrops = 2048; rain.maxSpawnsPerFrame = 256;
            rain.widthMultiplier = 8; rain.tint = new Color(.65f,.75f,.85f,.8f);
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.transform.position = new Vector3(4,4,0); roof.transform.localScale = new Vector3(6,.3f,6);
            Physics.SyncTransforms();
            Check(Physics.Raycast(new Vector3(4,1,0),Vector3.up,10), "Shelter collider detected");
            for (int i = 0; i < 300; i++) rain.Tick(1f/60);
            Check(rain.ActiveDrops > 20 && rain.ActiveDrops <= rain.maxDrops, "Near rain populates and respects budget");
            Check(Math.Abs(rain.Spectrum.RainMmPerHour-50) < .0001, "Graphics budget preserves diagnostic rain rate");
            var gauge = new GameObject("Gauge").AddComponent<RainGauge>();
            gauge.fallbackRainfall = rain; gauge.transform.position = new Vector3(4,1,0);
            gauge.Tick(60); Check(gauge.AccumulationMm == 0, "Covered gauge stays dry");
            gauge.transform.position = new Vector3(-4,1,0);
            gauge.Tick(60); Check(Math.Abs(gauge.AccumulationMm-50.0/60) < 1e-6, "Outdoor gauge integrates mm/hour into mm");
            // Horizontal wind can carry drops under roof edges; cover the whole volume for a sealed shelter test.
            roof.transform.position = new Vector3(0,15,-8); roof.transform.localScale = new Vector3(1000,.5f,1000);
            Physics.SyncTransforms(); rain.ResetDrops();
            for (int i = 0; i < 120; i++) rain.Tick(1f/60);
            Check(rain.ActiveDrops == 0, "Roof blocks spawning throughout a covered volume");
            UnityEngine.Object.DestroyImmediate(roof.gameObject);
            rain.ResetDrops();
            for (int i = 0; i < 180; i++) rain.Tick(1f/60);
            var shader = Resources.Load<Shader>("StormRainStreak");
            Check(shader && !ShaderUtil.ShaderHasError(shader), "Rain streak shader compiles");
            string outDir = Path.GetFullPath("../review-output"); Directory.CreateDirectory(outDir);
            var rt = new RenderTexture(960,540,24);
            cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
            var image = new Texture2D(960,540,TextureFormat.RGB24,false);
            image.ReadPixels(new Rect(0,0,960,540),0,0); image.Apply();
            File.WriteAllBytes(Path.Combine(outDir,"rainfall-preview.png"),image.EncodeToPNG());
            File.WriteAllText(Path.Combine(outDir,"unity-report.txt"),"PASS: Unity rainfall compile, population/budget, rain flux, collider shelter, streak shader; preview rendered.\n");
            cam.targetTexture = null; RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(rt);
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }
}
