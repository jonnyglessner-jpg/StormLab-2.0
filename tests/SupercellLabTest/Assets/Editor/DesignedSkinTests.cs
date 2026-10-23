// Designed-storm render test: the skin drawing a DesignedStorm (no brain). Writes
//   Screenshots/designed_tornado_<shape>.png  one ground view per tornado shape, mid-life
//   Screenshots/designed_tornado_sheet.png    all nine, 3 x 3
//   Screenshots/designed_tornado_close.png    six shapes from 1-3 km, the sun to the side: the funnel's surface
//   Screenshots/designed_tornado_variety.png  first tornadoes of unforced storms across presets and regions, as drawn
//   Screenshots/designed_tornado_variety_sunlit.png  the same tornadoes from the sun's side: the light on the funnel
//   Screenshots/designed_tornado_life.png     one tornado that changes shape, through its life
//   Screenshots/designed_timelapse.png        one multi-cycle storm every 10 min from a chaser following it
//   Screenshots/designed_chaser.png           (chaser only) big tornadoes from 300 m and 1 km, with the march's debug view
//   Screenshots/designed_game.png             (game only) the game's HDRP look: sky, exposure, fog, HDRP clouds, ground
// and validation/reports/DesignedSkinReport.md. Batch (never -nographics):
//   Unity.exe -batchmode -projectPath <SupercellLabTest> -executeMethod DesignedSkinTests.RunFromCommandLine -logFile designed.log
//   [-designedOnly sheet|variety|life|timelapse|anatomy|chaser|game|bench]
//   bench: Screenshots/designed_bench.png, the Test Bench's 2-storm game from a chaser: both storms, both without the
//   storm depth write, each alone (parts of one storm vanishing behind the other's depth showed here)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using StormLab.Supercell;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;
using SEnv = StormLab.Supercell.Environment;

public static class DesignedSkinTests
{
    static string ProjectDir => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
    static string ShotDir => Path.Combine(ProjectDir, "Screenshots");
    static string OutDir => Path.GetFullPath(Path.Combine(ProjectDir, "..", "..", "validation", "reports"));

    [MenuItem("Supercell Lab/Run Designed Storm Screenshots")]
    public static void RunMenu() => Run(null);

