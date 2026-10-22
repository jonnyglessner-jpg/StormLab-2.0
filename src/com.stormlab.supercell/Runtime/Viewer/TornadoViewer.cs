// Tornado Viewer: a play-mode tool for looking at designed tornadoes. Pick a storm (preset, region, seed) and look at its
// own tornadoes, or force one (shape, rating, width, skin, soil); scrub it through its life; orbit a camera round it in
// any light. Build a scene for it with Supercell Lab > Build Tornado Viewer Scene, then press Play.
//
//   right-drag  orbit          scroll  zoom            F  frame the tornado
//   space       play its life  H       hide the panel
//
// The viewer only changes the storm it spawned: forcing a tornado reshapes that storm's cycle the way the render tests do
// (DesignedSkinTests), and moves its later cycles out of the way.
using System;
using System.Linq;
using StormLab.Supercell.Rendering;
using UnityEngine;
using UnityEngine.InputSystem;

namespace StormLab.Supercell
{
    [RequireComponent(typeof(DesignedStormRunner))]
    public class TornadoViewer : MonoBehaviour
    {
        [Tooltip("the camera to drive (Camera.main if empty)")]
        public Camera cam;
        public bool showPanel = true;

        [Header("Storm")]
        public string preset = "plains";
        public ClimoRegion region = ClimoRegion.Plains;
        public int seed = 1;

        [Header("Tornado")]
        [Tooltip("off: the storm's own tornadoes as its genome drew them; on: reshape one to the settings below")]
        public bool force = true;
        public TornadoShape shape = TornadoShape.Cone;
        [Range(0, 5)] public int ef = 2;
        [Tooltip("damage-path width, m")]
        public float widthM = 350f;
        [Tooltip("let the forced tornado change shape over its life (the genome's phases)")]
        public bool lifePhases;
        public bool satellite;
        [Tooltip("forced tornado's life, minutes")]
        public float lifeMin = 25f;
        [Range(0f, 1f)] public float life = 0.4f;
        [Tooltip("tornado-life playback speed, storm seconds per real second")]
        public float playSpeed = 20f;

        [Header("View")]
        [Tooltip("compass bearing of the camera from the tornado, degrees (140: south-east, in the inflow)")]
        public float yaw = 140f;
        [Tooltip("camera elevation seen from the tornado's base, degrees")]
        public float pitch = 2f;
        public float distance = 3000f;
        [Range(15f, 100f)] public float fov = 55f;
        [Range(0f, 360f)] public float sunAzimuth = 272f;
        [Range(1f, 70f)] public float sunElevation = 15f;

        static readonly string[] SkinNames = { "Auto", "Ragged", "Laminar", "Ghost" };
        static readonly string[] SoilNames = { "Auto", "Red", "Tan", "Pale tan", "Dark", "Red clay" };
        static readonly (double r, double g, double b)[] SoilRgb = { (0, 0, 0), (0.62, 0.35, 0.23), (0.56, 0.47, 0.36), (0.68, 0.6, 0.47), (0.36, 0.3, 0.25), (0.56, 0.31, 0.21) };
        static readonly string[] EfNames = { "EF0", "EF1", "EF2", "EF3", "EF4", "EF5" };
        static readonly string[] ShapeNames = Enum.GetNames(typeof(TornadoShape));
        static readonly string[] ViewNames = { "Inflow", "Sunlit", "Side-lit", "Backlit" };

        public int skinIx, soilIx;
        DesignedStormRunner runner; StormSkin skin;
        DesignedStorm storm; CycleGene cyc; int tornadoIx; int foundSeed = -1;
        string status = "";
        bool playing, dirtyStorm = true, dirtyTornado = true;
        Vector3 target; bool haveTarget;
        Vector2 scroll; Rect panel = new Rect(10, 10, 340, 10);
        Vector3 lastMouse;

        void Start()
        {
            runner = GetComponent<DesignedStormRunner>();
            runner.spawnOnStart = false;
            skin = GetComponent<StormSkin>();
            if (!cam) cam = Camera.main;
            if (cam) { cam.nearClipPlane = 1f; cam.farClipPlane = 250000f; }
        }

