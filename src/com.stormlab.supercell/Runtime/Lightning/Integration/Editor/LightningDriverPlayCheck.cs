using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StormLab.Supercell.Lightning.EditorTools
{
    /// <summary>Runs LightningStormDriver in Play on a designed storm, first on the skin's own clock, then with the Storm
    /// Test Bench driving it: clock follow, the live readout, thunder at 1x only, a seek, a storm swap and 1x late in a long
    /// storm at 240 fps (where float rounding of playbackTime is a large share of a frame's step). Opens an empty
    /// scene for the run and reopens the previous one afterwards.</summary>
    [InitializeOnLoad]
    public static class LightningDriverPlayCheck
    {
        const string PendingKey = "StormLab.LightningDriverPlayCheck.Pending", SceneKey = "StormLab.LightningDriverPlayCheck.Scene";

        static LightningDriverPlayCheck()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(PendingKey, false))
                { SessionState.SetBool(PendingKey, false); new Run().Begin(); }
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    string path = SessionState.GetString(SceneKey, "");
                    SessionState.EraseString(SceneKey);
                    if (path != "") EditorApplication.delayCall += () => EditorSceneManager.OpenScene(path);
                }
            };
        }

        [MenuItem("Supercell Lab/Lightning/Check Storm Driver In Play")]
        public static void Start()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Exit Play mode before the storm driver check.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            SessionState.SetString(SceneKey, EditorSceneManager.GetActiveScene().path);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            EditorApplication.EnterPlaymode();
        }

        sealed class Phase
        {
            public string name; public int frames, clockOk, activityOk, readoutOk, scaleOne, flashes, thunder;
            public double expected; public float modelStart, modelEnd; public string readout = "";
            public readonly List<float> scales = new List<float>();
            public float MedianScale => scales.Count == 0 ? float.NaN : scales.OrderBy(s => s).ElementAt(scales.Count / 2);
        }

        sealed class Run
        {
            readonly string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../validation/lightning/out/driver"));
            readonly List<string> results = new List<string>();
            readonly HashSet<AudioSource> voices = new HashSet<AudioSource>(), heard = new HashSet<AudioSource>();
            readonly Dictionary<AudioSource, float> voiceBorn = new Dictionary<AudioSource, float>();
            readonly List<float> thunderDelays = new List<float>();
            StormSkin skin; DesignedStormRunner runner; StormTestBench bench; LightningSystem lightning; LightningStormDriver driver;
            Transform ear; SerializedObject driverObject; Phase phase; IEnumerator steps; int lastFrame = -1, vSync; float lastModel = float.NaN;

            public void Begin()
            {
                Directory.CreateDirectory(folder);
                foreach (var stale in new[] { "checks.txt", "failure.txt" }) File.Delete(Path.Combine(folder, stale));
                steps = Steps();
                EditorApplication.update += Tick;
            }

            void Tick()
            {
                if (!EditorApplication.isPlaying) { EditorApplication.update -= Tick; return; }
                if (Time.frameCount == lastFrame) return;
                lastFrame = Time.frameCount;
                try
                {
                    if (phase != null) Sample();
                    if (steps.MoveNext()) return;
                    File.WriteAllLines(Path.Combine(folder, "checks.txt"), results);
                    Debug.Log("PASS: storm driver in Play. " + string.Join("; ", results) + " Results: " + folder);
                    Finish();
                }
                catch (Exception e)
                {
                    File.WriteAllLines(Path.Combine(folder, "checks.txt"), results);
                    File.WriteAllText(Path.Combine(folder, "failure.txt"), e.ToString());
                    Debug.LogError("FAIL: storm driver in Play. " + e.Message + " Results: " + folder);
                    Finish();
                }
            }

            void Finish() { Application.targetFrameRate = -1; QualitySettings.vSyncCount = vSync; EditorApplication.update -= Tick; EditorApplication.ExitPlaymode(); }

            IEnumerator Steps()
            {
                Build();
                for (int i = 0; runner.Storm == null || driver.Bound != LightningStormDriver.Source.Designed; i++)
                {
                    if (i > 600) throw new Exception("The bench never spawned a storm the driver could bind");
                    yield return null;
                }
                var first = runner.Storm;
                Require(skin.Designed == first, "driver binds the designed storm the skin shows (Auto -> Designed)");
                Require(!lightning.advanceClock, "driver takes over the lightning clock in Play");

                // The skin runs its own clock, as a game using DesignedStormRunner does.
                bench.enabled = false;
                runner.timelapse = 1; skin.playing = true; skin.playbackTime = 2400;
                foreach (var _ in Hold("skin clock 1x", 25)) yield return null;
                var a = Close();
                Require(a.scaleOne >= .9f * a.scales.Count, $"skin clock 1x: thunder speed reads 1 on {a.scaleOne}/{a.scales.Count} frames");
                Require(Poisson(a), $"skin clock 1x: {a.flashes} flashes for {a.expected:0.0} expected over {a.modelEnd - a.modelStart:0.0} model s");
                Require(a.thunder > 0, $"skin clock 1x: {a.thunder} thunder voices");

                runner.timelapse = 10;
                foreach (var _ in Hold("skin clock 10x", 8)) yield return null;
                var b = Close();
                Require(Math.Abs(b.MedianScale - 10) < 1.5f, $"skin clock 10x: lightning time scale {b.MedianScale:0.00}");
                Require(b.thunder == 0, $"skin clock 10x: no thunder ({b.thunder} voices)");
                results.Add($"INFO skin clock 10x: {b.flashes} flashes shown for {b.expected:0.0} scheduled");

                // The Storm Test Bench owns the clock and turns the skin's own clock off.
                bench.s.time = skin.playbackTime; bench.s.timelapse = 1;
                typeof(StormTestBench).GetField("playing", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bench, true);
                bench.enabled = true;
                foreach (var _ in Hold("bench clock 1x", 25)) yield return null;
                var c = Close();
                Require(!skin.playing, "bench clock: the skin's own clock is off");
                Require(c.scaleOne >= .9f * c.scales.Count, $"bench clock 1x: thunder speed reads 1 on {c.scaleOne}/{c.scales.Count} frames");
                Require(Poisson(c), $"bench clock 1x: {c.flashes} flashes for {c.expected:0.0} expected over {c.modelEnd - c.modelStart:0.0} model s");
                Require(c.thunder > 0, $"bench clock 1x: {c.thunder} thunder voices");

                bench.s.timelapse = 30;
                foreach (var _ in Hold("bench clock 30x", 6)) yield return null;
                var d = Close();
                Require(Math.Abs(d.MedianScale - 30) < 4.5f, $"bench clock 30x: lightning time scale {d.MedianScale:0.0}");
                Require(d.thunder == 0, $"bench clock 30x: no thunder ({d.thunder} voices)");
                results.Add($"INFO bench clock 30x: {d.flashes} flashes shown for {d.expected:0.0} scheduled");

                bench.s.timelapse = 1;
                yield return null;
                float before = skin.playbackTime;
                bench.s.time = before - 600;
                Open("bench seek back 10 min");
                yield return null;
                var seekFrame = phase;
                Require(Math.Abs(lightning.playbackSeconds - skin.playbackTime) < 1e-3 && skin.playbackTime < before - 590,
                    $"seek: lightning follows the skin from {before / 60:0.0} to {skin.playbackTime / 60:0.0} min");
                Require(seekFrame.thunder == 0, "seek: no thunder on the seek frame");
                foreach (var _ in Hold("after seek", 3, reopen: false)) yield return null;
                Close();

                bench.s.seed = 2; bench.Respawn();
                for (int i = 0; runner.Storm == first; i++) { if (i > 600) throw new Exception("The bench never respawned the storm"); yield return null; }
                yield return null;
                Require(skin.Designed == runner.Storm && driver.Bound == LightningStormDriver.Source.Designed, "storm swap: driver rebinds to the new storm");
                foreach (var _ in Hold("after storm swap (seed 2)", 4)) yield return null;
                Close();

                // Late in the storm at a high frame rate, float rounding of playbackTime is a noticeable share of each step
                // (storms last about an hour on the game clock: at 40 min one 240 fps step is ~17 units in the last place).
                var second = runner.Storm;
                bench.s.seed = 3; bench.s.time = 2400; bench.Respawn();
                for (int i = 0; runner.Storm == second; i++) { if (i > 600) throw new Exception("The bench never respawned the storm"); yield return null; }
                Require(runner.Storm.lifeS > 2500, $"seed 3 lives {runner.Storm.lifeS / 60:0} min, past 40 min");
                Application.targetFrameRate = 240;
                yield return null;
                float realStart = Time.realtimeSinceStartup; int frameStart = Time.frameCount;
                foreach (var _ in Hold("bench clock 1x at 40 min, 240 fps", 25)) yield return null;
                float fps = (Time.frameCount - frameStart) / (Time.realtimeSinceStartup - realStart);
                var e = Close();
                Require(fps > 180, $"late storm ran at {fps:0} fps");
                Require(e.scaleOne >= .98f * e.scales.Count, $"late storm 1x at {fps:0} fps: thunder speed reads 1 on {e.scaleOne}/{e.scales.Count} frames");
                Require(e.thunder > 0, $"late storm 1x: {e.thunder} thunder voices");

                Require(heard.Count > 0, $"thunder voices start playing ({heard.Count} heard, delays {string.Join(", ", thunderDelays.Take(10).Select(x => x.ToString("0.0")))} s)");
                if (EditorUtility.audioMasterMute) results.Add("INFO the Game view's audio is muted, so the thunder played silently");
            }

            void Build()
            {
                // An empty scene runs at hundreds of fps in the editor; a game's frame rate is what the clock checks need.
                vSync = QualitySettings.vSyncCount; QualitySettings.vSyncCount = 0; Application.targetFrameRate = 60;
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                var cam = camGo.AddComponent<Camera>(); camGo.AddComponent<AudioListener>();
                cam.nearClipPlane = 1f; cam.farClipPlane = 250000f; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black;
                camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
                var sunGo = new GameObject("Sun");
                var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional; sun.intensity = 1.2f;
                sunGo.transform.rotation = Quaternion.LookRotation(-StormSkin.VolToWorldDir(StormSkin.DefaultSunVol));
                RenderSettings.sun = sun;

                var go = new GameObject("Storm");
                skin = go.AddComponent<StormSkin>(); skin.sun = sun;
                runner = go.AddComponent<DesignedStormRunner>(); runner.skin = skin; runner.spawnOnStart = false;
                runner.enableLightning = false; // This fixture tests its separate clock driver without cloud anchoring.
                bench = go.AddComponent<StormTestBench>();
                // Off before Start, so the user's saved bench session is neither loaded nor overwritten.
                bench.remember = false; bench.cam = cam;
                bench.s.preset = "plains"; bench.s.region = ClimoRegion.Plains; bench.s.seed = 1; bench.s.time = 2400; bench.s.timelapse = 1;
                bench.s.tier = SkinTier.Low; bench.s.nearField = false;

                ear = new GameObject("Thunder ear").transform;
                var lightningGo = new GameObject("Lightning");
                lightning = lightningGo.AddComponent<LightningSystem>();
                lightning.observer = ear; lightning.groundY = 0; lightning.thunder = true;
                lightning.CellCentreAtTime = t => runner.Storm == null ? Vector3.zero : World(runner.Storm.UpdraftAt(t));
                driver = lightningGo.AddComponent<LightningStormDriver>();
                driver.lightning = lightning; driver.skin = skin;
                driverObject = new SerializedObject(driver);
                lightning.FlashStarted += _ => { if (phase != null) phase.flashes++; };
            }

            static Vector3 World(Vec2 km) => new Vector3((float)km.x * 1000f, 0, (float)km.y * 1000f);

            IEnumerable Hold(string name, float realSeconds, bool reopen = true)
            {
                if (reopen || phase == null) Open(name); else phase.name = name;
                float end = Time.realtimeSinceStartup + realSeconds;
                while (Time.realtimeSinceStartup < end) yield return null;
            }

            void Open(string name) { phase = new Phase { name = name, modelStart = skin.playbackTime }; lastModel = float.NaN; }

            Phase Close()
            {
                var p = phase; phase = null;
                p.modelEnd = skin.playbackTime;
                Require(p.clockOk == p.frames, $"{p.name}: lightning clock equals the skin's on {p.clockOk}/{p.frames} frames");
                Require(p.activityOk == p.frames, $"{p.name}: activity matches the designed storm on {p.activityOk}/{p.frames} frames");
                Require(p.readoutOk == p.frames, $"{p.name}: Inspector readout current on {p.readoutOk}/{p.frames} frames, last \"{p.readout}\"");
                return p;
            }

            void Sample()
            {
                var p = phase; var storm = runner.Storm;
                if (storm == null) return;
                float now = Mathf.Max(0, skin.playbackTime);
                ear.position = World(storm.UpdraftAt(now)) + new Vector3(0, 2, -2500);
                p.frames++;
                if (Math.Abs(lightning.playbackSeconds - now) < 1e-3) p.clockOk++;
                var want = DesignedLightning.At(storm, now, driver.tuning);
                var got = driver.Current;
                if (got.HasValue && got.Value.flashesPerMinute == want.flashesPerMinute && got.Value.groundFraction == want.groundFraction
                    && got.Value.positiveGroundFraction == want.positiveGroundFraction) p.activityOk++;
                driverObject.Update();
                p.readout = driverObject.FindProperty("activityNow").stringValue;
                if (p.readout == $"{driver.Bound}: {want} at {now / 60:0.0} min") p.readoutOk++;
                // The first frame of a phase carries the previous phase's speed.
                if (!float.IsNaN(lastModel))
                {
                    p.scales.Add(lightning.timeScale);
                    if (lightning.timeScale == 1) p.scaleOne++;
                    float step = now - lastModel;
                    if (step > 0) p.expected += want.flashesPerMinute / 60.0 * step;
                }
                lastModel = now;
                foreach (var source in lightning.GetComponentsInChildren<AudioSource>())
                {
                    if (voices.Add(source)) { p.thunder++; voiceBorn[source] = Time.realtimeSinceStartup; }
                    if (source.isPlaying && source.timeSamples > 0 && heard.Add(source)) thunderDelays.Add(Time.realtimeSinceStartup - voiceBorn[source]);
                }
            }

            static bool Poisson(Phase p) => Math.Abs(p.flashes - p.expected) <= 4 * Math.Sqrt(p.expected) + 2;

            void Require(bool condition, string message)
            { if (!condition) throw new Exception(message); results.Add("PASS " + message); }
        }
    }
}
