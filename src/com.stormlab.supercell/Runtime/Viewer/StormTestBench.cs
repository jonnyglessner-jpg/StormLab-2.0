// Storm Test Bench: a play-mode tool for testing changes to the whole designed storm, not only its tornado. Pick a storm
// (preset, region, seed), scrub or play its whole life on a timeline that marks its cycles and tornadoes, and look at it
// from a chaser's eye on the ground, an orbit, or free flight, in any light and at any quality tier. The bench remembers
// where you were (storm, time, camera, sun) between play sessions, so after a code change you press Play and see the
// same storm from the same spot. Build its scene with Supercell Lab > Open Test Bench (Ctrl+Shift+T).
// Game scenario mode (G) plays a whole game instead: 2-4 storms on the 30 x 30 mile map on one hour's clock
// (StormScenario, DesignedScenarioRunner); the camera follows one storm at a time (Tab: next storm).
//
//   space  play / pause       , .  step 30 s back / on      T  next tornado    W  next wall cloud
//   1 2 3  chase / orbit / fly   right-drag  look / orbit    scroll  distance (fly: speed)
//   WASD QE  fly                 F12  screenshot             H  hide the panel
//   G  game scenario on / off    Tab  next storm (scenario)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using StormLab.Supercell.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StormLab.Supercell
{
    public enum BenchCamera { Chase, Orbit, Fly }

    [RequireComponent(typeof(DesignedStormRunner))]
    public class StormTestBench : MonoBehaviour
    {
        const string PrefsKey = "StormLab.TestBench";

        [Tooltip("the camera to drive (Camera.main if empty)")]
        public Camera cam;
        public bool showPanel = true;
        [Tooltip("restore the last session's storm, time, camera and sun on Play")]
        public bool remember = true;

        // everything the bench restores between sessions
        [Serializable]
        public class State
        {
            public string preset = "plains";
            public DesignedPrecipitation precipitation = DesignedPrecipitation.Auto;
            public ClimoRegion region = ClimoRegion.Plains;
            public int seed = 1;
            public float time = 1800f;
            public float timelapse = 30f;
            public SkinTier tier = SkinTier.Average;
            public float renderScale = 0.5f;
            public bool nearField = true;
            public BenchCamera mode = BenchCamera.Chase;
            public bool followTornado = true;
            [Tooltip("compass bearing of the camera from the storm, degrees (140: south-east, in the inflow)")]
            public float bearing = 140f;
            public float distance = 12000f;
            public float elevation = 8f;
            public float orbitTargetHeight;
            public float eyeHeight = 2f;
            public float lookUp = 6f;
            public float fov = 60f;
            public Vector3 flyPos = new Vector3(0, 300, -15000);
            public Vector2 flyAngles = new Vector2(-4, 0);
            public bool timeOfDay;
            public float solarHour = 18.8f;
            public float sunAzimuth = 272f;
            public float sunElevation = 15f;
            public float exposure = 1.7f;
            public bool showRain = true;
            [Tooltip("play a whole game scenario (2-4 storms) instead of one storm")]
            public bool scenario;
            [Tooltip("the scenario storm the camera follows")]
            public int focusStorm;
        }
        public State s = new State();

        DesignedStormRunner runner; StormSkin skin;
        DesignedStorm storm;
        DesignedScenarioRunner game;
        /// <summary>Set by the lightning integration: a storm's lightning activity at its time t, for the readout.</summary>
        public static Func<DesignedStorm, double, string> LightningReadout;
        bool playing, dirtyStorm = true;
        string status = "";
        Vector2 scroll; Rect panel = new Rect(10, 10, 360, 10);
        Vector3 lastMouse; float flySpeed = 200f;
        float fpsSmooth = 60f;
        string shotNote = ""; float shotNoteUntil;

        void Start()
        {
            runner = GetComponent<DesignedStormRunner>();
            runner.spawnOnStart = false;
            if (!cam) cam = Camera.main;
            if (cam)
            {
                cam.nearClipPlane = 1f; cam.farClipPlane = 250000f;
                if (!FindAnyObjectByType<AudioListener>()) cam.gameObject.AddComponent<AudioListener>();
            }
            if (remember) Load();
        }

        void OnApplicationQuit() { if (remember) Save(); }
        void OnDisable() { if (remember && Application.isPlaying) Save(); }

        void Load()
        {
            var json = PlayerPrefs.GetString(PrefsKey, "");
            if (json == "") return;
            try { JsonUtility.FromJsonOverwrite(json, s); } catch (Exception e) { Debug.LogWarning($"[Bench] could not restore the last session: {e.Message}"); }
        }
        void Save() { PlayerPrefs.SetString(PrefsKey, JsonUtility.ToJson(s)); PlayerPrefs.Save(); }

        /// <summary>Spawn the storm again from the current settings (keeps the storm time).</summary>
        public void Respawn() => dirtyStorm = true;

        /// <summary>A repeatable whole-storm view for comparing presets and precipitation modes: a mature storm (25 min on
        /// the game clock looks like the old 45-min storm).</summary>
        public void StructureComparison()
        {
            playing = false;
            s.time = 25 * 60;
            s.mode = BenchCamera.Orbit; s.followTornado = false;
            s.bearing = 140; s.distance = 55000; s.elevation = 8; s.fov = 60;
            s.orbitTargetHeight = 6000;
            s.timeOfDay = false; s.sunAzimuth = 225; s.sunElevation = 35; s.exposure = 1.7f;
            s.showRain = true;
        }

        public static ClimoRegion PresetRegion(string preset) => preset == "dixie" ? ClimoRegion.Dixie
            : preset == "ohio" ? ClimoRegion.Midwest : ClimoRegion.Plains;

        // ------------------------------------------------------------------ the storm
        void SpawnStorm()
        {
            dirtyStorm = false;
            if (s.scenario) { SpawnScenario(); return; }
            if (game) { Destroy(game.gameObject); game = null; }
            // the single storm's skin, lightning and the rest switch back on (scenario mode turned them off)
            foreach (var b in GetComponents<Behaviour>()) if (b != this) b.enabled = true;
            var pr = StormPreset.Resolve(s.preset);
            s.preset = pr.key;
            skin = runner.skin ? runner.skin : GetComponent<StormSkin>();
            if (skin)
            {
                // tier and the near field are read when the skin binds a storm, so drop the old one first
                skin.Release();
                skin.tier = s.tier; skin.nearField = s.nearField;
            }
            float keep = s.time;
            var src = new FixedEnvironment(Environment.Make(pr.ToEnv()));
            runner.precipitation = s.precipitation;
            storm = runner.Spawn(src, Vector2.zero, Math.Max(1, s.seed), s.region);
            skin = runner.skin;
            s.time = Mathf.Clamp(keep, 0f, (float)storm.lifeS);
            skin.playbackTime = s.time;
            skin.ResetVolume();
            int tors = storm.cycles.Count(c => c.tornado != null && c.tornado.lifeS > 0);
            status = $"{pr.name}: {storm.kind}, lives {storm.lifeS / 60:0} min, {storm.cycles.Count} cycles, {tors} tornado{(tors == 1 ? "" : "es")}.";
            Debug.Log($"[Bench] {s.preset}, {s.region}, seed {s.seed}, tier {s.tier}: {status}");
        }

        void SpawnScenario()
        {
            var pr = StormPreset.Resolve(s.preset);
            s.preset = pr.key;
            skin = runner.skin ? runner.skin : GetComponent<StormSkin>();
            if (skin) skin.Release();
            // the bench's own storm stands down: its skin, lightning and the rest (the runner only answers queries)
            foreach (var b in GetComponents<Behaviour>()) if (b != this && !(b is DesignedStormRunner)) b.enabled = false;
            storm = null;
            if (!game)
            {
                var go = new GameObject("Game scenario");
                go.transform.SetParent(transform, false);
                game = go.AddComponent<DesignedScenarioRunner>();
                game.spawnOnStart = false;
            }
            game.tier = s.tier; game.nearField = s.nearField; game.precipitation = s.precipitation; game.lookFrom = skin;
            game.playing = false;
            var sc = game.Spawn(new FixedEnvironment(Environment.Make(pr.ToEnv())), Math.Max(1, s.seed), s.region);
            s.time = Mathf.Clamp(s.time, 0f, (float)StormScenario.GameS);
            s.focusStorm = Mathf.Clamp(s.focusStorm, 0, sc.storms.Count - 1);
            status = $"{pr.name} game, seed {s.seed}: {sc}.";
            Debug.Log($"[Bench] scenario {s.preset}, {s.region}, seed {s.seed}: {sc}");
        }

        bool Scenario => s.scenario && game && game.Scenario != null;
        /// <summary>the storm the camera follows, when it initiates (game s), and its own time now</summary>
        DesignedStorm FocusStorm => Scenario ? game.Scenario.storms[s.focusStorm].storm : storm;
        double FocusStart => Scenario ? game.Scenario.storms[s.focusStorm].startS : 0;
        float FocusTime => FocusStorm == null ? 0f : Mathf.Clamp(s.time - (float)FocusStart, 0f, (float)FocusStorm.lifeS);
        /// <summary>the length of the timeline: the storm's life, or the game</summary>
        float Life => Scenario ? (float)StormScenario.GameS : storm == null ? 1f : (float)storm.lifeS;

        /// <summary>every tornado and wall cloud, in game seconds, with the storm it belongs to</summary>
        IEnumerable<(int storm, double formS, TornadoGene t)> Tornadoes()
        {
            if (Scenario)
            {
                foreach (var st in game.Scenario.storms)
                foreach (var t in st.Tornadoes) yield return (st.index, st.startS + t.formS, t);
            }
            else if (storm != null)
                foreach (var c in storm.cycles) if (c.tornado != null && c.tornado.lifeS > 0) yield return (0, c.tornado.formS, c.tornado);
        }
        IEnumerable<(int storm, double wallS)> WallClouds()
        {
            if (Scenario) { foreach (var st in game.Scenario.storms) foreach (var c in st.storm.cycles) yield return (st.index, st.startS + c.wallS); }
            else if (storm != null) foreach (var c in storm.cycles) yield return (0, c.wallS);
        }

        void Seek(float t)
        {
            if (storm == null && !Scenario) return;
            s.time = Mathf.Clamp(t, 0f, Life);
            if (skin && !Scenario) skin.playbackTime = s.time;
        }

        void NextTornado()
        {
            var all = Tornadoes().OrderBy(x => x.formS).ToList();
            if (all.Count == 0) { Flash("This storm puts down no tornado. Try another seed."); return; }
            var next = all.FirstOrDefault(x => x.formS > s.time + 1);
            if (next.t == null) next = all[0];
            s.focusStorm = next.storm;
            Seek((float)next.formS + 30f);
        }

        void NextWallCloud()
        {
            var all = WallClouds().OrderBy(x => x.wallS).ToList();
            if (all.Count == 0) return;
            var next = all.FirstOrDefault(x => x.wallS > s.time + 1);
            if (next.wallS <= s.time + 1) next = all[0];
            s.focusStorm = next.storm;
            Seek((float)next.wallS);
        }

        void NextStorm() { if (Scenario) s.focusStorm = (s.focusStorm + 1) % game.Scenario.storms.Count; }

        void Flash(string msg) { shotNote = msg; shotNoteUntil = Time.unscaledTime + 4f; }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!runner) return;
            if (dirtyStorm) SpawnStorm();
            if ((storm == null && !Scenario) || !skin) return;
            fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            bool overPanel = showPanel && panel.Contains(new Vector2(MousePosition.x, Screen.height - MousePosition.y));
            Keys();
            if (dirtyStorm) return;

            // time: the bench owns playback, the skin (or the game's skins) only show it
            if (playing)
            {
                s.time += Time.deltaTime * s.timelapse;
                if (s.time >= Life) { s.time = Life; playing = false; }
            }
            skin.playing = false;
            if (Scenario) { game.playing = false; game.gameTime = s.time; game.Apply(); }
            else skin.playbackTime = s.time;
            skin.renderScale = s.renderScale;
            skin.exposure = s.exposure;
            skin.debugFlags = s.showRain ? skin.debugFlags & ~16 : skin.debugFlags | 16;

            // sun
            if (s.timeOfDay)
            {
                var a = StormSkin.SolarPosition(s.solarHour, skin.latitude, skin.dayOfYear);
                s.sunAzimuth = a.x; s.sunElevation = Mathf.Max(a.y, 0.5f);
            }
            skin.sunSource = SunSource.Fixed;
            skin.sunAzimuth = s.sunAzimuth; skin.sunElevation = s.sunElevation;
            if (skin.sun) skin.sun.transform.rotation = Quaternion.LookRotation(-StormSkin.VolToWorldDir(StormSkin.SunVol(s.sunAzimuth, s.sunElevation)));

            DriveCamera(overPanel);
        }

        void Keys()
        {
            if (GUIUtility.keyboardControl != 0) return;
            // editor shortcuts (Ctrl+Shift+T leaves Play) reach the game too
            if ((Keyboard.current?.leftCtrlKey.isPressed ?? false) || (Keyboard.current?.rightCtrlKey.isPressed ?? false) || (Keyboard.current?.leftAltKey.isPressed ?? false) || (Keyboard.current?.rightAltKey.isPressed ?? false)) return;
            if ((Keyboard.current?.hKey.wasPressedThisFrame ?? false)) showPanel = !showPanel;
            if ((Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)) { playing = !playing; if (playing && s.time >= Life - 1) s.time = 0; }
            if ((Keyboard.current?.gKey.wasPressedThisFrame ?? false)) { s.scenario = !s.scenario; dirtyStorm = true; }
            if ((Keyboard.current?.tabKey.wasPressedThisFrame ?? false)) NextStorm();
            if ((Keyboard.current?.commaKey.wasPressedThisFrame ?? false)) Seek(s.time - 30f);
            if ((Keyboard.current?.periodKey.wasPressedThisFrame ?? false)) Seek(s.time + 30f);
            if ((Keyboard.current?.tKey.wasPressedThisFrame ?? false)) NextTornado();
            if (s.mode != BenchCamera.Fly && (Keyboard.current?.wKey.wasPressedThisFrame ?? false)) NextWallCloud();
            if ((Keyboard.current?.digit1Key.wasPressedThisFrame ?? false)) s.mode = BenchCamera.Chase;
            if ((Keyboard.current?.digit2Key.wasPressedThisFrame ?? false)) s.mode = BenchCamera.Orbit;
            if ((Keyboard.current?.digit3Key.wasPressedThisFrame ?? false)) EnterFly();
            if ((Keyboard.current?.f12Key.wasPressedThisFrame ?? false)) Screenshot();
        }

        Vector3 Focus()
        {
            Vector3 p;
            if (Scenario)
            {
                var tor = game.Tornado(s.focusStorm);
                p = s.followTornado && tor.on ? game.TornadoGroundWorld(s.focusStorm) : game.UpdraftWorld(s.focusStorm);
            }
            else
            {
                var tor = runner.Tornado;
                p = s.followTornado && tor.on ? runner.TornadoGroundWorld : runner.UpdraftWorld;
            }
            p.y = skin.groundHeight;
            return p;
        }

        void EnterFly()
        {
            if (s.mode != BenchCamera.Fly && cam)
            {
                s.flyPos = cam.transform.position;
                var e = cam.transform.rotation.eulerAngles;
                s.flyAngles = new Vector2(Mathf.DeltaAngle(0, e.x), e.y);
            }
            s.mode = BenchCamera.Fly;
        }

        static Vector3 MousePosition => Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
        void DriveCamera(bool overPanel)
        {
            if (!cam) return;
            var md = MousePosition - lastMouse; lastMouse = MousePosition;
            bool drag = (Mouse.current?.rightButton.isPressed ?? false) && !overPanel;
            float wheel = overPanel ? 0f : (Mouse.current?.scroll.ReadValue().y / 120f ?? 0f);
            float zb = (float)FocusStorm.CharacterAt(FocusTime).zb * 1000f;
            var focus = Focus();

            if (s.mode == BenchCamera.Fly)
            {
                if (drag) { s.flyAngles.y += md.x * 0.2f; s.flyAngles.x = Mathf.Clamp(s.flyAngles.x - md.y * 0.2f, -89f, 89f); }
                if (wheel != 0f) flySpeed = Mathf.Clamp(flySpeed * Mathf.Exp(wheel * 0.15f), 10f, 5000f);
                var rot = Quaternion.Euler(s.flyAngles.x, s.flyAngles.y, 0f);
                var v = Vector3.zero;
                if (GUIUtility.keyboardControl == 0)
                {
                    if ((Keyboard.current?.wKey.isPressed ?? false)) v += Vector3.forward;
                    if ((Keyboard.current?.sKey.isPressed ?? false)) v += Vector3.back;
                    if ((Keyboard.current?.aKey.isPressed ?? false)) v += Vector3.left;
                    if ((Keyboard.current?.dKey.isPressed ?? false)) v += Vector3.right;
                    if ((Keyboard.current?.eKey.isPressed ?? false)) v += Vector3.up;
                    if ((Keyboard.current?.qKey.isPressed ?? false)) v += Vector3.down;
                }
                float sp = flySpeed * ((Keyboard.current?.leftShiftKey.isPressed ?? false) ? 5f : 1f);
                s.flyPos += rot * v * sp * Time.unscaledDeltaTime;
                s.flyPos.y = Mathf.Max(s.flyPos.y, skin.groundHeight + 1.5f);
                cam.transform.SetPositionAndRotation(s.flyPos, rot);
            }
            else
            {
                if (drag)
                {
                    s.bearing = Mathf.Repeat(s.bearing + md.x * 0.25f, 360f);
                    if (s.mode == BenchCamera.Orbit) s.elevation = Mathf.Clamp(s.elevation - md.y * 0.15f, 0f, 85f);
                    else s.lookUp = Mathf.Clamp(s.lookUp - md.y * 0.1f, -5f, 60f);
                }
                if (wheel != 0f) s.distance = Mathf.Clamp(s.distance * Mathf.Exp(-wheel * 0.12f), 100f, 80000f);
                var dir = new Vector3(Mathf.Sin(s.bearing * Mathf.Deg2Rad), 0f, Mathf.Cos(s.bearing * Mathf.Deg2Rad));
                Vector3 pos; Quaternion look;
                if (s.mode == BenchCamera.Chase)
                {
                    // a chaser standing on the ground: eye height above the ground, looking at the storm and tilted up
                    pos = focus + dir * s.distance; pos.y = skin.groundHeight + s.eyeHeight;
                    var flat = focus - pos; flat.y = 0;
                    look = Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(-s.lookUp, 0, 0);
                }
                else
                {
                    pos = focus + dir * s.distance * Mathf.Cos(s.elevation * Mathf.Deg2Rad) + Vector3.up * s.distance * Mathf.Sin(s.elevation * Mathf.Deg2Rad);
                    pos.y = Mathf.Max(pos.y, skin.groundHeight + 2f);
                    float aimHeight = s.orbitTargetHeight > 0 ? s.orbitTargetHeight : zb * 0.6f;
                    look = Quaternion.LookRotation(focus + Vector3.up * aimHeight - pos, Vector3.up);
                }
                cam.transform.SetPositionAndRotation(pos, look);
            }
            cam.fieldOfView = s.fov;
        }

        /// <summary>Writes a screenshot of the Game view to a path. The package does not depend on Unity's screen-capture
        /// module, so the host project sets this (the test project's StormTestBenchScene does).</summary>
        public static Action<string> Capture;

        public void Screenshot()
        {
            if (Capture == null) { Flash("No screenshot handler: set StormTestBench.Capture"); return; }
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Screenshots"));
            Directory.CreateDirectory(dir);
            string game = Scenario ? $"game_storm{s.focusStorm + 1}_" : "";
            string file = Path.Combine(dir, $"bench_{game}{s.preset}_{s.precipitation}_{s.region}_s{s.seed}_t{s.time / 60:000.0}min_{DateTime.Now:HHmmss}.png");
            Capture(file);
            Flash($"Saved {Path.GetFileName(file)}");
            Debug.Log($"[Bench] screenshot {file}");
        }

        // ------------------------------------------------------------------ the panel
        void OnGUI()
        {
            if (shotNote != "" && Time.unscaledTime < shotNoteUntil)
                GUI.Label(new Rect(Screen.width - 420, 10, 410, 24), shotNote, Bold());
            if (!showPanel) { GUI.Label(new Rect(10, 10, 300, 22), "H: show the Storm Test Bench panel"); return; }
            panel.height = Mathf.Min(Screen.height - 20, 1100);
            GUILayout.BeginArea(panel, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<b>Designed Storm Comparison</b>", Rich());
            bool sc = GUILayout.Toggle(s.scenario, " Game scenario: 2-4 storms on the 30 x 30 mile map, one hour (G)");
            if (sc != s.scenario) { s.scenario = sc; dirtyStorm = true; }

            // storm
            GUILayout.Label("Environment preset", Bold());
            var keys = StormPreset.All.Select(p => p.key).ToArray();
            int pi = Array.IndexOf(keys, s.preset);
            int npi = GUILayout.SelectionGrid(Mathf.Max(pi, 0), keys, 4);
            if (npi != pi) { s.preset = keys[npi]; s.region = PresetRegion(s.preset); dirtyStorm = true; }
            GUILayout.Label("Precipitation mode", Bold());
            int npm = GUILayout.Toolbar((int)s.precipitation, new[] { "Auto", "Classic", "LP", "HP" });
            if (npm != (int)s.precipitation) { s.precipitation = (DesignedPrecipitation)npm; dirtyStorm = true; }
            GUILayout.Label(s.precipitation == DesignedPrecipitation.Auto
                ? "Mode follows the environment. LP uses the former hail environment."
                : "Authored mode override; sounding and storm motion are retained.", Wrap());
            if (GUILayout.Button("25-minute (mature) structure comparison")) StructureComparison();
            s.showRain = GUILayout.Toggle(s.showRain, " Show designed rain, hail shafts and haze");
            GUILayout.Label("Tornado climatology region", Bold());
            int nr = GUILayout.Toolbar((int)s.region, Enum.GetNames(typeof(ClimoRegion)));
            if (nr != (int)s.region) { s.region = (ClimoRegion)nr; dirtyStorm = true; }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Seed {s.seed}", GUILayout.Width(110));
            if (GUILayout.Button("-")) { s.seed = Mathf.Max(1, s.seed - 1); dirtyStorm = true; }
            if (GUILayout.Button("+")) { s.seed++; dirtyStorm = true; }
            if (GUILayout.Button("Random")) { s.seed = UnityEngine.Random.Range(1, 100000); dirtyStorm = true; }
            if (GUILayout.Button("Respawn")) dirtyStorm = true;
            GUILayout.EndHorizontal();
            if (status != "") GUILayout.Label(status, Wrap());

            // time
            GUILayout.Space(6);
            float life = Life;
            GUILayout.Label(Scenario ? $"Game time {Clock(s.time)} of {Clock(life)}" : $"Time {Clock(s.time)} of {Clock(life)}", Bold());
            Timeline(life);
            if (Scenario)
            {
                var names = game.Scenario.storms.Select(x => $"Storm {x.index + 1}").ToArray();
                int nf = GUILayout.Toolbar(s.focusStorm, names);
                if (nf != s.focusStorm) s.focusStorm = nf;
            }
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(playing ? "Pause" : "Play")) { playing = !playing; if (playing && s.time >= life - 1) s.time = 0; }
            if (GUILayout.Button("-30 s")) Seek(s.time - 30f);
            if (GUILayout.Button("+30 s")) Seek(s.time + 30f);
            if (GUILayout.Button("Wall cloud")) NextWallCloud();
            if (GUILayout.Button("Tornado")) NextTornado();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Speed x{s.timelapse:0}", GUILayout.Width(90));
            s.timelapse = Mathf.Round(Mathf.Exp(GUILayout.HorizontalSlider(Mathf.Log(s.timelapse), 0f, Mathf.Log(600f))));
            GUILayout.EndHorizontal();

            // camera
            GUILayout.Space(6);
            GUILayout.Label("Camera", Bold());
            int nm = GUILayout.Toolbar((int)s.mode, new[] { "1 Chase", "2 Orbit", "3 Fly" });
            if (nm != (int)s.mode) { if (nm == 2) EnterFly(); else s.mode = (BenchCamera)nm; }
            if (s.mode != BenchCamera.Fly)
            {
                s.followTornado = GUILayout.Toggle(s.followTornado, " Centre on the tornado while it is down");
                GUILayout.Label($"Distance {s.distance / 1000f:0.0} km ({s.distance / 1609.34f:0.0} mi), bearing {s.bearing:0}");
                s.distance = Mathf.Exp(GUILayout.HorizontalSlider(Mathf.Log(s.distance), Mathf.Log(100f), Mathf.Log(80000f)));
                s.bearing = GUILayout.HorizontalSlider(s.bearing, 0f, 360f);
                if (s.mode == BenchCamera.Chase)
                {
                    GUILayout.Label($"Look up {s.lookUp:0}°, eye {s.eyeHeight:0} m");
                    s.lookUp = GUILayout.HorizontalSlider(s.lookUp, -5f, 60f);
                    s.eyeHeight = GUILayout.HorizontalSlider(s.eyeHeight, 1.5f, 300f);
                }
                else
                {
                    GUILayout.Label($"Elevation {s.elevation:0}°");
                    s.elevation = GUILayout.HorizontalSlider(s.elevation, 0f, 85f);
                    GUILayout.Label($"Orbit target height {s.orbitTargetHeight / 1000:0.0} km (0: cloud base)");
                    s.orbitTargetHeight = GUILayout.HorizontalSlider(s.orbitTargetHeight, 0f, 12000f);
                }
            }
            else GUILayout.Label($"WASD move, Q/E down/up, Shift fast, right-drag look, scroll speed ({flySpeed:0} m/s)", Wrap());
            GUILayout.Label($"Field of view {s.fov:0}°");
            s.fov = GUILayout.HorizontalSlider(s.fov, 15f, 100f);

            // light
            GUILayout.Space(6);
            GUILayout.Label("Light", Bold());
            s.timeOfDay = GUILayout.Toggle(s.timeOfDay, " Sun from the time of day (Dodge City, late May)");
            if (s.timeOfDay)
            {
                GUILayout.Label($"Solar time {Mathf.FloorToInt(s.solarHour):00}:{Mathf.FloorToInt(s.solarHour % 1 * 60):00}  (sun {s.sunAzimuth:0}°, {s.sunElevation:0}° up)");
                s.solarHour = GUILayout.HorizontalSlider(s.solarHour, 6f, 19.4f);
            }
            else
            {
                GUILayout.Label($"Sun bearing {s.sunAzimuth:0}°, height {s.sunElevation:0}°");
                s.sunAzimuth = GUILayout.HorizontalSlider(s.sunAzimuth, 0f, 360f);
                s.sunElevation = GUILayout.HorizontalSlider(s.sunElevation, 1f, 70f);
            }
            GUILayout.Label($"Exposure {s.exposure:0.00}");
            s.exposure = GUILayout.HorizontalSlider(s.exposure, 0.5f, 4f);

            // quality
            GUILayout.Space(6);
            GUILayout.Label("Quality", Bold());
            int nt = GUILayout.Toolbar(s.tier == SkinTier.Low ? 0 : s.tier == SkinTier.Average ? 1 : 2, new[] { "Low", "Average", "High" });
            var tier = nt == 0 ? SkinTier.Low : nt == 1 ? SkinTier.Average : SkinTier.High;
            if (tier != s.tier) { s.tier = tier; dirtyStorm = true; }
            GUILayout.Label($"Render scale {s.renderScale:0.00}");
            s.renderScale = GUILayout.HorizontalSlider(s.renderScale, 0.25f, 1f);
            bool nn = GUILayout.Toggle(s.nearField, " Near-field volume (wall cloud detail)");
            if (nn != s.nearField) { s.nearField = nn; dirtyStorm = true; }

            // what it is doing
            GUILayout.Space(6);
            Readout();

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Screenshot (F12)")) Screenshot();
            if (GUILayout.Button("Reset bench")) { s = new State(); playing = false; dirtyStorm = true; }
            GUILayout.EndHorizontal();
            GUILayout.Label("Space play, , . step, T tornado, W wall cloud, 1-3 camera, G game scenario, Tab next storm, H hide", Wrap());
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // the storm's life as a bar: grey cycles (wall cloud to end), red tornadoes, white playhead; click or drag to seek
        void Timeline(float life)
        {
            int rows = Scenario ? game.Scenario.storms.Count : 1;
            var r = GUILayoutUtility.GetRect(300, 26 * rows, GUILayout.ExpandWidth(true));
            Fill(r, new Color(0.12f, 0.12f, 0.14f));
            float X(double t) => r.x + (float)(Math.Max(t, 0) / life) * r.width;
            void Row(DesignedStorm st, double start, float y, bool focus)
            {
                // the storm's life (dim), its cycles (grey) and tornadoes (EF colour), shifted to game time
                if (Scenario) Fill(new Rect(X(start), y + 1, Mathf.Max(2f, X(Math.Min(start + st.lifeS, life)) - X(start)), 24), focus ? new Color(0.22f, 0.24f, 0.3f) : new Color(0.17f, 0.17f, 0.2f));
                foreach (var c in st.cycles)
                {
                    if (start + c.wallS >= life) continue;
                    Fill(new Rect(X(start + c.wallS), y + 3, Mathf.Max(2f, X(Math.Min(start + c.endS, life)) - X(start + c.wallS)), 8), new Color(0.55f, 0.6f, 0.7f));
                    var t = c.tornado;
                    if (t != null && t.lifeS > 0 && start + t.formS < life)
                        Fill(new Rect(X(start + t.formS), y + 13, Mathf.Max(2f, X(Math.Min(start + t.formS + t.lifeS, life)) - X(start + t.formS)), 10), EfColour(t.ef));
                }
            }
            if (Scenario) foreach (var st in game.Scenario.storms) Row(st.storm, st.startS, r.y + 26 * st.index, st.index == s.focusStorm);
            else if (storm != null) Row(storm, 0, r.y, true);
            Fill(new Rect(X(s.time) - 1, r.y, 2, r.height), Color.white);
            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition))
            {
                Seek((e.mousePosition.x - r.x) / r.width * life);
                e.Use();
            }
            GUILayout.Label(Scenario ? "<size=10>one row per storm from its start; grey: cycles (wall cloud), colour: tornadoes by EF</size>"
                : "<size=10>grey: rotation cycles (wall cloud)   colour: tornadoes by EF</size>", Rich());
        }

        void Readout()
        {
            var st = FocusStorm;
            if (st == null) return;
            double ft = FocusTime;
            var ch = st.CharacterAt(ft);
            TornadoNow tor;
            if (Scenario)
            {
                int i = s.focusStorm; var fr = game.Runners[i];
                tor = game.Tornado(i);
                double own = game.StormTime(i);
                string when = own < 0 ? $"initiates at {Clock((float)FocusStart)} (in {Clock((float)-own)})" : own > st.lifeS ? "has dissipated" : $"{Clock((float)own)} old";
                GUILayout.Label($"<b>Storm {i + 1}</b> ({when}): {(game.Alive(i) ? fr.Stage : "")}", RichWrap());
                if (game.Alive(i)) GUILayout.Label(fr.Caption, Wrap());
                // the whole game at a glance
                foreach (var x in game.Scenario.storms)
                {
                    var tx = game.Tornado(x.index);
                    string what = !game.Alive(x.index) ? (game.StormTime(x.index) < 0 ? $"starts {Clock((float)x.startS)}" : "gone")
                        : tx.on ? $"EF{tx.ef} {tx.phaseShape} on the ground" : game.Runners[x.index].Stage;
                    string tors = string.Join(" ", x.Tornadoes.Select(t => $"EF{t.ef}"));
                    GUILayout.Label($"<size=11>{x.index + 1}: {what}. Tornadoes {tors}</size>", RichWrap());
                }
            }
            else
            {
                tor = runner.Tornado;
                GUILayout.Label($"<b>{runner.Stage}</b>", Rich());
                GUILayout.Label(runner.Caption, Wrap());
            }
            if (LightningReadout != null) GUILayout.Label($"Lightning: {LightningReadout(st, ft)}", Wrap());
            double mspd = Math.Sqrt(ch.motion.x * ch.motion.x + ch.motion.y * ch.motion.y);
            double mdir = (Math.Atan2(ch.motion.x, ch.motion.y) * Mathf.Rad2Deg + 360) % 360;
            GUILayout.Label($"{ch.kind}, {(ch.LP ? "LP" : ch.HP ? "HP" : "classic")} ({ch.hpness:+0.00;-0.00}). Base {ch.zb * 1000 / C.FT:0} ft, top {ch.el:0.0} km, updraft {ch.w:0} m/s.\n"
                + $"CAPE {ch.cape:0}, CIN {ch.cin:0}, 0-6 km shear {ch.shear6 / C.KT:0} kt, 0-1 km SRH {ch.srh1:0}, STP {ch.stp:0.0}.\n"
                + $"Moving toward {mdir:0}° at {mspd / C.MPH:0} mph.", Wrap());
            if (tor.on)
            {
                var camPos = cam ? cam.transform.position : Vector3.zero;
                var w = (Scenario ? game.TornadoWindWorld(camPos) : runner.TornadoWindWorld(camPos)).magnitude;
                GUILayout.Label($"<b>Tornado on the ground</b>: EF{tor.ef} {tor.phaseShape}, {tor.groundKm * 1000:0} m wide at the ground, {tor.lifeFrac * 100:0} % through its life. Wind at the camera {w / C.MPH:0} mph.", RichWrap());
            }
            else GUILayout.Label("No tornado on the ground.", Wrap());
            var cp = cam ? cam.transform.position : Vector3.zero;
            var upW = Scenario ? game.UpdraftWorld(s.focusStorm) : runner.UpdraftWorld;
            float dist = (new Vector2(cp.x, cp.z) - new Vector2(upW.x, upW.z)).magnitude;
            GUILayout.Label($"Camera {dist / 1000:0.0} km from the updraft, {cp.y - skin.groundHeight:0} m up.  {fpsSmooth:0} fps", Wrap());
        }

        static string Clock(float t) => $"{Mathf.FloorToInt(t / 3600)}:{Mathf.FloorToInt(t / 60 % 60):00}:{Mathf.FloorToInt(t % 60):00}";
        static Color EfColour(int ef) => ef switch
        {
            0 => new Color(0.4f, 0.8f, 0.4f), 1 => new Color(0.9f, 0.9f, 0.3f), 2 => new Color(1f, 0.65f, 0.2f),
            3 => new Color(1f, 0.3f, 0.2f), 4 => new Color(0.9f, 0.2f, 0.6f), _ => new Color(0.7f, 0.3f, 1f),
        };
        static void Fill(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = o; }
        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        static GUIStyle RichWrap() => new GUIStyle(GUI.skin.label) { richText = true, wordWrap = true };
        static GUIStyle Bold() => new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        static GUIStyle Wrap() => new GUIStyle(GUI.skin.label) { wordWrap = true };
    }
}