        /// <summary>Re-apply the tornado settings (shape, rating, width, skin, soil, phases, satellite) to the storm's tornado.</summary>
        public void MarkTornadoDirty() => dirtyTornado = true;
        /// <summary>Spawn the storm again (preset, region, seed, or switching between forced and own tornadoes).</summary>
        public void MarkStormDirty() => dirtyStorm = true;

        // settings changed in the Inspector while playing: storm settings respawn the storm, the rest reshape the tornado
        (string, ClimoRegion, int, bool) stormKey;
        void OnValidate()
        {
            if (!Application.isPlaying) return;
            var k = (preset, region, seed, force);
            Debug.Log($"[Viewer] DBG OnValidate {k} vs {stormKey}");
            if (k != stormKey) dirtyStorm = true; else dirtyTornado = true;
        }

        // ------------------------------------------------------------------ the storm and its tornado
        void SpawnStorm()
        {
            dirtyStorm = false; dirtyTornado = true; tornadoIx = 0; haveTarget = false;
            stormKey = (preset, region, seed, force);
            var pr = Array.Find(StormPreset.All, p => p.key == preset) ?? StormPreset.All[0];
            preset = pr.key;
            var src = new FixedEnvironment(Environment.Make(pr.ToEnv()));
            // the first storm from this seed on that puts down a tornado lasting at least 4 min
            foundSeed = -1;
            for (int s = Math.Max(seed, 1); s < seed + 400 && foundSeed < 0; s++)
            {
                var probe = new DesignedStorm(src, new Vec2(0, 0), s, region);
                if (probe.cycles.Any(c => c.tornado != null && c.tornado.lifeS > 240)) foundSeed = s;
            }
            if (foundSeed < 0) { storm = null; cyc = null; status = $"No tornadic storm within 400 seeds of {seed} for '{preset}'. Try another preset (lp, hail, capped and pulse rarely make one)."; return; }
            Debug.Log($"[Viewer] storm: {preset}, {region}, seed {foundSeed} (asked {seed}), {(force ? "forced tornado" : "its own tornadoes")}");
            storm = runner.Spawn(src, Vector2.zero, foundSeed, region);
            skin = runner.skin;
            skin.playing = false;
            skin.sunSource = SunSource.Fixed;
            status = "";
        }

        void ApplyTornado()
        {
            dirtyTornado = false;
            if (storm == null) return;
            var list = storm.cycles.Where(c => c.tornado != null && c.tornado.lifeS > 0).ToArray();
            if (list.Length == 0) return;
            tornadoIx = Mathf.Clamp(tornadoIx, 0, list.Length - 1);
            cyc = list[tornadoIx];
            if (!force) return;
            var tg = cyc.tornado;
            // a fresh shape each time (Reshape is idempotent: it rebuilds the funnel from the width and rating)
            tg.ef = ef; tg.widthM = Mathf.Clamp(widthM, 20f, 3000f);
            tg.vmax = TornadoClimatology.SampleVmax(ef, 0.5);
            tg.rcKm = C.Clamp(0.5 * tg.widthM / 1000 * 29.0 / tg.vmax, 0.012, 0.6);
            tg.lifeS = Mathf.Clamp(lifeMin, 2f, 90f) * 60;
            var ch = storm.CharacterAt(tg.formS);
            StormGenome.Reshape(tg, shape, ch, foundSeed, region);
            if (!lifePhases)
            {
                tg.phases = new[] { new TornadoPhase { shape = shape, groundKm = tg.groundKm, topKm = tg.topKm, profile = tg.profile, sinuosity = tg.sinuosity, cond = 1,
                    sideKm = shape == TornadoShape.Sidewinder ? C.Clamp(0.55 * ch.zb, 0.2, 1.0) : 0 } };
            }
            switch (skinIx)
            {
                case 1: tg.skin = FunnelSkin.Ragged; tg.roughness = 1.1; tg.striation = 0.15; tg.ghost = 0; break;
                case 2: tg.skin = FunnelSkin.Laminar; tg.roughness = 0.25; tg.striation = 0.8; tg.ghost = 0; break;
                case 3: tg.skin = FunnelSkin.Ghost; tg.roughness = 1.1; tg.striation = 0.1; tg.ghost = 0.7; break;
            }
            if (soilIx > 0) { var s = SoilRgb[soilIx]; tg.soilR = s.r; tg.soilG = s.g; tg.soilB = s.b; tg.soilName = SoilNames[soilIx].ToLowerInvariant(); }
            tg.satellite = satellite && tg.lifeS > 240;
            if (tg.satellite)
            {
                tg.satFrac = 0.3; tg.satLifeS = Math.Min(240, 0.4 * tg.lifeS);
                tg.satOrbitKm = Math.Max(tg.groundKm * 2.5, 0.25) + 0.5; tg.satRadKm = 0.022; tg.satVmax = 45; tg.satAngle = 1.0;
            }
            cyc.occludeS = tg.formS + tg.lifeS * (1 - 0.6 * tg.ropeFrac);
            cyc.endS = tg.EndS + 300;
            foreach (var c in storm.cycles.Where(c => c.index > cyc.index && c.wallS < 9000))
            { c.wallS += 9000; c.occludeS += 9000; c.endS += 9000; if (c.tornado != null) c.tornado.formS += 9000; }
            skin.ResetVolume();
        }

