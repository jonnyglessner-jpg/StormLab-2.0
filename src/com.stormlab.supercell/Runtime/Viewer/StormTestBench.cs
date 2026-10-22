// Storm Test Bench: a play-mode tool for testing changes to the whole designed storm, not only its tornado. Pick a storm
// (preset, region, seed), scrub or play its whole life on a timeline that marks its cycles and tornadoes, and look at it
// from a chaser's eye on the ground, an orbit, or free flight, in any light and at any quality tier. The bench remembers
// where you were (storm, time, camera, sun) between play sessions, so after a code change you press Play and see the
// same storm from the same spot. Build its scene with Supercell Lab > Open Test Bench (Ctrl+Shift+T).
//
//   space  play / pause       , .  step 30 s back / on      T  next tornado    W  next wall cloud
//   1 2 3  chase / orbit / fly   right-drag  look / orbit    scroll  distance (fly: speed)
//   WASD QE  fly                 F12  screenshot             H  hide the panel
using System;
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
        }
        public State s = new State();

        DesignedStormRunner runner; StormSkin skin;
        DesignedStorm storm;
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
            if (cam) { cam.nearClipPlane = 1f; cam.farClipPlane = 250000f; }
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

        // ------------------------------------------------------------------ the storm
        void SpawnStorm()
        {
            dirtyStorm = false;
            var pr = Array.Find(StormPreset.All, p => p.key == s.preset) ?? StormPreset.All[0];
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
            storm = runner.Spawn(src, Vector2.zero, Math.Max(1, s.seed), s.region);
            skin = runner.skin;
            s.time = Mathf.Clamp(keep, 0f, (float)storm.lifeS);
            skin.playbackTime = s.time;
            skin.ResetVolume();
            int tors = storm.cycles.Count(c => c.tornado != null && c.tornado.lifeS > 0);
            status = $"{pr.name}: {storm.kind}, lives {storm.lifeS / 60:0} min, {storm.cycles.Count} cycles, {tors} tornado{(tors == 1 ? "" : "es")}.";
            Debug.Log($"[Bench] {s.preset}, {s.region}, seed {s.seed}, tier {s.tier}: {status}");
        }

        TornadoGene[] Tornadoes => storm == null ? Array.Empty<TornadoGene>()
            : storm.cycles.Where(c => c.tornado != null && c.tornado.lifeS > 0).Select(c => c.tornado).ToArray();

        void Seek(float t)
        {
            if (storm == null) return;
            s.time = Mathf.Clamp(t, 0f, (float)storm.lifeS);
            if (skin) skin.playbackTime = s.time;
        }

        void NextTornado()
        {
            var next = Tornadoes.Where(t => t.formS > s.time + 1).OrderBy(t => t.formS).FirstOrDefault()
                    ?? Tornadoes.OrderBy(t => t.formS).FirstOrDefault();
            if (next == null) { Flash("This storm puts down no tornado. Try another seed."); return; }
            Seek((float)next.formS + 30f);
        }

        void NextWallCloud()
        {
            if (storm == null || storm.cycles.Count == 0) return;
            var next = storm.cycles.Where(c => c.wallS > s.time + 1).OrderBy(c => c.wallS).FirstOrDefault() ?? storm.cycles.OrderBy(c => c.wallS).First();
            Seek((float)next.wallS);
        }

        void Flash(string msg) { shotNote = msg; shotNoteUntil = Time.unscaledTime + 4f; }

        // ------------------------------------------------------------------ per frame
        void Update()
        {
            if (!runner) return;
            if (dirtyStorm) SpawnStorm();
            if (storm == null || !skin) return;
            fpsSmooth = Mathf.Lerp(fpsSmooth, 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-4f), 0.05f);
            bool overPanel = showPanel && panel.Contains(new Vector2(MousePosition.x, Screen.height - MousePosition.y));
            Keys();

            // time: the bench owns playback, the skin only shows it
            if (playing)
            {
                s.time += Time.deltaTime * s.timelapse;
                if (s.time >= storm.lifeS) { s.time = (float)storm.lifeS; playing = false; }
            }
            skin.playing = false;
            skin.playbackTime = s.time;
            skin.renderScale = s.renderScale;
            skin.exposure = s.exposure;

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
            if ((Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)) { playing = !playing; if (playing && s.time >= storm.lifeS - 1) s.time = 0; }
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
            var tor = runner.Tornado;
            var p = s.followTornado && tor.on ? runner.TornadoGroundWorld : runner.UpdraftWorld;
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
            float zb = (float)storm.CharacterAt(s.time).zb * 1000f;
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
                    look = Quaternion.LookRotation(focus + Vector3.up * zb * 0.6f - pos, Vector3.up);
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
            string file = Path.Combine(dir, $"bench_{s.preset}_s{s.seed}_t{s.time / 60:000.0}min_{DateTime.Now:HHmmss}.png");
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
            GUILayout.Label("<b>Storm Test Bench</b>", Rich());

            // storm
            GUILayout.Label("Storm", Bold());
            var keys = StormPreset.All.Select(p => p.key).ToArray();
            int pi = Array.IndexOf(keys, s.preset);
            int npi = GUILayout.SelectionGrid(Mathf.Max(pi, 0), keys, 4);
            if (npi != pi) { s.preset = keys[npi]; dirtyStorm = true; }
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
            float life = storm == null ? 1f : (float)storm.lifeS;
            GUILayout.Label($"Time {Clock(s.time)} of {Clock(life)}", Bold());
            Timeline(life);
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
            GUILayout.Label("Space play, , . step, T tornado, W wall cloud, 1-3 camera, H hide", Wrap());
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // the storm's life as a bar: grey cycles (wall cloud to end), red tornadoes, white playhead; click or drag to seek
        void Timeline(float life)
        {
            var r = GUILayoutUtility.GetRect(300, 26, GUILayout.ExpandWidth(true));
            Fill(r, new Color(0.12f, 0.12f, 0.14f));
            float X(double t) => r.x + (float)(t / life) * r.width;
            if (storm != null)
            {
                foreach (var c in storm.cycles)
                {
                    if (c.wallS >= life) continue;
                    Fill(new Rect(X(c.wallS), r.y + 3, Mathf.Max(2f, X(Math.Min(c.endS, life)) - X(c.wallS)), 8), new Color(0.55f, 0.6f, 0.7f));
                    var t = c.tornado;
                    if (t != null && t.lifeS > 0 && t.formS < life)
                        Fill(new Rect(X(t.formS), r.y + 13, Mathf.Max(2f, X(Math.Min(t.formS + t.lifeS, life)) - X(t.formS)), 10), EfColour(t.ef));
                }
            }
            Fill(new Rect(X(s.time) - 1, r.y, 2, r.height), Color.white);
            var e = Event.current;
            if ((e.type == EventType.MouseDown || e.type == EventType.MouseDrag) && e.button == 0 && r.Contains(e.mousePosition))
            {
                Seek((e.mousePosition.x - r.x) / r.width * life);
                e.Use();
            }
            GUILayout.Label("<size=10>grey: rotation cycles (wall cloud)   colour: tornadoes by EF</size>", Rich());
        }

        void Readout()
        {
            if (storm == null) return;
            var ch = storm.CharacterAt(s.time);
            var tor = runner.Tornado;
            GUILayout.Label($"<b>{runner.Stage}</b>", Rich());
            GUILayout.Label(runner.Caption, Wrap());
            double mspd = Math.Sqrt(ch.motion.x * ch.motion.x + ch.motion.y * ch.motion.y);
            double mdir = (Math.Atan2(ch.motion.x, ch.motion.y) * Mathf.Rad2Deg + 360) % 360;
            GUILayout.Label($"{ch.kind}, {(ch.hpness < -0.33 ? "LP" : ch.hpness > 0.33 ? "HP" : "classic")} ({ch.hpness:+0.00;-0.00}). Base {ch.zb * 1000 / C.FT:0} ft, top {ch.el:0.0} km, updraft {ch.w:0} m/s.\n"
                + $"CAPE {ch.cape:0}, CIN {ch.cin:0}, 0-6 km shear {ch.shear6 / C.KT:0} kt, 0-1 km SRH {ch.srh1:0}, STP {ch.stp:0.0}.\n"
                + $"Moving toward {mdir:0}° at {mspd / C.MPH:0} mph.", Wrap());
            if (tor.on)
            {
                var w = runner.TornadoWindWorld(cam ? cam.transform.position : Vector3.zero).magnitude;
                GUILayout.Label($"<b>Tornado on the ground</b>: EF{tor.ef} {tor.phaseShape}, {tor.groundKm * 1000:0} m wide at the ground, {tor.lifeFrac * 100:0} % through its life. Wind at the camera {w / C.MPH:0} mph.", RichWrap());
            }
            else GUILayout.Label("No tornado on the ground.", Wrap());
            var cp = cam ? cam.transform.position : Vector3.zero;
            float dist = (new Vector2(cp.x, cp.z) - new Vector2(runner.UpdraftWorld.x, runner.UpdraftWorld.z)).magnitude;
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