    public static void RunFromCommandLine()
    {
        bool ok;
        bool renderError = false;
        Application.LogCallback checkError = (message, stack, type) =>
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                renderError = true;
        };
        Application.logMessageReceived += checkError;
        try
        {
            var a = System.Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-designedOnly");
            ok = Run(i >= 0 && i + 1 < a.Length ? a[i + 1] : null);
        }
        catch (Exception e) { Debug.LogException(e); ok = false; }
        finally { Application.logMessageReceived -= checkError; }
        EditorApplication.Exit(ok && !renderError ? 0 : 1);
    }

    static void Sync() { var b = new ComputeBuffer(1, 4); var a = new int[1]; b.GetData(a); b.Release(); }

    static Texture2D Shot(Camera cam, int w, int h, int frames = 12)
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
        rt.Release(); Object.DestroyImmediate(rt);
        return tex;
    }

    static void Save(Texture2D t, string name) => File.WriteAllBytes(Path.Combine(ShotDir, name + ".png"), t.EncodeToPNG());

    static Texture2D Sheet(List<Texture2D> tiles, int cols)
    {
        int w = tiles[0].width, h = tiles[0].height, rows = (tiles.Count + cols - 1) / cols;
        var o = new Texture2D(w * cols, h * rows, TextureFormat.RGB24, false);
        for (int i = 0; i < tiles.Count; i++)
            o.SetPixels(i % cols * w, (rows - 1 - i / cols) * h, w, h, tiles[i].GetPixels());
        o.Apply();
        return o;
    }

    static Vector3 Bearing(float deg) => new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));
    static void Pose(Camera cam, Vector3 from, Vector3 at, float fov) { cam.transform.position = from; cam.transform.rotation = Quaternion.LookRotation(at - from, Vector3.up); cam.fieldOfView = fov; }

    /// <summary>Run the skin up to time T from 90 s before it, so the advected volume has settled.</summary>
    static void Settle(StormSkin skin, float T)
    {
        skin.ResetVolume();
        float t0 = Mathf.Max(0, T - 90f);
        skin.StepTo(t0, t0);
        for (float t = t0 + 2f; t <= T + 1e-3f; t += 2f) skin.StepTo(t, t);
        Sync();
    }

    static Vector3 KmToWorld(StormSkin skin, Vec2 p, float zM = 0) =>
        new Vector3((float)(p.x - skin.DesignedOriginKm.x) * 1000f + skin.groundOrigin.x, zM, (float)(p.y - skin.DesignedOriginKm.y) * 1000f + skin.groundOrigin.y);

    static string Life(TornadoGene tg) => (tg.phases == null ? tg.shape.ToString() : string.Join(" > ", tg.phases.Select(p => p.shape.ToString())))
        + (tg.satellite ? $", satellite at {tg.satFrac:0.00}" : "");
    static string Look(TornadoGene tg) => $"{tg.skin}, brightness {tg.albedo:0.00}, {tg.soilName} soil, dirt {tg.dirt:0.00}, lean {tg.leanKm * 1000:0} m";

    /// <summary>
    /// Shoot cycle cyc's tornado at frac of its life from a chaser 3-6 km south-east of it, its later cycles moved out of the
    /// way. frame: the tornado whose size sets the framing (default: itself at the shot time); the chaser follows it.
    /// bearing: where the chaser stands, compass degrees from the tornado (140: south-east, in the inflow).
    /// </summary>
    static Texture2D ShootTornado(StormSkin skin, Camera cam, DesignedStorm st, CycleGene cyc, double frac, int w, int h, out TornadoNow tor, TornadoNow? frame = null, float bearing = 140f)
    {
        var tg = cyc.tornado;
        foreach (var c in st.cycles.Where(c => c.index > cyc.index && c.wallS < 9000)) { c.wallS += 9000; c.occludeS += 9000; c.endS += 9000; if (c.tornado != null) c.tornado.formS += 9000; }
        float T = (float)(tg.formS + frac * tg.lifeS);
        Settle(skin, T);
        tor = st.TornadoAt(T);
        var fr = frame ?? tor;
        var g = KmToWorld(skin, tor.ground);
        double zb = st.CharacterAt(T).zb;
        float D = Mathf.Clamp((float)(Math.Max(tg.groundKm, fr.groundKm) * 9000), 3000f, 6000f) + (tg.satellite ? 1500f : 0f);
        Pose(cam, g + Bearing(bearing) * D + Vector3.up * 2f, g + Vector3.up * (float)(zb * 450), 2f * Mathf.Atan((float)(zb * 1000 * 1.25) / D) * Mathf.Rad2Deg);
        skin.ResetHistory(cam);
        Debug.Log($"[Designed] shot at {frac:0.00} of life (cycle {cyc.index}, T {T:0} s): {skin.Params.stage}; on {tor.on} cycle {tor.cycle} ground km {tor.ground.x:0.00},{tor.ground.y:0.00} frame km {fr.ground.x:0.00},{fr.ground.y:0.00} updraft km {st.UpdraftAt(T).x:0.00},{st.UpdraftAt(T).y:0.00} world {g} cam {cam.transform.position} origin {skin.DesignedOriginKm}; radius {tor.groundKm:0.000}/{tor.topKm:0.000} side {tor.sideKm:0.00} sat {tor.satAmount:0.00}");
        return Shot(cam, w, h);
    }

    public static bool Run(string only)
    {
        if (!File.Exists(Path.Combine(ProjectDir, SkinTests.ScenePath))) SkinTests.BuildScene();
        EditorSceneManager.OpenScene(SkinTests.ScenePath, OpenSceneMode.Single);
        ShaderUtil.allowAsyncCompilation = false;
        Directory.CreateDirectory(ShotDir); Directory.CreateDirectory(OutDir);
        var skin = Object.FindAnyObjectByType<StormSkin>();
        var brain = Object.FindAnyObjectByType<StormLab.Supercell.Dynamics.SupercellRunner>();
        if (brain) brain.enabled = false;
        skin.runner = null;
        var runner = skin.gameObject.AddComponent<DesignedStormRunner>();
        runner.spawnOnStart = false; runner.skin = skin;
        var cam = Camera.main;
        skin.Activate();
        var rep = new StringBuilder();
        rep.AppendLine("# Designed storm render test").AppendLine();
        rep.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}, {SystemInfo.graphicsDeviceName}, Unity {Application.unityVersion}.").AppendLine();
        bool ok = true;

        var plains = SEnv.Make(StormPreset.All.First(p => p.key == "plains").ToEnv());
        // a High Plains dryline day for the dust whirl (high base)
        var dryline = SEnv.Make(new StormPreset { T0F = 90, TdF = 63, moistFt = 1500, lapse = 8.5, cap = 0.5, llj = 45, shear = 52, upper = 90 }.ToEnv());

        if (only == null || only == "sheet")
        {
            rep.AppendLine("## Tornado shapes").AppendLine().AppendLine("| Shape | EF | Width | Funnel radius ground / top | Condensation reach | Shot |").AppendLine("| --- | --- | --- | --- | --- | --- |");
            var tiles = new List<Texture2D>(); var close = new List<Texture2D>();
            var sv0 = skin.SunDirVol; float sunAz0 = Mathf.Atan2(sv0.x, sv0.y) * Mathf.Rad2Deg;
            var closeShapes = new[] { TornadoShape.Rope, TornadoShape.Cone, TornadoShape.Stovepipe, TornadoShape.Wedge, TornadoShape.MultiVortex, TornadoShape.ElephantTrunk };
            var widths = new Dictionary<TornadoShape, (double w, int ef)>
            {
                { TornadoShape.Rope, (90, 1) }, { TornadoShape.Cone, (350, 2) }, { TornadoShape.Stovepipe, (450, 3) }, { TornadoShape.Wedge, (1400, 4) },
                { TornadoShape.MultiVortex, (900, 3) }, { TornadoShape.Drillbit, (140, 1) }, { TornadoShape.ElephantTrunk, (220, 2) }, { TornadoShape.DustWhirl, (180, 1) },
                { TornadoShape.Sidewinder, (110, 1) },
            };
            foreach (TornadoShape shape in Enum.GetValues(typeof(TornadoShape)))
            {
                var env = shape == TornadoShape.DustWhirl ? dryline : plains;
                int found = -1;
                for (int seed = 1; seed < 400 && found < 0; seed++)
                    if (new DesignedStorm(new FixedEnvironment(env), new Vec2(0, 0), seed, ClimoRegion.Plains).cycles.Any(c => c.tornado != null)) found = seed;
                if (found < 0) { ok = false; Debug.LogError($"[Designed] no tornadic storm for {shape}"); continue; }
                var st = runner.Spawn(new FixedEnvironment(env), Vector2.zero, found, ClimoRegion.Plains);
                var cyc = st.cycles.First(c => c.tornado != null);
                var tg = cyc.tornado; var (w, ef) = widths[shape];
                tg.ef = ef; tg.widthM = w; tg.vmax = TornadoClimatology.SampleVmax(ef, 0.5); tg.rcKm = C.Clamp(0.5 * w / 1000 * 29.0 / tg.vmax, 0.012, 0.6);
                tg.lifeS = 1500; tg.rainWrap = 0;
                StormGenome.Reshape(tg, shape, st.CharacterAt(tg.formS), 7);
                // the sheet shows each shape itself: no change of shape, satellite or lean
                tg.phases = null; tg.satellite = false; tg.leanKm = 0;
                if (shape == TornadoShape.Sidewinder) tg.phases = new[] { new TornadoPhase { shape = shape, groundKm = tg.groundKm, topKm = tg.topKm, profile = tg.profile, sinuosity = tg.sinuosity, cond = 1, sideKm = 0.6 } };
                cyc.occludeS = tg.formS + tg.lifeS * (1 - 0.6 * tg.ropeFrac); cyc.endS = tg.EndS + 300;
                // later cycles out of the way, so this tornado is the one on the ground
                foreach (var c in st.cycles.Where(c => c.index > cyc.index)) { c.wallS += 5000; c.occludeS += 5000; c.endS += 5000; if (c.tornado != null) c.tornado.formS += 5000; }
                float frac = shape == TornadoShape.MultiVortex ? 0.25f : shape == TornadoShape.Wedge ? 0.55f : 0.4f;
                float T = (float)(tg.formS + frac * tg.lifeS);
                Settle(skin, T);
                var tor = st.TornadoAt(T);
                var g = KmToWorld(skin, tor.ground);
                double zb = st.CharacterAt(T).zb;
                // a chaser 5 km away (closer for small tornadoes), south-east of it, in the inflow
                float D = Mathf.Clamp((float)(tg.groundKm * 9000), 3000f, 6000f);
                Pose(cam, g + Bearing(140f) * D + Vector3.up * 2f, g + Vector3.up * (float)(zb * 450), 2f * Mathf.Atan((float)(zb * 1000 * 1.25) / D) * Mathf.Rad2Deg);
                skin.ResetHistory(cam);
                var Pt = skin.Params;
                Debug.Log($"[Designed] {shape} placement: tornado world {g}, cam {cam.transform.position}, cam in volume frame {skin.WorldToVol(cam.transform.position)}, S_tor {Pt[Slot.Tor]}, S_tor2 {Pt[Slot.Tor2]}, up {Pt[Slot.Up]}, base {Pt[Slot.Base]}, wall2 {Pt[Slot.Wall2]}, centre {skin.Centre}, origin km {skin.DesignedOriginKm}, groundOrigin {skin.groundOrigin}");
                var tex = Shot(cam, 960, 540);
                Save(tex, $"designed_tornado_{shape}");
                tiles.Add(tex);
                if (closeShapes.Contains(shape))
                {
                    // close, with the sun 60 degrees to the viewer's side, so the lit side and the shading across it show
                    float Dc = Mathf.Clamp((float)(tor.topKm * 5000), 900f, 3500f);
                    Pose(cam, g + Bearing(sunAz0 + 60f) * Dc + Vector3.up * 2f, g + Vector3.up * (float)(zb * 420), 2f * Mathf.Atan((float)(zb * 1000 * 1.1) / Dc) * Mathf.Rad2Deg);
                    skin.ResetHistory(cam);
                    close.Add(Shot(cam, 960, 540));
                }
                Debug.Log($"[Designed] {shape}: EF{tor.ef} amount {tor.amount:0.00} ground {tor.groundKm:0.000} top {tor.topKm:0.000} cond {tor.condReach:0.00} multi {tor.multi:0.00} envelope {tor.envelope:0.00} spin {tor.spin:0.00} stage {skin.Params.stage}");
                if (!tor.on) { ok = false; Debug.LogError($"[Designed] {shape}: no tornado at the shot time"); }
                rep.AppendLine($"| {shape} | EF{tor.ef} | {w:0} m | {tor.groundKm * 1000:0} / {tor.topKm * 1000:0} m | {tor.condReach:0.00} | `designed_tornado_{shape}.png` |");
            }
            if (tiles.Count > 0) { Save(Sheet(tiles, 3), "designed_tornado_sheet"); foreach (var t in tiles) Object.DestroyImmediate(t); }
            if (close.Count > 0) { Save(Sheet(close, 3), "designed_tornado_close"); foreach (var t in close) Object.DestroyImmediate(t); }
            rep.AppendLine().AppendLine("Sheet: `designed_tornado_sheet.png`, in the order Rope, Cone, Stovepipe / Wedge, MultiVortex, Drillbit / ElephantTrunk, DustWhirl, Sidewinder.").AppendLine();
        }

        if (only == null || only == "variety")
        {
            // unforced storms: whatever the genome draws, from presets and regions that differ
            rep.AppendLine("## Variety").AppendLine().AppendLine("| Tile | Preset / region | Seed | Tornado | Life | Look | Shot at |").AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            var tiles = new List<Texture2D>(); var lit = new List<Texture2D>();
            // the sun's compass bearing: a chaser standing toward it sees the funnel front-lit
            var sv = skin.SunDirVol; float sunAz = Mathf.Atan2(sv.x, sv.y) * Mathf.Rad2Deg;
            var cases = new (string key, ClimoRegion reg)[] { ("plains", ClimoRegion.Plains), ("plains", ClimoRegion.Plains), ("plains", ClimoRegion.Plains), ("ohio", ClimoRegion.Midwest), ("dixie", ClimoRegion.Dixie), ("dryline", ClimoRegion.Plains) };
            int seed = 10;
            foreach (var (key, reg) in cases)
            {
                var env = key == "dryline" ? dryline : SEnv.Make(StormPreset.All.First(p => p.key == key).ToEnv());
                DesignedStorm st = null; CycleGene cyc = null;
                for (int k = 0; k < 600 && cyc == null; k++, seed++)
                {
                    var probe = new DesignedStorm(new FixedEnvironment(env), new Vec2(0, 0), seed, reg);
                    var c = probe.cycles.FirstOrDefault(x => x.tornado != null && x.tornado.lifeS > 240);
                    if (c != null) { st = runner.Spawn(new FixedEnvironment(env), Vector2.zero, seed, reg); cyc = st.cycles.First(x => x.index == c.index); }
                }
                if (cyc == null) { Debug.LogWarning($"[Designed] variety: no tornadic {key} storm"); continue; }
                var tg = cyc.tornado;
                double frac = tg.satellite ? tg.satFrac + 0.5 * tg.satLifeS / tg.lifeS : 0.45;
                var tex = ShootTornado(skin, cam, st, cyc, frac, 960, 540, out var tor);
                tiles.Add(tex);
                lit.Add(ShootTornado(skin, cam, st, cyc, frac, 960, 540, out _, null, sunAz));
                rep.AppendLine($"| {tiles.Count} | {key} / {reg} | {st.genome.seed} | EF{tg.ef} {tg.shape} {tg.widthM:0} m | {Life(tg)} | {Look(tg)} | {frac:0.00} of its life, {skin.Params.stage} |");
                seed += 17;
            }
            if (tiles.Count > 0) { Save(Sheet(tiles, 3), "designed_tornado_variety"); foreach (var t in tiles) Object.DestroyImmediate(t); }
            if (lit.Count > 0) { Save(Sheet(lit, 3), "designed_tornado_variety_sunlit"); foreach (var t in lit) Object.DestroyImmediate(t); }
            rep.AppendLine().AppendLine($"Sheets: `designed_tornado_variety.png` (chaser south-east, in the inflow, looking into the storm) and `designed_tornado_variety_sunlit.png` (chaser toward the sun, bearing {sunAz:0}), left to right, top to bottom.").AppendLine();
        }

        if (only == null || only == "life")
        {
            // a long-lived tornado that passes through three shapes
            var env = plains; DesignedStorm st = null; CycleGene cyc = null;
            for (int seed = 1; seed < 3000 && cyc == null; seed++)
            {
                var probe = new DesignedStorm(new FixedEnvironment(env), new Vec2(0, 0), seed, ClimoRegion.Plains);
                var c = probe.cycles.FirstOrDefault(x => x.tornado != null && x.tornado.phases != null && x.tornado.phases.Length >= 3 && x.tornado.lifeS > 720);
                if (c != null) { st = runner.Spawn(new FixedEnvironment(env), Vector2.zero, seed, ClimoRegion.Plains); cyc = st.cycles.First(x => x.index == c.index); }
            }
            if (cyc == null) { ok = false; Debug.LogError("[Designed] life: no tornado with three phases"); }
            else
            {
                var tg = cyc.tornado;
                rep.AppendLine("## Life of one tornado").AppendLine().AppendLine($"Seed {st.genome.seed}, EF{tg.ef} {tg.widthM:0} m, {tg.lifeS / 60:0} min: {Life(tg)}; {Look(tg)}.").AppendLine();
                rep.AppendLine("| Tile | Fraction of life | Stage | Funnel radius ground / top |").AppendLine("| --- | --- | --- | --- |");
                var tiles = new List<Texture2D>();
                // the chaser keeps pace with it, framed for its mid-life size, so the change of shape shows against a fixed frame
                double[] fr = { 0.06, 0.2, 0.4, 0.62, 0.78, 0.93 };
                var mid = st.TornadoAt(tg.formS + 0.5 * tg.lifeS);
                foreach (var f in fr)
                {
                    var tex = ShootTornado(skin, cam, st, cyc, f, 640, 360, out var tor, mid);
                    tiles.Add(tex);
                    rep.AppendLine($"| {tiles.Count} | {f:0.00} | {skin.Params.stage} | {tor.groundKm * 1000:0} / {tor.topKm * 1000:0} m |");
                }
                Save(Sheet(tiles, 3), "designed_tornado_life"); foreach (var t in tiles) Object.DestroyImmediate(t);
                rep.AppendLine().AppendLine("Sheet: `designed_tornado_life.png`, left to right, top to bottom.").AppendLine();
            }
        }

        if (only == "anatomy")
        {
            // Fixed seed, camera and clock for reviewing the joins between the funnel, base and RFD.
            // Keep these separate from the variety sheet: its close cameras hide most of the clear slot.
            var st = runner.Spawn(new FixedEnvironment(plains), Vector2.zero, 1, ClimoRegion.Plains);
            var cyc = st.cycles.First(c => c.tornado != null);
            var tg = cyc.tornado;
            tg.ef = 2; tg.widthM = 350; tg.vmax = TornadoClimatology.SampleVmax(2, 0.5);
            tg.rcKm = C.Clamp(0.175 * 29.0 / tg.vmax, 0.012, 0.6);
            tg.lifeS = 1500; tg.rainWrap = 0;
            StormGenome.Reshape(tg, TornadoShape.Cone, st.CharacterAt(tg.formS), 7);
            tg.phases = null; tg.satellite = false; tg.leanKm = 0;
            cyc.occludeS = tg.formS + 1300; cyc.endS = tg.EndS + 300;
            foreach (var c in st.cycles.Where(c => c.index > cyc.index))
            {
                c.wallS += 9000; c.occludeS += 9000; c.endS += 9000;
                if (c.tornado != null) c.tornado.formS += 9000;
            }
            float T = (float)(tg.formS + 600);
            Settle(skin, T);
            var tor = st.TornadoAt(T);
            if (!tor.on) { ok = false; Debug.LogError("[Anatomy] expected a mature tornado"); }
            var g = KmToWorld(skin, tor.ground);
            var u = KmToWorld(skin, st.UpdraftAt(T));
            float zb = (float)st.CharacterAt(T).zb * 1000;
            var sun = skin.SunDirVol;
            float sunAz = Mathf.Atan2(sun.x, sun.y) * Mathf.Rad2Deg;
            var p = skin.Params;
            var rf = p[Slot.Rfdb]; var fwd = p[Slot.F];
            Vector2 rear = new Vector2(Mathf.Cos(2.18f) * fwd.x + Mathf.Sin(2.18f) * fwd.z,
                Mathf.Cos(2.18f) * fwd.y + Mathf.Sin(2.18f) * fwd.w);
            Vector2 slotEye = new Vector2(rf.x, rf.y) + rear * (rf.z + 1.2f);
            var slotAt = skin.VolToWorld(new Vector3(rf.x, rf.y, zb / 1000 + 0.55f));
            var views = new (string name, Vector3 from, Vector3 at, float fov)[]
            {
                ("inflow", g + Bearing(140) * 8000 + Vector3.up * 2, u + Vector3.up * zb, 55),
                ("slot", g + Bearing(250) * 6500 + Vector3.up * 700, u + Vector3.up * (zb + 650), 65),
                ("aerial", u + Bearing(225) * 9500 + Vector3.up * (zb + 5500), u + Vector3.up * zb, 65),
                ("funnel", g + Bearing(sunAz + 60) * 2300 + Vector3.up * 2, g + Vector3.up * (zb * 0.42f), 48),
                ("backlit", g + Bearing(sunAz + 180) * 3200 + Vector3.up * 2, g + Vector3.up * (zb * 0.48f), 48),
                ("sidelit", g + Bearing(sunAz + 100) * 2600 + Vector3.up * 2, g + Vector3.up * (zb * 0.45f), 48),
                ("interior", skin.VolToWorld(new Vector3(slotEye.x, slotEye.y, Mathf.Max(0.1f, zb / 1000 - 0.25f))), slotAt, 75),
            };
            rep.AppendLine("## Anatomy review").AppendLine().AppendLine("Fixed Plains seed 1, mature cone. Inflow, rear clear slot, aerial, funnel with the sun 60 degrees off behind the camera, backlit and side-lit funnels, and a view into the rear opening.").AppendLine();
            var tiles = new List<Texture2D>();
            foreach (var v in views)
            {
                Pose(cam, v.from, v.at, v.fov); skin.ResetHistory(cam);
                var tex = Shot(cam, 1280, 720); Save(tex, "anatomy_" + v.name); tiles.Add(tex);
                Debug.Log("[Anatomy] captured " + v.name);
            }
            // Same camera with the near volume disabled: coarse and fine cloud must join coherently.
            skin.nearField = false;
            Pose(cam, views[0].from, views[0].at, views[0].fov); skin.ResetHistory(cam);
            var coarse = Shot(cam, 1280, 720); Save(coarse, "anatomy_coarse"); tiles.Add(coarse);
            var sheet = Sheet(tiles, 3); Save(sheet, "anatomy_sheet"); Object.DestroyImmediate(sheet);
            foreach (var tex in tiles) Object.DestroyImmediate(tex);
            rep.AppendLine("`anatomy_sheet.png`: inflow / slot / aerial; funnel / backlit / side-lit; interior / coarse-only inflow.");
        }

        if (only == "chaser")
        {
            // A game's chaser view: big tornadoes from a few hundred metres to a kilometre, eye height, a game camera's field
            // of view. Each view is shot as drawn and with the march's debug output (r steps used / maxSteps, g detail steps,
            // b transmittance T), and T is logged on the funnel's axis: a funnel the horizon shows through has T well above 0.
            rep.AppendLine("## Chaser views").AppendLine().AppendLine("| Shape | Width | From | T on the axis at 100 / 300 / 600 m | Steps used (max of the axis samples) |").AppendLine("| --- | --- | --- | --- | --- |");
            var tiles = new List<Texture2D>();
            var chaserShapes = new[] { (TornadoShape.Wedge, 1400.0, 4), (TornadoShape.MultiVortex, 900.0, 3), (TornadoShape.Stovepipe, 450.0, 3) };
            foreach (var (shape, w, ef) in chaserShapes)
            {
                int found = -1;
                for (int seed = 1; seed < 400 && found < 0; seed++)
                    if (new DesignedStorm(new FixedEnvironment(plains), new Vec2(0, 0), seed, ClimoRegion.Plains).cycles.Any(c => c.tornado != null)) found = seed;
                var st = runner.Spawn(new FixedEnvironment(plains), Vector2.zero, found, ClimoRegion.Plains);
                var cyc = st.cycles.First(c => c.tornado != null);
                var tg = cyc.tornado;
                tg.ef = ef; tg.widthM = w; tg.vmax = TornadoClimatology.SampleVmax(ef, 0.5); tg.rcKm = C.Clamp(0.5 * w / 1000 * 29.0 / tg.vmax, 0.012, 0.6);
                tg.lifeS = 1500; tg.rainWrap = 0;
                StormGenome.Reshape(tg, shape, st.CharacterAt(tg.formS), 7);
                tg.phases = null; tg.satellite = false; tg.leanKm = 0; tg.ghost = 0;
                cyc.occludeS = tg.formS + tg.lifeS * (1 - 0.6 * tg.ropeFrac); cyc.endS = tg.EndS + 300;
                foreach (var c in st.cycles.Where(c => c.index > cyc.index)) { c.wallS += 5000; c.occludeS += 5000; c.endS += 5000; if (c.tornado != null) c.tornado.formS += 5000; }
                float T = (float)(tg.formS + 0.5 * tg.lifeS);
                Settle(skin, T);
                var tor = st.TornadoAt(T);
                var g = KmToWorld(skin, tor.ground);
                double zb = st.CharacterAt(T).zb;
                foreach (float gap in new[] { 300f, 1000f })
                {
                    float D = (float)tor.groundKm * 1000f + gap;
                    Pose(cam, g + Bearing(140f) * D + Vector3.up * 2f, g + Vector3.up * (float)(zb * 380), 60f);
                    skin.debugFlags = 0; skin.ResetHistory(cam);
                    var tex = Shot(cam, 960, 540);
                    tiles.Add(tex);
                    // the debug pass: no temporal blend or checkerboard, so each pixel is one ray's own result
                    float blend = skin.temporalBlend; bool cb = skin.checkerboard;
                    skin.debugFlags = 64; skin.temporalBlend = 1f; skin.checkerboard = false; skin.ResetHistory(cam);
                    var rt = new RenderTexture(960, 540, 24, RenderTextureFormat.ARGBHalf, RenderTextureReadWrite.Linear);
                    rt.Create();
                    var req = new RenderPipeline.StandardRequest { destination = rt };
                    for (int f = 0; f < 3; f++) RenderPipeline.SubmitRenderRequest(cam, req);
                    var prev = RenderTexture.active; RenderTexture.active = rt;
                    var dbg = new Texture2D(960, 540, TextureFormat.RGBAHalf, false, true);
                    dbg.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); dbg.Apply();
                    RenderTexture.active = prev; rt.Release(); Object.DestroyImmediate(rt);
                    skin.debugFlags = 0; skin.temporalBlend = blend; skin.checkerboard = cb;
                    var Ts = new List<string>(); float steps = 0f;
                    foreach (float zm in new[] { 100f, 300f, 600f })
                    {
                        var vp = cam.WorldToViewportPoint(g + Vector3.up * zm);
                        var px = dbg.GetPixel(Mathf.Clamp((int)(vp.x * 960), 0, 959), Mathf.Clamp((int)(vp.y * 540), 0, 539));
                        Ts.Add($"{px.b:0.00}"); steps = Mathf.Max(steps, px.r);
                    }
                    var vis = new Texture2D(960, 540, TextureFormat.RGB24, false);
                    vis.SetPixels(dbg.GetPixels().Select(p => new Color(p.r, p.g, p.b)).ToArray()); vis.Apply();
                    tiles.Add(vis);
                    Object.DestroyImmediate(dbg);
                    rep.AppendLine($"| {shape} | {w:0} m | {gap:0} m from its edge | {string.Join(" / ", Ts)} | {steps * skin.maxSteps:0} of {skin.maxSteps} |");
                    Debug.Log($"[Chaser] {shape} from {gap:0} m: T on the axis {string.Join(" / ", Ts)}, steps {steps * skin.maxSteps:0}/{skin.maxSteps}; radius {tor.groundKm * 1000:0}/{tor.topKm * 1000:0} m, envelope {tor.envelope:0.00}, multi {tor.multi:0.00}, cond {tor.condReach:0.00}");
                }
            }
            Save(Sheet(tiles, 4), "designed_chaser"); foreach (var t in tiles) Object.DestroyImmediate(t);
            rep.AppendLine().AppendLine("`designed_chaser.png`: per row, one shape: from 300 m (as drawn, debug), from 1 km (as drawn, debug). Debug: red steps used, green detail steps, blue transmittance.");
        }

        if (only == "game")
        {
            ok &= GameViews(skin, cam, runner, plains, rep);
        }

        if (only == "bench")
        {
            // the Storm Test Bench's game scenario as a chaser sees it, for parts of a storm that vanish with the view:
            // both storms, each alone, and both without the storm depth write
            var parts = new List<Texture2D>();
            skin.UseStandaloneLook(); skin.renderScale = 1f; skin.enabled = false;
            var gameGo = new GameObject("Game scenario");
            var game = gameGo.AddComponent<DesignedScenarioRunner>(); game.spawnOnStart = false; game.lookFrom = skin; game.playing = false;
            game.Spawn(new FixedEnvironment(plains), 66074, ClimoRegion.Plains);
            skin.sunSource = SunSource.Fixed; skin.sunAzimuth = 244f; skin.sunElevation = 34f;
            float gameT = 29 * 60 + 10;
            void SettleGame()
            {
                for (float t = gameT - 90f; t <= gameT + 1e-3f; t += 2f)
                {
                    game.gameTime = t; game.Apply();
                    foreach (var r in game.Runners) if (r && r.gameObject.activeSelf) r.skin.StepTo(r.skin.playbackTime, t);
                }
                Sync();
            }
            SettleGame();
            foreach (var (bearing, look) in new[] { (157f, 29f), (107f, 17f) })
            {
                var focus = game.UpdraftWorld(0); var dir = Bearing(bearing);
                var pos = focus + dir * 30500f; pos.y = skin.groundHeight + 142f;
                var flat = focus - pos; flat.y = 0;
                cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flat, Vector3.up) * Quaternion.Euler(-look, 0, 0)); cam.fieldOfView = 60f;
                Texture2D Take(string what) { foreach (var r in game.Runners) r.skin.ResetHistory(cam); var t = Shot(cam, 960, 540); Debug.Log($"[Bench] bearing {bearing}: {what}"); return t; }
                parts.Add(Take("both storms"));
                var g = StormSkinCustomPass.Global; g.writeDepth = false; parts.Add(Take("both, no depth write")); g.writeDepth = true;
                for (int i = 0; i < game.Runners.Count; i++)
                {
                    for (int j = 0; j < game.Runners.Count; j++) game.Runners[j].gameObject.SetActive(j == i);
                    parts.Add(Take($"storm {i + 1} alone"));
                }
                game.Apply();
                foreach (var r in game.Runners) r.gameObject.SetActive(true);
            }
            Save(Sheet(parts, 4), "designed_bench");
            foreach (var t in parts) Object.DestroyImmediate(t);
            Object.DestroyImmediate(gameGo);
            rep.AppendLine("`designed_bench.png`: per row, one view: both storms, both without the depth write, storm 1 alone, storm 2 alone.");
        }

        if (only == null || only == "timelapse")
        {
            // a storm with at least three cycles and two tornadoes
            int pick = 1;
            for (int seed = 1; seed < 400; seed++)
            {
                var c0 = new DesignedStorm(new FixedEnvironment(plains), new Vec2(0, 0), seed, ClimoRegion.Plains);
                if (c0.cycles.Count >= 3 && c0.cycles.Count(c => c.tornado != null) >= 2) { pick = seed; break; }
            }
            var st = runner.Spawn(new FixedEnvironment(plains), Vector2.zero, pick, ClimoRegion.Plains);
            rep.AppendLine("## Timelapse").AppendLine();
            rep.AppendLine($"Seed {st.genome.seed}: life {st.lifeS / 60:0} min; cycles: " + string.Join("; ", st.cycles.Select(c => c.tornado == null
                ? $"wall cloud at {c.wallS / 60:0} min, no tornado"
                : $"wall cloud at {c.wallS / 60:0} min, EF{c.tornado.ef} {c.tornado.shape} {c.tornado.widthM:0} m from {c.tornado.formS / 60:0} to {c.tornado.EndS / 60:0} min"))).AppendLine();
            rep.AppendLine().AppendLine("| Time | Stage | Shot |").AppendLine("| --- | --- | --- |");
            var tiles = new List<Texture2D>();
            for (float T = 600; T < st.lifeS; T += 600)
            {
                Settle(skin, T);
                var U = KmToWorld(skin, st.UpdraftAt(T));
                var m = st.MotionAt(T); var fwd = new Vector3((float)m.x, 0, (float)m.y).normalized; var right = new Vector3(fwd.z, 0, -fwd.x);
                // the chaser keeps 22 km south-east of the updraft (ahead and to the right of the motion), looking at the base
                Pose(cam, U + (right * 0.8f + fwd * 0.6f).normalized * 22000f + Vector3.up * 2f, U + Vector3.up * 2500f, 55f);
                skin.ResetHistory(cam);
                var tex = Shot(cam, 640, 360);
                tiles.Add(tex);
                rep.AppendLine($"| {T / 60:0} min | {skin.Params.stage} | tile {tiles.Count} |");
                Debug.Log($"[Designed] timelapse {T / 60:0} min: {skin.Params.stage}");
            }
            if (tiles.Count > 0) { Save(Sheet(tiles, 4), "designed_timelapse"); foreach (var t in tiles) Object.DestroyImmediate(t); }
            rep.AppendLine().AppendLine("Contact sheet: `designed_timelapse.png`, 10 min apart, left to right, top to bottom.").AppendLine();
        }

        skin.Release();
        Object.DestroyImmediate(runner);
        File.WriteAllText(Path.Combine(OutDir, only == "anatomy" ? "AnatomySkinReport.md" : only == "chaser" ? "ChaserSkinReport.md" : only == "game" ? "GameSkinReport.md" : "DesignedSkinReport.md"), rep.ToString());
        Debug.Log($"[Designed] render test {(ok ? "PASS" : "FAIL")}; screenshots in {ShotDir}");
        return ok;
    }

    /// <summary>A quad of StormLab/RainStreak (an HDRP transparent) in one flat colour, facing from, size m.</summary>
    static GameObject Marker(string name, Vector3 at, Vector3 from, float size, Color c)
    {
        var mesh = new Mesh { name = name };
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
        // u at the middle across (the shader's edge fade peaks there); v 0.2-0.75 is where its end fade is 1
        mesh.uv = new[] { new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.3f), new Vector2(0.5f, 0.7f), new Vector2(0.5f, 0.7f) };
        mesh.colors = new[] { c, c, c, c };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        var go = new GameObject(name);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = new Material(Shader.Find("StormLab/RainStreak"));
        go.transform.position = at; go.transform.rotation = Quaternion.LookRotation(at - from, Vector3.up); go.transform.localScale = Vector3.one * size;
        return go;
    }

    /// <summary>
    /// The game's look, as a host game sets it up under HDRP: physically based sky, a 100,000 lux sun, fixed exposure,
    /// ACES, fog, HDRP's volumetric clouds and a lit ground, with the skin in its defaults (physical units, no backdrop).
    /// Checks that the storm hides what is behind it: the funnel looks the same with HDRP's volumetric clouds on and off,
    /// and a transparent marker behind the tornado is hidden while one in front of it shows.
    /// </summary>
    static bool GameViews(StormSkin skin, Camera cam, DesignedStormRunner runner, SEnv plains, StringBuilder rep)
    {
        bool ok = true;
        var made = new List<GameObject>();
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) l.enabled = false;
        var sunGo = new GameObject("Game sun"); made.Add(sunGo);
        var sun = sunGo.AddComponent<Light>(); sun.type = LightType.Directional;
        sunGo.AddComponent<HDAdditionalLightData>();
        sun.lightUnit = LightUnit.Lux; sun.intensity = 100000f; sun.color = Color.white;
        sunGo.transform.rotation = Quaternion.LookRotation(-StormSkin.VolToWorldDir(StormSkin.DefaultSunVol));
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        var env = profile.Add<VisualEnvironment>(true); env.skyType.Override((int)SkyType.PhysicallyBased); env.cloudType.Override(0);
        profile.Add<PhysicallyBasedSky>(true);
        var ex = profile.Add<Exposure>(true); ex.mode.Override(ExposureMode.Fixed); ex.fixedExposure.Override(13f);
        var tm = profile.Add<Tonemapping>(true); tm.mode.Override(TonemappingMode.ACES);
        var fog = profile.Add<Fog>(true); fog.enabled.Override(true); fog.meanFreePath.Override(40000f); fog.maxFogDistance.Override(100000f);
        var clouds = profile.Add<VolumetricClouds>(true); clouds.enable.Override(true);
        var volGo = new GameObject("Game volume"); made.Add(volGo);
        var vol = volGo.AddComponent<Volume>(); vol.isGlobal = true; vol.priority = 10; vol.sharedProfile = profile;
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane); made.Add(ground);
        ground.name = "Game ground"; ground.transform.localScale = new Vector3(12000f, 1f, 12000f);
        var lit = new Material(Shader.Find("HDRP/Lit")); lit.SetColor("_BaseColor", new Color(0.18f, 0.2f, 0.12f)); lit.SetFloat("_Smoothness", 0.1f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = lit;
        bool tm0 = skin.toneMap, bd0 = skin.drawBackdrop, na0 = skin.nightAdaptation; var sun0 = skin.sun;
        skin.toneMap = false; skin.drawBackdrop = false; skin.nightAdaptation = false; skin.sun = sun;
        rep.AppendLine("## Game look (HDRP sky, sun 100,000 lux, EV 13, ACES, fog, volumetric clouds)").AppendLine();
        rep.AppendLine($"Skin luminance {skin.Luminance:0} nits per unit.").AppendLine();
        rep.AppendLine("| Shape | Axis change with HDRP clouds on/off (100 / 300 / 600 m) | Marker behind: change | Marker in front: change |").AppendLine("| --- | --- | --- | --- |");
        var tiles = new List<Texture2D>();
        try
        {
            foreach (var (shape, w, ef) in new[] { (TornadoShape.MultiVortex, 900.0, 3), (TornadoShape.Wedge, 1400.0, 4) })
            {
                int found = -1;
                for (int seed = 1; seed < 400 && found < 0; seed++)
                    if (new DesignedStorm(new FixedEnvironment(plains), new Vec2(0, 0), seed, ClimoRegion.Plains).cycles.Any(c => c.tornado != null)) found = seed;
                var st = runner.Spawn(new FixedEnvironment(plains), Vector2.zero, found, ClimoRegion.Plains);
                // the runner binds the skin afresh; keep the game look on it
                skin.toneMap = false; skin.drawBackdrop = false; skin.nightAdaptation = false; skin.sun = sun;
                var cyc = st.cycles.First(c => c.tornado != null);
                var tg = cyc.tornado;
                tg.ef = ef; tg.widthM = w; tg.vmax = TornadoClimatology.SampleVmax(ef, 0.5); tg.rcKm = C.Clamp(0.5 * w / 1000 * 29.0 / tg.vmax, 0.012, 0.6);
                tg.lifeS = 1500; tg.rainWrap = 0;
                StormGenome.Reshape(tg, shape, st.CharacterAt(tg.formS), 7);
                tg.phases = null; tg.satellite = false; tg.leanKm = 0; tg.ghost = 0;
                cyc.occludeS = tg.formS + tg.lifeS * (1 - 0.6 * tg.ropeFrac); cyc.endS = tg.EndS + 300;
                foreach (var c in st.cycles.Where(c => c.index > cyc.index)) { c.wallS += 5000; c.occludeS += 5000; c.endS += 5000; if (c.tornado != null) c.tornado.formS += 5000; }
                float T = (float)(tg.formS + 0.5 * tg.lifeS);
                Settle(skin, T);
                var tor = st.TornadoAt(T);
                var g = KmToWorld(skin, tor.ground);
                double zb = st.CharacterAt(T).zb;
                var eye = g + Bearing(140f) * ((float)tor.groundKm * 1000f + 1000f) + Vector3.up * 2f;
                Pose(cam, eye, g + Vector3.up * (float)(zb * 380), 60f);
                var axis = new[] { 100f, 300f, 600f }.Select(z => g + Vector3.up * z).ToArray();
                Color[] At(Texture2D t) => axis.Select(p => { var v = cam.WorldToViewportPoint(p); return t.GetPixel(Mathf.Clamp((int)(v.x * t.width), 0, t.width - 1), Mathf.Clamp((int)(v.y * t.height), 0, t.height - 1)); }).ToArray();
                float Diff(Color a, Color b) => (Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b)) / Mathf.Max(a.r + a.g + a.b, 0.02f);
                Texture2D Take() { skin.ResetHistory(cam); return Shot(cam, 960, 540, 16); }

                var on = Take(); tiles.Add(on);
                clouds.enable.Override(false); var off = Take(); clouds.enable.Override(true); tiles.Add(off);
                var dClouds = At(on).Zip(At(off), Diff).ToArray();
                // red transparents on the line of sight to the axis 300 m up: one well beyond the tornado, one halfway to it
                var behind = Marker("Marker behind", eye + (axis[1] - eye) * 1.8f, eye, 500f, new Color(1f, 0f, 0f, 1f)); made.Add(behind);
                var withBehind = Take(); tiles.Add(withBehind); behind.SetActive(false);
                var front = Marker("Marker in front", eye + (axis[1] - eye) * 0.5f, eye, 40f, new Color(1f, 0f, 0f, 1f)); made.Add(front);
                var withFront = Take(); tiles.Add(withFront); front.SetActive(false);
                float dBehind = Diff(At(on)[1], At(withBehind)[1]), dFront = Diff(At(on)[1], At(withFront)[1]);
                rep.AppendLine($"| {shape} | {string.Join(" / ", dClouds.Select(d => $"{d:0%}"))} | {dBehind:0%} | {dFront:0%} |");
                Debug.Log($"[Game] {shape}: axis change with HDRP clouds {string.Join(" / ", dClouds.Select(d => $"{d:0.000}"))}; marker behind {dBehind:0.000}, in front {dFront:0.000}; axis colours {string.Join(" ", At(on).Select(c => $"({c.r:0.00},{c.g:0.00},{c.b:0.00})"))}");
                if (dClouds.Max() > 0.05f) { ok = false; Debug.LogError($"[Game] {shape}: HDRP's volumetric clouds show through the funnel"); }
                if (dBehind > 0.05f) { ok = false; Debug.LogError($"[Game] {shape}: a transparent behind the tornado shows through it"); }
                if (dFront < 0.2f) { ok = false; Debug.LogError($"[Game] {shape}: a transparent in front of the tornado is hidden"); }
                // control: looking away from the storm, HDRP's clouds must show, or the check above proves nothing
                Pose(cam, eye, eye + Bearing(140f) * 1000f + Vector3.up * 350f, 60f);
                var awayOn = Take(); clouds.enable.Override(false); var awayOff = Take(); clouds.enable.Override(true);
                // the share of the view HDRP's clouds change by more than 10 % (they are a few small cumulus in a wide view)
                var pa = awayOn.GetPixels(); var pb = awayOff.GetPixels(); int changed = 0, n = 0;
                for (int k = 0; k < pa.Length; k += 13, n++) if (Diff(pa[k], pb[k]) > 0.1f) changed++;
                double dAway = changed / (double)n;
                tiles.Add(awayOn); tiles.Add(awayOff); Object.DestroyImmediate(withFront); tiles.Remove(withFront);
                rep.AppendLine($"| {shape} control, facing away | HDRP clouds change {dAway:0.0%} of the view | | |");
                Debug.Log($"[Game] {shape}: facing away from the storm, HDRP clouds change {dAway:0.000} of the view");
                if (dAway < 0.005) { ok = false; Debug.LogError($"[Game] {shape}: HDRP's volumetric clouds do not render; the cloud check is void"); }
            }
            Save(Sheet(tiles, 5), "designed_game");
            rep.AppendLine().AppendLine("`designed_game.png`: per row, one shape from 1 km: HDRP clouds on, off, red marker behind the tornado; then facing away with HDRP clouds on and off.");
        }
        finally
        {
            foreach (var t in tiles) Object.DestroyImmediate(t);
            foreach (var go in made) Object.DestroyImmediate(go);
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude)) l.enabled = true;
            skin.toneMap = tm0; skin.drawBackdrop = bd0; skin.nightAdaptation = na0; skin.sun = sun0;
        }
        return ok;
    }
}
