// Designed-storm render test: the skin drawing a DesignedStorm (no brain). Writes
//   Screenshots/designed_tornado_<shape>.png  one ground view per tornado shape, mid-life
//   Screenshots/designed_tornado_sheet.png    all eight, 4 x 2
//   Screenshots/designed_timelapse.png        one multi-cycle storm every 10 min from a chaser following it
// and Validation/DesignedSkinReport.md. Batch (never -nographics):
//   Unity.exe -batchmode -projectPath <SupercellLabTest> -executeMethod DesignedSkinTests.RunFromCommandLine -logFile designed.log
//   [-designedOnly sheet|timelapse]
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
    static string OutDir => Path.Combine(ProjectDir, "Validation");

    [MenuItem("Supercell Lab/Run Designed Storm Screenshots")]
    public static void RunMenu() => Run(null);

    public static void RunFromCommandLine()
    {
        bool ok;
        try
        {
            var a = System.Environment.GetCommandLineArgs(); int i = Array.IndexOf(a, "-designedOnly");
            ok = Run(i >= 0 && i + 1 < a.Length ? a[i + 1] : null);
        }
        catch (Exception e) { Debug.LogException(e); ok = false; }
        EditorApplication.Exit(ok ? 0 : 1);
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
            var tiles = new List<Texture2D>();
            var widths = new Dictionary<TornadoShape, (double w, int ef)>
            {
                { TornadoShape.Rope, (90, 1) }, { TornadoShape.Cone, (350, 2) }, { TornadoShape.Stovepipe, (450, 3) }, { TornadoShape.Wedge, (1400, 4) },
                { TornadoShape.MultiVortex, (900, 3) }, { TornadoShape.Drillbit, (140, 1) }, { TornadoShape.ElephantTrunk, (220, 2) }, { TornadoShape.DustWhirl, (180, 1) },
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
                Debug.Log($"[Designed] {shape}: EF{tor.ef} amount {tor.amount:0.00} ground {tor.groundKm:0.000} top {tor.topKm:0.000} cond {tor.condReach:0.00} multi {tor.multi:0.00} envelope {tor.envelope:0.00} spin {tor.spin:0.00} stage {skin.Params.stage}");
                if (!tor.on) { ok = false; Debug.LogError($"[Designed] {shape}: no tornado at the shot time"); }
                rep.AppendLine($"| {shape} | EF{tor.ef} | {w:0} m | {tor.groundKm * 1000:0} / {tor.topKm * 1000:0} m | {tor.condReach:0.00} | `designed_tornado_{shape}.png` |");
            }
            if (tiles.Count > 0) { Save(Sheet(tiles, 4), "designed_tornado_sheet"); foreach (var t in tiles) Object.DestroyImmediate(t); }
            rep.AppendLine().AppendLine("Sheet: `designed_tornado_sheet.png`, in the order Rope, Cone, Stovepipe, Wedge / MultiVortex, Drillbit, ElephantTrunk, DustWhirl.").AppendLine();
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
        File.WriteAllText(Path.Combine(OutDir, "DesignedSkinReport.md"), rep.ToString());
        Debug.Log($"[Designed] render test {(ok ? "PASS" : "FAIL")}; screenshots in {ShotDir}");
        return ok;
    }
}