        float TornadoTime => cyc == null ? 0f : (float)(cyc.tornado.formS + life * cyc.tornado.lifeS);

        // ------------------------------------------------------------------ per frame
        static Vector3 MousePosition => Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
        void Update()
        {
            if (!runner) return;
            if (dirtyStorm) SpawnStorm();
            if (dirtyTornado) ApplyTornado();
            if ((Keyboard.current?.hKey.wasPressedThisFrame ?? false)) showPanel = !showPanel;
            if ((Keyboard.current?.spaceKey.wasPressedThisFrame ?? false)) playing = !playing;
            if ((Keyboard.current?.fKey.wasPressedThisFrame ?? false)) haveTarget = false;
            if (cyc == null || skin == null) return;

            if (playing)
            {
                life += Time.deltaTime * playSpeed / (float)cyc.tornado.lifeS;
                if (life >= 1f) { life = 1f; playing = false; }
            }
            skin.playing = false;
            skin.playbackTime = TornadoTime;
            skin.sunSource = SunSource.Fixed;
            skin.sunAzimuth = sunAzimuth; skin.sunElevation = sunElevation;
            if (skin.sun) skin.sun.transform.rotation = Quaternion.LookRotation(-StormSkin.VolToWorldDir(StormSkin.SunVol(sunAzimuth, sunElevation)));

            // orbit
            bool overPanel = showPanel && panel.Contains(new Vector2(MousePosition.x, Screen.height - MousePosition.y));
            if ((Mouse.current?.rightButton.isPressed ?? false) && !overPanel)
            {
                var d = MousePosition - lastMouse;
                yaw = Mathf.Repeat(yaw + d.x * 0.25f, 360f);
                pitch = Mathf.Clamp(pitch - d.y * 0.15f, -2f, 75f);
            }
            lastMouse = MousePosition;
            float wheel = overPanel ? 0f : (Mouse.current?.scroll.ReadValue().y / 120f ?? 0f);
            if (wheel != 0f) distance = Mathf.Clamp(distance * Mathf.Exp(-wheel * 0.12f), 150f, 30000f);

            // follow the tornado (or, before it forms and after it lifts, where it is going to be or was)
            var tor = runner.Tornado;
            if (tor.on) { target = runner.TornadoGroundWorld; haveTarget = true; }
            else if (!haveTarget) { target = runner.UpdraftWorld; target.y = skin.groundHeight; haveTarget = true; }
            if (!haveTarget) return;
            var zb = (float)storm.CharacterAt(TornadoTime).zb * 1000f;
            var dir = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            var pos = target + dir * distance * Mathf.Cos(pitch * Mathf.Deg2Rad) + Vector3.up * distance * Mathf.Sin(pitch * Mathf.Deg2Rad);
            pos.y = Mathf.Max(pos.y, skin.groundHeight + 2f);
            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(target + Vector3.up * zb * 0.42f - pos, Vector3.up);
            cam.fieldOfView = fov;
        }

