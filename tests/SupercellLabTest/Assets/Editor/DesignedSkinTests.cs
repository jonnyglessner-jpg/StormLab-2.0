// Designed-storm render test: the skin drawing a DesignedStorm (no brain). Writes
//   Screenshots/designed_tornado_<shape>.png  one ground view per tornado shape, mid-life
//   Screenshots/designed_tornado_sheet.png    all nine, 3 x 3
//   Screenshots/designed_tornado_close.png    six shapes from 1-3 km, the sun to the side: the funnel's surface
//   Screenshots/designed_tornado_variety.png  first tornadoes of unforced storms across presets and regions, as drawn
//   Screenshots/designed_tornado_variety_sunlit.png  the same tornadoes from the sun's side: the light on the funnel
//   Screenshots/designed_tornado_life.png     one tornado that changes shape, through its life
//   Screenshots/designed_timelapse.png        one multi-cycle storm every 10 min from a chaser following it
// and validation/reports/DesignedSkinReport.md. Batch (never -nographics):
//   Unity.exe -batchmode -projectPath <SupercellLabTest> -executeMethod DesignedSkinTests.RunFromCommandLine -logFile designed.log
//   [-designedOnly sheet|variety|life|timelapse|anatomy]
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
        File.WriteAllText(Path.Combine(OutDir, only == "anatomy" ? "AnatomySkinReport.md" : "DesignedSkinReport.md"), rep.ToString());
        Debug.Log($"[Designed] render test {(ok ? "PASS" : "FAIL")}; screenshots in {ShotDir}");
        return ok;
    }
}