        // ------------------------------------------------------------------ the panel
        void OnGUI()
        {
            if (!showPanel) { GUI.Label(new Rect(10, 10, 300, 22), "H: show the Tornado Viewer panel"); return; }
            panel.height = Mathf.Min(Screen.height - 20, 900);
            GUILayout.BeginArea(panel, GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("<b>Tornado Viewer</b>", Rich());

            // storm
            GUILayout.Label("Storm", Bold());
            var keys = StormPreset.All.Select(p => p.key).ToArray();
            int pi = Array.IndexOf(keys, preset);
            int npi = GUILayout.SelectionGrid(Mathf.Max(pi, 0), keys, 4);
            if (npi != pi) { Debug.Log($"[Viewer] DBG panel preset {pi}->{npi} event {Event.current.type}"); preset = keys[npi]; dirtyStorm = true; }
            var regions = Enum.GetNames(typeof(ClimoRegion));
            int nr = GUILayout.Toolbar((int)region, regions);
            if (nr != (int)region) { region = (ClimoRegion)nr; dirtyStorm = true; }
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Seed {seed}" + (foundSeed > 0 && foundSeed != seed ? $" (first tornadic: {foundSeed})" : ""), GUILayout.Width(190));
            if (GUILayout.Button("-")) { seed = Mathf.Max(1, (foundSeed > 0 ? foundSeed : seed) - 1); dirtyStorm = true; }
            if (GUILayout.Button("+")) { seed = (foundSeed > 0 ? foundSeed : seed) + 1; dirtyStorm = true; }
            if (GUILayout.Button("Random")) { seed = UnityEngine.Random.Range(1, 100000); dirtyStorm = true; }
            GUILayout.EndHorizontal();

            // tornado
            GUILayout.Space(6);
            GUILayout.Label("Tornado", Bold());
            bool nf = GUILayout.Toggle(force, " Force a tornado (off: the storm's own)");
            if (nf != force) { force = nf; dirtyStorm = true; }
            if (force)
            {
                int ns = GUILayout.SelectionGrid((int)shape, ShapeNames, 3);
                if (ns != (int)shape) { shape = (TornadoShape)ns; dirtyTornado = true; }
                int ne = GUILayout.Toolbar(ef, EfNames);
                if (ne != ef) { ef = ne; dirtyTornado = true; }
                GUILayout.Label($"Damage width {widthM:0} m ({widthM / 0.9144f:0} yd)");
                float nw = Mathf.Round(Mathf.Exp(GUILayout.HorizontalSlider(Mathf.Log(widthM), Mathf.Log(20f), Mathf.Log(3000f))));
                if (Mathf.Abs(nw - widthM) >= 1f) { widthM = nw; dirtyTornado = true; }
                GUILayout.Label("Skin");
                int nsk = GUILayout.Toolbar(skinIx, SkinNames);
                if (nsk != skinIx) { skinIx = nsk; dirtyTornado = true; }
                GUILayout.Label("Soil");
                int nso = GUILayout.SelectionGrid(soilIx, SoilNames, 3);
                if (nso != soilIx) { soilIx = nso; dirtyTornado = true; }
                bool nlp = GUILayout.Toggle(lifePhases, " Changes shape over its life");
                if (nlp != lifePhases) { lifePhases = nlp; dirtyTornado = true; }
                bool nsat = GUILayout.Toggle(satellite, " Satellite tornado (from 30 % of its life)");
                if (nsat != satellite) { satellite = nsat; dirtyTornado = true; }
            }
            else
            {
                var list = storm == null ? Array.Empty<CycleGene>() : storm.cycles.Where(c => c.tornado != null).ToArray();
                GUILayout.BeginHorizontal();
                GUILayout.Label(list.Length == 0 ? "no tornadoes" : $"Tornado {tornadoIx + 1} of {list.Length}", GUILayout.Width(190));
                if (GUILayout.Button("<") && list.Length > 0) { tornadoIx = (tornadoIx + list.Length - 1) % list.Length; dirtyTornado = true; }
                if (GUILayout.Button(">") && list.Length > 0) { tornadoIx = (tornadoIx + 1) % list.Length; dirtyTornado = true; }
                GUILayout.EndHorizontal();
            }

            // life
            GUILayout.Space(6);
            GUILayout.Label($"Life {life * 100:0} %" + (cyc != null ? $"  ({life * cyc.tornado.lifeS / 60:0.0} of {cyc.tornado.lifeS / 60:0} min)" : ""), Bold());
            life = GUILayout.HorizontalSlider(life, 0f, 1f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(playing ? "Pause (space)" : "Play (space)")) { playing = !playing; if (playing && life >= 0.999f) life = 0f; }
            GUILayout.Label($"x{playSpeed:0}", GUILayout.Width(40));
            playSpeed = GUILayout.HorizontalSlider(playSpeed, 1f, 120f, GUILayout.Width(110));
            GUILayout.EndHorizontal();

            // view
            GUILayout.Space(6);
            GUILayout.Label("View", Bold());
            int v = GUILayout.Toolbar(-1, ViewNames);
            if (v == 0) yaw = 140f;
            else if (v == 1) yaw = sunAzimuth;
            else if (v == 2) yaw = sunAzimuth + 70f;
            else if (v == 3) yaw = sunAzimuth + 180f;
            if (v >= 0) yaw = Mathf.Repeat(yaw, 360f);
            GUILayout.Label($"Distance {distance / 1000f:0.0} km   bearing {yaw:0}   elevation {pitch:0}");
            distance = Mathf.Exp(GUILayout.HorizontalSlider(Mathf.Log(distance), Mathf.Log(150f), Mathf.Log(30000f)));
            GUILayout.Label($"Field of view {fov:0}");
            fov = GUILayout.HorizontalSlider(fov, 15f, 100f);
            GUILayout.Label($"Sun bearing {sunAzimuth:0}, height {sunElevation:0}");
            sunAzimuth = GUILayout.HorizontalSlider(sunAzimuth, 0f, 360f);
            sunElevation = GUILayout.HorizontalSlider(sunElevation, 1f, 70f);

            // what it is
            GUILayout.Space(6);
            if (status != "") GUILayout.Label(status, Wrap());
            if (storm != null && cyc != null)
            {
                var tg = cyc.tornado; var tor = runner.Tornado;
                GUILayout.Label($"<b>{(skin ? skin.Params.stage : "")}</b>", Rich());
                GUILayout.Label(skin ? skin.Params.caption : "", Wrap());
                string phases = tg.phases == null ? tg.shape.ToString() : string.Join(" > ", tg.phases.Select(p => p.shape.ToString()));
                GUILayout.Label($"EF{tg.ef}, {tg.widthM:0} m path, peak {tg.vmax / C.MPH:0} mph. Life: {phases}.\n"
                    + $"Funnel now {(tor.on ? $"{tor.groundKm * 1000:0} m at the ground, {tor.topKm * 1000:0} m at the top" : "not on the ground")}.\n"
                    + $"{tg.skin} skin, brightness {tg.albedo:0.00}, {tg.soilName} soil, lean {tg.leanKm * 1000:0} m" + (tg.satellite ? ", satellite" : "") + $". Seed {foundSeed}, {preset}, {region}.", Wrap());
            }
            GUILayout.Label("Right-drag orbit, scroll zoom, F frame, space play, H hide", Wrap());
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        static GUIStyle Rich() => new GUIStyle(GUI.skin.label) { richText = true, fontSize = 15 };
        static GUIStyle Bold() => new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        static GUIStyle Wrap() => new GUIStyle(GUI.skin.label) { wordWrap = true };
    }
}
