// Headless validation of the Phase 1 brain (Runtime/Dynamics/README.md). Batch:
//   Unity.exe -batchmode -projectPath unity/SupercellLabTest -executeMethod StormLab.Supercell.EditorTools.DynamicsValidation.RunFromCommandLine
//             [-dynSuite all|quick|wk|ddc|perf] -logFile <file>
// Writes Validation/ValidationReport.md and Validation/ddc_20160524_hrrr.csv next to the test project's Assets folder.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StormLab.Supercell.Dynamics;
using UnityEditor;
using UnityEngine;
using SEnv = StormLab.Supercell.Environment;

namespace StormLab.Supercell.EditorTools
{
    public static class DynamicsValidation
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static StringBuilder report;
        static int passed, failed;

        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Validation"));
        static string RepoRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));

        [MenuItem("Supercell Lab/Run Dynamics Validation (quick)")]
        public static void RunQuickMenu() => Run("quick");
        [MenuItem("Supercell Lab/Run Dynamics Validation (all)")]
        public static void RunAllMenu() => Run("all");

        public static void RunFromCommandLine()
        {
            string suite = "all";
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dynSuite") suite = args[i + 1];
            bool ok;
            try { ok = Run(suite); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Run(string suite)
        {
            passed = failed = 0;
            report = new StringBuilder();
            Directory.CreateDirectory(OutDir);
            report.AppendLine("# Supercell Lab 2.0 dynamics validation");
            report.AppendLine();
            report.AppendLine($"Run {DateTime.Now:yyyy-MM-dd HH:mm}, suite `{suite}`, GPU {SystemInfo.graphicsDeviceName} ({SystemInfo.graphicsDeviceType}), Unity {Application.unityVersion}.");
            report.AppendLine();
            report.AppendLine("| Test | Result | Detail |");
            report.AppendLine("| --- | --- | --- |");
            var tail = new StringBuilder();
            if (!SystemInfo.supportsComputeShaders) { Log("compute shaders unsupported (is -nographics set?)"); return false; }
            bool all = suite == "all";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (all || suite == "quick")
            {
                Guard("calm atmosphere stays at rest", CalmAtRest);
                Guard("uniform wind carried unchanged", UniformWind);
                Guard("pressure solver: flow is mass-conserving", Divergence);
                Guard("microphysics conserves water and energy", MicroConservation);
                Guard("water mass conserved by the full model", WaterBudget);
                Guard("dry warm bubble rises", WarmBubble);
                Guard("capped sounding stays quiet", Capped);
                Guard("CIN delays initiation", InitiationTiming);
                Guard("outflow boundary in the initial state", Outflow);
                Guard("seek restores a checkpoint and replays bit for bit", SeekReplay);
                Guard("run-ahead scheduler respects budget and speed cap", Scheduler);
            }
            if (all || suite == "wk") Guard("Weisman-Klemp sounding makes a supercell (average tier)", () => WeismanKlemp(tail));
            if (all || suite == "ddc")
            {
                Guard("Dodge City 2016-05-24 HRRR sounding, 2 h (average tier)", () => DodgeCity(tail));
                Guard("Dodge City storm vs KDDC radar geometry", () => RadarCompare(tail));
            }
            if (all || suite == "perf") Guard("step time at both tiers", () => Perf(tail));
            if (suite == "tune") Guard("sensitivity: mixing and rain evaporation", () => Tune(tail));
            report.AppendLine();
            report.AppendLine($"{passed} passed, {failed} failed, {sw.Elapsed.TotalMinutes:0.0} min.");
            report.AppendLine();
            report.Append(tail);
            File.WriteAllText(Path.Combine(OutDir, suite == "all" ? "ValidationReport.md" : $"ValidationReport_{suite}.md"), report.ToString());
            Log($"overall {(failed == 0 ? "PASS" : "FAIL")}: {passed} passed, {failed} failed; report in {OutDir}");
            return failed == 0;
        }

        static void Guard(string name, Func<string> test)
        {
            string detail; bool ok;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { detail = test(); ok = !detail.StartsWith("FAIL", StringComparison.Ordinal); }
            catch (Exception e) { Debug.LogException(e); detail = "FAIL exception: " + e.Message; ok = false; }
            if (ok) passed++; else failed++;
            detail = detail.StartsWith("FAIL ", StringComparison.Ordinal) ? detail.Substring(5) : detail;
            Log($"{(ok ? "PASS" : "FAIL")} {name}: {detail} ({sw.Elapsed.TotalSeconds:0.0} s)");
            report.AppendLine($"| {name} | {(ok ? "PASS" : "**FAIL**")} | {detail.Replace("|", "/")} |");
        }

        static void Log(string m) => Debug.Log("[Dynamics] " + m);
        static string F(float v, string fmt = "0.000") => v.ToString(fmt, Inv);
        static string F(double v, string fmt = "0.000") => v.ToString(fmt, Inv);
        static string Verdict(bool ok, string s) => ok ? s : "FAIL " + s;

        // ------------------------------------------------------------ small grids for the 1.0 cases
        static SimGrid Small() => SimGrid.Make(48, 48, 30, 1000f, 15000f, 250f, 1000f);

        /// <summary>number of levels for the average tier in the tune and perf suites (-dynNZ, default the tier's 72)</summary>
        static int AverageNZ()
        {
            var args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dynNZ") return int.Parse(args[i + 1], Inv);
            return SimGrid.ForTier(SimTier.Average).NZ;
        }
        static SimGrid AverageGrid() { var g = SimGrid.ForTier(SimTier.Average); return SimGrid.Make(g.NX, g.NY, AverageNZ(), g.dx); }
        static SimParams SmallParams(SimGrid g) { var p = SimParams.ForGrid(g); p.dt = 5f; return p; }

        static FuncProfile StableDry(double u, double v) => new FuncProfile
        { theta = z => 300 + 0.004 * z, qv = z => 0.004 * Math.Exp(-z / 2000), u = z => u, v = z => v };

        static float MaxAbsDev(float[] a, float v) { float m = 0; foreach (var x in a) m = Math.Max(m, Math.Abs(x - v)); return m; }

        static string CalmAtRest()
        {
            var g = Small(); var p = SmallParams(g);
            using (var m = new SupercellModel(g, BaseState.FromProfile(g, StableDry(0, 0), 0, 0), p))
            {
                m.Step(15);
                var w = m.FieldStats(m.W, Layout.W); var u = m.FieldStats(m.U, Layout.U);
                bool ok = w.maxAbs < 1e-4f && u.maxAbs < 1e-4f && w.nan == 0;
                return Verdict(ok, $"after 15 steps max abs w {w.maxAbs:E1} m/s, u {u.maxAbs:E1} m/s (limit 1E-4)");
            }
        }

        static string UniformWind()
        {
            var g = Small(); var p = SmallParams(g);
            using (var m = new SupercellModel(g, BaseState.FromProfile(g, StableDry(12, -5), 0, 0), p))
            {
                m.Step(10);
                float du = MaxAbsDev(m.Download(m.U, Layout.U), 12f), dv = MaxAbsDev(m.Download(m.V, Layout.V), -5f);
                var w = m.FieldStats(m.W, Layout.W);
                bool ok = du < 1e-3f && dv < 1e-3f && w.maxAbs < 1e-3f;
                return Verdict(ok, $"u=12, v=-5 m/s after 10 steps: max change u {du:E1}, v {dv:E1}, w {w.maxAbs:E1} m/s (limit 1E-3)");
            }
        }

        static double MaxDivergence(SupercellModel m)
        {
            var g = m.g; var b = m.b; int NX = g.NX, NY = g.NY, plane = NX * NY;
            var u = m.Download(m.U, Layout.U); var v = m.Download(m.V, Layout.V); var w = m.Download(m.W, Layout.W);
            double mx = 0, scale = 0;
            for (int k = 0; k < g.NZ; k++) for (int j = 0; j < NY; j++) for (int i = 0; i < NX; i++)
            {
                double du = u[(k * NY + j) * (NX + 1) + i + 1] - u[(k * NY + j) * (NX + 1) + i];
                double dv = v[(k * (NY + 1) + j + 1) * NX + i] - v[(k * (NY + 1) + j) * NX + i];
                double dw = b.rhoW[k + 1] * w[(k + 1) * plane + j * NX + i] - b.rhoW[k] * w[k * plane + j * NX + i];
                double d = (b.rho0[k] * (du / g.dx + dv / g.dy) + dw / g.dzc[k]) / b.rho0[k];
                mx = Math.Max(mx, Math.Abs(d));
                scale = Math.Max(scale, Math.Abs(dw / g.dzc[k] / b.rho0[k]));
            }
            return mx;
        }

        static string Divergence()
        {
            var g = Small(); var p = SmallParams(g);
            var wk = new WeismanKlempProfile(0.014, 25);
            using (var m = new SupercellModel(g, BaseState.FromProfile(g, wk, 12, 0), p))
            {
                m.Initialise(new RunSetup { noiseAmp = 0 }, new Bubble { x = 20000, y = 24000, z = 1400, rh = 8000, rz = 1400, dth = 3 });
                m.Step(20);
                double div = MaxDivergence(m);
                var w = m.FieldStats(m.W, Layout.W);
                bool ok = w.maxAbs > 1f && div < 1e-5;
                return Verdict(ok, $"moist bubble after 20 steps: max w {w.maxAbs:0.0} m/s, max abs div(rho u)/rho {div:E1} 1/s (limit 1E-5; 1.0's relaxation solver allowed 2E-5)");
            }
        }

        static string MicroConservation()
        {
            // the four 1.0 cases: [th, qv, qc, qr, qi, qs, qh], pi, p, rho
            var cases = new[]
            {
                (new[] { 300f, 0.016f, 0.003f, 0.002f, 0f, 0f, 0f }, 0.97f, 90000f, 1.05f),
                (new[] { 351f, 0.0025f, 0.002f, 0.003f, 0.0005f, 0.001f, 0.002f }, 0.75f, 36500f, 0.48f),
                (new[] { 372f, 0.0001f, 0f, 0f, 0.0008f, 0.0005f, 0f }, 0.6f, 17000f, 0.26f),
                (new[] { 316f, 0.005f, 0f, 0.001f, 0f, 0.0005f, 0.004f }, 0.88f, 64000f, 0.8f),
            };
            var g = SimGrid.Make(8, 8, 4, 1000f, 4000f, 1000f, 0f, true);
            var b = BaseState.FromProfile(g, StableDry(0, 0), 0, 0);
            for (int k = 0; k < 4; k++) { b.pi0[k] = cases[k].Item2; b.p0[k] = cases[k].Item3; b.rho0[k] = cases[k].Item4; }
            var p = SimParams.ForGrid(g); p.dt = 6f;
            double worstW = 0, worstE = 0; bool neg = false;
            using (var m = new SupercellModel(g, b, p))
            {
                var bufs = new[] { m.TH, m.QV, m.QC, m.QR, m.QI, m.QS, m.QH };
                for (int f = 0; f < 7; f++)
                {
                    var a = new float[g.Cells];
                    for (int k = 0; k < 4; k++) for (int c = k * g.Plane; c < (k + 1) * g.Plane; c++) a[c] = cases[k].Item1[f];
                    m.Upload(bufs[f], a);
                }
                m.MicroOnly(20);
                var outF = bufs.Select(x => m.Download(x, Layout.Centre)).ToArray();
                for (int k = 0; k < 4; k++)
                {
                    var s0 = cases[k].Item1; int c = k * g.Plane + 3 * g.NX + 3; float pi = cases[k].Item2;
                    double w0 = s0[1] + s0[2] + s0[3] + s0[4] + s0[5] + s0[6];
                    double w1 = 0; for (int f = 1; f < 7; f++) w1 += outF[f][c];
                    double e0 = 1004 * s0[0] * pi + 2.5e6 * s0[1] - 3.34e5 * (s0[4] + s0[5] + s0[6]);
                    double e1 = 1004 * outF[0][c] * pi + 2.5e6 * outF[1][c] - 3.34e5 * (outF[4][c] + outF[5][c] + outF[6][c]);
                    worstW = Math.Max(worstW, Math.Abs(w1 - w0) / w0); worstE = Math.Max(worstE, Math.Abs(e1 - e0) / 1004);
                    for (int f = 2; f < 7; f++) if (outF[f][c] < 0) neg = true;
                }
            }
            bool ok = worstW < 2e-6 && worstE < 0.02 && !neg;
            return Verdict(ok, $"4 cases x 20 steps: worst relative water change {worstW:E1} (fp32 limit 2E-6), energy {worstE:0.0000} K (limit 0.02), negative mixing ratios: {(neg ? "yes" : "none")}");
        }

        static double TotalWater(SupercellModel m)
        {
            var g = m.g; double total = 0;
            var fs = new[] { m.QV, m.QC, m.QR, m.QI, m.QS, m.QH }.Select(x => m.Download(x, Layout.Centre)).ToArray();
            for (int k = 0; k < g.NZ; k++)
            {
                double lay = 0;
                for (int c = k * g.Plane; c < (k + 1) * g.Plane; c++) for (int f = 0; f < 6; f++) lay += fs[f][c];
                total += lay * m.b.rho0[k] * g.dzc[k];
            }
            var acc = m.DownloadAcc(); foreach (var a in acc) total += a;   // mm = kg/m^2
            return total * g.dx * g.dy;
        }
        static double Condensate(SupercellModel m)
        {
            var g = m.g; double total = 0;
            var fs = new[] { m.QC, m.QR, m.QI, m.QS, m.QH }.Select(x => m.Download(x, Layout.Centre)).ToArray();
            for (int k = 0; k < g.NZ; k++)
            {
                double lay = 0; for (int c = k * g.Plane; c < (k + 1) * g.Plane; c++) for (int f = 0; f < 5; f++) lay += fs[f][c];
                total += lay * m.b.rho0[k] * g.dzc[k];
            }
            var acc = m.DownloadAcc(); foreach (var a in acc) total += a;
            return total * g.dx * g.dy;
        }

        static string WaterBudget()
        {
            // closed box: no mean wind, sponge and upper damping off, so every change of total water is the model's error
            var g = SimGrid.Make(64, 64, 40, 1000f, 18000f, 250f, 1000f); var p = SmallParams(g);
            p.spongeTau = 1e9f; p.dampZ = 17999f; p.cd = 0; p.closedLateral = true;
            var wk = new WeismanKlempProfile(0.014, 0);
            using (var m = new SupercellModel(g, BaseState.FromProfile(g, wk, 0, 0), p))
            {
                m.SetNudge(new Nudge { tau = p.dt, towers = new[] { new Tower { x = 32000, y = 32000, z = 1500, rh = 10000, rz = 1500, wmax = 10, t0 = 0, t1 = 900 } } });
                double w0 = TotalWater(m);
                m.Step(300);   // 25 min
                double w1 = TotalWater(m), cyc = Condensate(m);
                var acc = m.DownloadAcc(); double rain = acc.Sum(a => (double)a) * g.dx * g.dy;
                double err = Math.Abs(w1 - w0) / Math.Max(cyc, 1);
                bool ok = err < 0.05 && cyc > 0;
                return Verdict(ok, $"WK moist storm in a closed 64 km box (walls, no sponge, no upper damping), 25 min: total water change {(w1 - w0) / 1e6:0.0} kt = {err * 100:0.00} % of the condensate aloft plus precipitation on the ground ({cyc / 1e6:0.0} kt, of which {rain / 1e6:0.0} kt on the ground; limit 5 %)");
            }
        }

        static string WarmBubble()
        {
            var g = Small(); var p = SmallParams(g);
            var prof = new FuncProfile { theta = z => 300, qv = z => 0, u = z => 0, v = z => 0 };
            using (var m = new SupercellModel(g, BaseState.FromProfile(g, prof, 0, 0), p))
            {
                m.Initialise(new RunSetup { noiseAmp = 0 }, new Bubble { x = 24000, y = 24000, z = 2000, rh = 6000, rz = 1500, dth = 2 });
                m.Step(60);   // 300 s
                var th = m.Download(m.TH, Layout.Centre); float best = 0; int kb = 0;
                for (int k = 0; k < g.NZ; k++) for (int c = k * g.Plane; c < (k + 1) * g.Plane; c++) if (th[c] - 300 > best) { best = th[c] - 300; kb = k; }
                var w = m.FieldStats(m.W, Layout.W);
                bool ok = g.zc[kb] > 2400 && w.maxAbs > 1;
                return Verdict(ok, $"2 K bubble at 2 km after 300 s: warm core at {g.zc[kb]:0} m (must pass 2,400 m), max w {w.maxAbs:0.0} m/s");
            }
        }

        static SEnv CappedEnv() => SEnv.Make(new EnvInput { T0F = 90, TdF = 68, moist = 0, lapse = 8.0, cap = 4.0, llj = 36 * C.KT, shear = 50 * C.KT, upper = 80 * C.KT });

        static string Capped()
        {
            var E = CappedEnv();
            var s = new RunSetup { tier = SimTier.Test, runLength = 1800 };
            using (var run = StormRun.FromEnvironment(E, s))
            {
                run.recording = false; run.checkpointing = false;
                run.AdvanceTo(1800);
                var qc = run.model.FieldStats(run.model.QC, Layout.Centre); var qi = run.model.FieldStats(run.model.QI, Layout.Centre);
                bool ok = !run.plan.fires && qc.maxAbs < 1e-6f && qi.maxAbs < 1e-6f;
                return Verdict(ok, $"1.0 'cap holds' preset, 30 min with 0.1 K noise: {run.plan}; max qc {qc.maxAbs:E1}, qi {qi.maxAbs:E1} kg/kg (limit 1E-6)");
            }
        }

        static string InitiationTiming()
        {
            var igra = SoundingFile.ToEnvironment(SoundingFile.Parse(File.ReadAllText(Path.Combine(RepoRoot, "data", "raw", "soundings", "ddc_20160524_igra.json"))));
            var hrrr = SoundingFile.ToEnvironment(SoundingFile.Parse(File.ReadAllText(Path.Combine(RepoRoot, "data", "raw", "soundings", "ddc_20160524_hrrr.json"))));
            var pi = Initiation.FromEnvironment(igra, 10800); var ph = Initiation.FromEnvironment(hrrr, 10800);
            bool ok = pi.fires && ph.fires && ph.onset < 1 && pi.onset > 1800 && pi.onset < 7200;
            return Verdict(ok, $"DDC 19Z special sounding: {pi}. HRRR 22Z: {ph}");
        }

        static string Outflow()
        {
            var E = SEnv.Make(new EnvInput { T0F = 84, TdF = 68, moist = 4000 * C.FT, lapse = 7.8, cap = 1.0, llj = 44 * C.KT, shear = 55 * C.KT, upper = 80 * C.KT });
            var s = new RunSetup { tier = SimTier.Test, runLength = 600, nudgeEnabled = false };
            var g = SimGrid.ForTier(SimTier.Test);
            s.outflow = new OutflowBoundary { enabled = true, x = g.Lx / 2, y = g.Ly * 0.6f, orientationDeg = 90, depth = 1200, dTheta = -4, width = 3000 };
            using (var run = StormRun.FromEnvironment(E, s))
            {
                run.Pump(true);
                var f0 = run.timeline.Get(0);
                // cold air on the north side of a west-east line at 60 % of the domain height
                float north = f0.coldPoolBox.y, expectedArea = g.Lx * (g.Ly * 0.4f) / 1e6f;
                bool ok = f0.coldPoolMin < -3.3f && f0.coldPoolMin > -4.6f && north > g.Ly * 0.55f && f0.coldPoolArea > 0.7f * expectedArea && f0.coldPoolArea < 1.3f * expectedArea;
                run.AdvanceTo(600); run.Pump(true);
                var f1 = run.timeline.AtTime(600);
                return Verdict(ok && f1 != null, $"-4 K, 1.2 km deep pool north of a west-east line: frame 0 coldest theta' {f0.coldPoolMin:0.0} K, -1 K area {f0.coldPoolArea:0} km2 (expected about {expectedArea:0}), south edge {north / 1000:0.0} km; after 10 min the gust front has spread to {f1.coldPoolArea:0} km2");
            }
        }

        static string SeekReplay()
        {
            var wk = new WeismanKlempProfile(0.014, 25);
            wk.ToEnvironmentGrid(out var sd, out var wd);
            var rm = wk.BunkersRightMover();
            var s = new RunSetup { tier = SimTier.Test, runLength = 1800, seed = 11 };
            using (var run = StormRun.FromProfile(wk, sd, wd, new Vector2((float)rm.x, (float)rm.y), s))
            {
                run.AdvanceTo(1500); run.Pump(true);
                var original = new Dictionary<int, TimelineFrame>();
                for (int i = 0; i <= run.timeline.Latest; i++) original[i] = run.timeline.Get(i);
                int nck = run.checkpoints.All.Count;
                var replay = run.Replay(1100, 1500);
                int same = 0, diff = 0; string first = "";
                foreach (var f in replay)
                {
                    var o = original[f.index];
                    bool eq = o.wMax == f.wMax && o.wColumn.SequenceEqual(f.wColumn) && o.map2D.SequenceEqual(f.map2D) && o.q3D.SequenceEqual(f.q3D) && o.meso1.zeta == f.meso1.zeta;
                    if (eq) same++; else { diff++; if (first == "") first = $" first difference at {f.t:0} s: w {o.wMax} vs {f.wMax}"; }
                }
                bool ok = diff == 0 && same == 60 && nck == 2 && Math.Abs(run.Time - 1500) < 1e-3;
                float wmax = original.Values.Max(f => f.wMax);
                return Verdict(ok, $"test grid, WK storm (peak w {wmax:0} m/s): {nck} checkpoints (0 and 15 min); seek to 1100 s restored 900 s and re-ran; {same} of {replay.Count} replayed frames identical to the originals{first}; timeline {run.timeline.Bytes / 1e6:0.0} MB for {run.timeline.Latest + 1} frames, {run.checkpoints.Bytes / 1e6:0.0} MB of checkpoints");
            }
        }

        static string Scheduler()
        {
            // average tier, where one whole step (about 9 ms) is bigger than a 4 ms frame budget
            var wk = new WeismanKlempProfile(0.014, 30);
            wk.ToEnvironmentGrid(out var sd, out var wd);
            var rm = wk.BunkersRightMover();
            var s = new RunSetup { tier = SimTier.Average, runLength = 3600 };
            using (var run = StormRun.FromProfile(wk, sd, wd, new Vector2((float)rm.x, (float)rm.y), s))
            {
                const float budget = 4f, dtWall = 1f / 60f;
                var sch = new RunAheadScheduler(run, budget, 0f);
                sch.Calibrate(3);
                float piece = Math.Max(budget, Math.Max(sch.diagMs, sch.segMs.Max()));
                var sw = new System.Diagnostics.Stopwatch();
                // budget only: 240 frames; each frame's queued work is timed to GPU completion
                var measured = new List<double>(); float maxEst = 0; float t0 = run.Time;
                for (int i = 0; i < 240; i++)
                {
                    run.model.Sync();
                    sw.Restart(); sch.Tick(dtWall); run.model.Sync(); measured.Add(sw.Elapsed.TotalMilliseconds);
                    maxEst = Math.Max(maxEst, sch.lastFrameMs);
                    if (i % 20 == 0) run.Pump(true);
                }
                float budgetSpeed = (run.Time - t0) / (240 * dtWall);
                measured.Sort();
                double mean = measured.Average(), p95 = measured[(int)(0.95 * measured.Count)], mx = measured[measured.Count - 1];
                // speed cap: 10x real time
                sch.maxSpeed = 10f; float t1 = run.Time;
                for (int i = 0; i < 240; i++) { sch.Tick(dtWall); if (i % 20 == 0) run.Pump(true); }
                run.Pump(true);
                float capped = (run.Time - t1) / (240 * dtWall);
                bool ok = maxEst <= piece + 1e-3f && mean <= budget * 1.25 + 0.5 && capped <= 10.5f && capped >= 9f && budgetSpeed > 10f;
                string segs = string.Join(", ", sch.segMs.Select((m, i) => $"{SupercellModel.SegmentNames[i]} {m:0.0}"));
                return Verdict(ok, $"average tier, 4 ms per 60 fps frame: step split into {SupercellModel.Segments} pieces ({segs}; diagnostics {sch.diagMs:0.0} ms); " +
                                   $"estimated GPU time per frame at most {maxEst:0.0} ms (largest piece {piece:0.0} ms); measured per frame (queue to GPU done, includes sync) mean {mean:0.0}, 95th percentile {p95:0.0}, max {mx:0.0} ms; " +
                                   $"budget alone runs {budgetSpeed:0}x real time; with the 10x cap {capped:0.0}x");
            }
        }

        // ------------------------------------------------------------ long runs
        static string WeismanKlemp(StringBuilder tail)
        {
            var wk = new WeismanKlempProfile(0.014, 30);
            wk.ToEnvironmentGrid(out var sd, out var wd);
            var rm = wk.BunkersRightMover();
            var s = new RunSetup { tier = SimTier.Average, runLength = 7200 };
            var csv = new StringBuilder("t_min,wmax_ms,updraft_x_km,updraft_y_km,updraft_z_m,meso1_zeta,meso1_zeta_peak,meso4_zeta,meso_depth_m,coldpool_K,dbz_max,echo_top18_m,hail_mm,storm_from_deg,storm_kt\n");
            float peakW = 0, peakZ1 = 0, peakZ1Late = 0, peakZ4 = 0, coldest = 0; string plan;
            var motions = new List<Vector2>(); string motion;
            var wall = System.Diagnostics.Stopwatch.StartNew();
            using (var run = StormRun.FromProfile(wk, sd, wd, new Vector2((float)rm.x, (float)rm.y), s))
            {
                plan = run.plan.ToString();
                for (int frame = 30; frame <= 720; frame += 30)
                {
                    run.AdvanceTo(frame * 10f); run.Pump(true);
                    for (int i = frame - 29; i <= frame; i++)
                    {
                        var f = run.timeline.Get(i); if (f == null) continue;
                        peakW = Mathf.Max(peakW, f.wMax); peakZ1 = Mathf.Max(peakZ1, f.meso1.zeta); peakZ4 = Mathf.Max(peakZ4, f.meso4.zeta);
                        if (f.t >= 1200) peakZ1Late = Mathf.Max(peakZ1Late, f.meso1.zeta);
                        coldest = Mathf.Min(coldest, f.coldPoolMin);
                    }
                    var fr = run.timeline.Get(frame);
                    csv.AppendLine(Row(fr));
                    if (fr.t >= 1800) motions.Add(fr.stormMotion);
                    if (float.IsNaN(fr.wMax)) return "FAIL NaN in the run at " + fr.t;
                }
                motion = MotionSummary(motions, new Vector2((float)rm.x, (float)rm.y));
                tail.AppendLine("## Weisman-Klemp supercell, average tier");
                tail.AppendLine();
                tail.AppendLine($"{run.g}; dt {run.p.dt} s; frame motion {rm.x:0.0}, {rm.y:0.0} m/s; {plan}; {wall.Elapsed.TotalSeconds:0} s wall for 2 h.");
                tail.AppendLine();
                tail.AppendLine(CsvToTable(csv.ToString()));
            }
            File.WriteAllText(Path.Combine(OutDir, "weisman_klemp.csv"), csv.ToString());
            bool ok = peakW >= 50 && peakW <= 70 && peakZ1Late >= 0.01f && peakZ1Late <= 0.04f;
            return Verdict(ok, $"peak updraft {peakW:0.0} m/s (50-70); peak 1 km meso vorticity after 20 min {peakZ1Late:0.0000} 1/s (0.01-0.04), 4 km {peakZ4:0.0000} 1/s; coldest cold pool {coldest:0.0} K; {motion}");
        }

        static string MotionSummary(List<Vector2> m, Vector2 bunkers)
        {
            if (m.Count == 0) return "no motion estimate";
            var us = m.Select(v => v.x).OrderBy(x => x).ToList(); var vs = m.Select(v => v.y).OrderBy(x => x).ToList();
            var med = new Vector2(us[us.Count / 2], vs[vs.Count / 2]);
            int near = m.Count(v => (v - med).magnitude <= 10 * (float)C.KT);
            Func<Vector2, string> fmt = v => { double from = (Math.Atan2(-v.x, -v.y) * 180 / Math.PI + 360) % 360; return $"from {from:0} at {v.magnitude / C.KT:0} kt"; };
            return $"storm motion after 30 min: median {fmt(med)} (Bunkers {fmt(bunkers)}), {near} of {m.Count} samples within 10 kt of the median";
        }

        static string Row(TimelineFrame f)
        {
            var mv = f.stormMotion; double spd = mv.magnitude / C.KT; double from = (Math.Atan2(-mv.x, -mv.y) * 180 / Math.PI + 360) % 360;
            return string.Join(",", new[]
            {
                F(f.t / 60, "0"), F(f.wMax, "0.0"), F(f.updraftPos.x / 1000, "0.0"), F(f.updraftPos.y / 1000, "0.0"), F(f.updraftZ, "0"), F(f.meso1.zeta, "0.0000"), F(f.meso1.zetaPeak, "0.0000"), F(f.meso4.zeta, "0.0000"),
                F(f.mesoDepth, "0"), F(f.coldPoolMin, "0.0"), F(f.maxDbz, "0"), F(f.echoTop18, "0"), F(f.maxHailMm, "0"), F(from, "0"), F(spd, "0")
            });
        }

        static string CsvToTable(string csv)
        {
            var lines = csv.Trim().Split('\n'); var sb = new StringBuilder();
            sb.AppendLine("| " + string.Join(" | ", lines[0].Split(',')) + " |");
            sb.AppendLine("|" + string.Concat(Enumerable.Repeat(" --- |", lines[0].Split(',').Length)));
            foreach (var l in lines.Skip(1)) sb.AppendLine("| " + string.Join(" | ", l.Trim().Split(',')) + " |");
            return sb.ToString();
        }

        static string DodgeCity(StringBuilder tail)
        {
            string path = Path.Combine(RepoRoot, "data", "raw", "soundings", "ddc_20160524_hrrr.json");
            var json = File.ReadAllText(path);
            var s = new RunSetup { tier = SimTier.Average, runLength = 7200 };
            var csv = new StringBuilder("t_min,wmax_ms,updraft_x_km,updraft_y_km,updraft_z_m,meso1_zeta,meso1_zeta_peak,meso4_zeta,meso_depth_m,coldpool_K,dbz_max,echo_top18_m,hail_mm,storm_from_deg,storm_kt\n");
            float peakW = 0, peakZ1 = 0, coldest = 0; bool nan = false;
            var motions = new List<Vector2>(); string motion;
            ddcRadar.Clear();
            var wall = System.Diagnostics.Stopwatch.StartNew();
            using (var run = StormRun.FromSoundingJson(json, s))
            {
                var E = run.env;
                double bunkFrom = (Math.Atan2(-E.bk.rm.x, -E.bk.rm.y) * 180 / Math.PI + 360) % 360, bunkKt = Math.Sqrt(E.bk.rm.x * E.bk.rm.x + E.bk.rm.y * E.bk.rm.y) / C.KT;
                for (int frame = 30; frame <= 720; frame += 30)
                {
                    run.AdvanceTo(frame * 10f); run.Pump(true);
                    for (int i = frame - 29; i <= frame; i++)
                    {
                        var f = run.timeline.Get(i); if (f == null) continue;
                        peakW = Mathf.Max(peakW, f.wMax); peakZ1 = Mathf.Max(peakZ1, f.meso1.zeta); coldest = Mathf.Min(coldest, f.coldPoolMin);
                        if (float.IsNaN(f.wMax)) nan = true;
                    }
                    var fr = run.timeline.Get(frame);
                    csv.AppendLine(Row(fr));
                    if (fr.t >= 1800) { motions.Add(fr.stormMotion); ddcRadar.Add(RadarLook.Measure(run.model, fr.t)); }
                    Log($"DDC {fr.t / 60:0} min: w {fr.wMax:0.0} m/s, meso 1 km {fr.meso1.zeta:0.0000} 4 km {fr.meso4.zeta:0.0000} 1/s, cold pool {fr.coldPoolMin:0.0} K");
                }
                motion = MotionSummary(motions, new Vector2((float)E.bk.rm.x, (float)E.bk.rm.y));
                tail.AppendLine("## Dodge City 2016-05-24, HRRR 22Z sounding, average tier");
                tail.AppendLine();
                tail.AppendLine($"{run.g}; dt {run.p.dt} s; MLCAPE {E.pc.cape:0} J/kg, MLCIN {E.pc.cin:0} J/kg, 0-1/0-3 km SRH {E.srh1:0}/{E.srh3:0} m2/s2, 0-6 km shear {E.shear6 / C.KT:0} kt; " +
                                $"grid moves with the Bunkers right-mover, from {bunkFrom:0} at {bunkKt:0} kt; {run.plan}; {wall.Elapsed.TotalSeconds:0} s wall for 2 h; " +
                                $"timeline {run.timeline.Bytes / 1e6:0} MB ({run.timeline.Bytes / 1e3 / (run.timeline.Latest + 1):0} KB per frame), checkpoints {run.checkpoints.Bytes / 1e6:0} MB ({run.checkpoints.All.Count}).");
                tail.AppendLine();
                tail.AppendLine("Every 5 minutes: peak updraft, its height, mesocyclone vorticity at 1 km (1 km block mean, and grid-point peak) and 4 km, mesocyclone depth, coldest surface theta' in the cold pool, max reflectivity, 18 dBZ echo top, largest hail at the ground, and the storm's ground-relative motion (from the drift of the 5 km updraft over the frame).");
                tail.AppendLine();
                tail.AppendLine(CsvToTable(csv.ToString()));
            }
            File.WriteAllText(Path.Combine(OutDir, "ddc_20160524_hrrr.csv"), csv.ToString());
            bool ok = !nan && peakW > 25 && peakW <= 110 && peakZ1 <= 0.08f && coldest >= -15f;
            return Verdict(ok, $"a storm forms and runs 2 h: peak updraft {peakW:0.0} m/s (25-110; parcel theory gives {Math.Sqrt(2 * 5098):0}), peak 1 km meso {peakZ1:0.0000} 1/s (at most 0.08), coldest cold pool {coldest:0.0} K (no colder than -15), no NaN: {(!nan ? "yes" : "no")}; {motion}");
        }

        // ------------------------------------------------------------ Dodge City against the KDDC radar
        // The model storm's radar volumes every 5 minutes from 30 min (RadarLook, measured as tools/fetch_nexrad.py
        // measures NEXRAD), against the two KDDC volumes in data/derived/radar. The model starts from the 22Z HRRR
        // sounding with the storm already firing, so its clock is not the real storm's: the comparison uses the mature
        // storm (60-120 min) rather than a matched time.
        static readonly List<RadarVolume> ddcRadar = new List<RadarVolume>();

        static string RadarCompare(StringBuilder tail)
        {
            if (ddcRadar.Count == 0) return "FAIL no model volumes (the Dodge City run did not finish)";
            string dir = Path.Combine(RepoRoot, "data", "derived", "radar");
            var o1 = RadarLook.FromJson(File.ReadAllText(Path.Combine(dir, "ddc_20160524_KDDC_2248.json")));
            var o2 = RadarLook.FromJson(File.ReadAllText(Path.Combine(dir, "ddc_20160524_KDDC_2335.json")));
            var mature = ddcRadar.Where(v => v.t >= 3600).ToList();
            Func<Func<RadarVolume, float>, float[]> St = sel =>
            {
                var xs = mature.Select(sel).Where(x => !float.IsNaN(x)).OrderBy(x => x).ToList();
                return xs.Count == 0 ? new[] { float.NaN, float.NaN, float.NaN } : new[] { xs[xs.Count / 2], xs[0], xs[xs.Count - 1] };
            };
            var rows = new StringBuilder();
            var misses = new List<string>(); var biases = new List<string>(); bool allOk = true;
            Func<float, string> fKm = x => float.IsNaN(x) ? "none" : (x / 1000f).ToString("0.0", Inv);
            Func<float, string> fA = x => float.IsNaN(x) ? "?" : x.ToString("0", Inv);
            Func<float, string> fDb = x => float.IsNaN(x) ? "none" : x.ToString("0.0", Inv);
            Action<string, string, string, float[], Func<float, string>, string, bool?> Row = (name, a, b, st, fmt, tol, ok) =>
            {
                bool bias = tol.EndsWith("*");
                string mark = ok == null ? "not tested" : ok.Value ? "yes" : bias ? "**no**, known bias (not gated)" : "**no**";
                rows.AppendLine($"| {name} | {a} | {b} | {fmt(st[0])} | {fmt(st[1])} to {fmt(st[2])} | {tol} | {mark} |");
                if (ok != null && !ok.Value) { if (bias) biases.Add(name); else { allOk = false; misses.Add(name); } }
            };

            var s18 = St(v => v.top18); var s30 = St(v => v.top30); var s50 = St(v => v.top50);
            var s40a1 = St(v => v.area1[0]); var s50a1 = St(v => v.area1[1]); var s55a1 = St(v => v.area1[2]); var s60a1 = St(v => v.area1[3]);
            var s40a4 = St(v => v.area4[0]); var s50a4 = St(v => v.area4[1]); var s40a7 = St(v => v.area7[0]); var s50a7 = St(v => v.area7[1]);
            var sMax = St(v => v.maxDbz); var sMaxZ = St(v => v.maxDbzZ);
            var withVault = mature.Where(v => v.vault).ToList();
            float vaultFrac = mature.Count > 0 ? withVault.Count / (float)mature.Count : 0;
            var sVb = St(v => v.vault ? v.vaultBase : float.NaN); var sVt = St(v => v.vault ? v.vaultTop : float.NaN);
            var sVd = St(v => v.vault ? v.vaultDepth : float.NaN); var sVc = St(v => v.vault ? v.vaultCap : float.NaN);

            // tolerances: tops within 2 / 2.5 km of the 22:48Z volume (the 23:35Z tops are cut off by the cone of silence);
            // low-level core areas from half the smaller to twice the larger of the two volumes (one storm, two moments)
            float lo40 = 0.5f * Mathf.Min(o1.area1[0], o2.area1[0]), hi40 = 2f * Mathf.Max(o1.area1[0], o2.area1[0]);
            float lo50 = 0.5f * Mathf.Min(o1.area1[1], o2.area1[1]), hi50 = 2f * Mathf.Max(o1.area1[1], o2.area1[1]);
            const string cone = " (cone of silence)";
            Row("18 dBZ echo top, km", fKm(o1.top18), fKm(o2.top18) + cone, s18, fKm, "15.5 +/- 2", Mathf.Abs(s18[0] - o1.top18) <= 2000f);
            Row("30 dBZ echo top, km", fKm(o1.top30), fKm(o2.top30) + cone, s30, fKm, "", null);
            Row("50 dBZ echo top, km", fKm(o1.top50), fKm(o2.top50) + cone, s50, fKm, "9.5 +/- 2.5*", Mathf.Abs(s50[0] - o1.top50) <= 2500f);
            Row("40 dBZ area at 1 km, km2", fA(o1.area1[0]), fA(o2.area1[0]), s40a1, fA, $"{lo40:0}-{hi40:0}", s40a1[0] >= lo40 && s40a1[0] <= hi40);
            Row("50 dBZ core at 1 km, km2", fA(o1.area1[1]), fA(o2.area1[1]), s50a1, fA, $"{lo50:0}-{hi50:0}", s50a1[0] >= lo50 && s50a1[0] <= hi50);
            Row("55 dBZ core at 1 km, km2", fA(o1.area1[2]), fA(o2.area1[2]), s55a1, fA, "", null);
            Row("60 dBZ core at 1 km, km2", fA(o1.area1[3]), fA(o2.area1[3]), s60a1, fA, "", null);
            Row("40 dBZ area at 4 km, km2", fA(o1.area4[0]), fA(o2.area4[0]), s40a4, fA, "", null);
            Row("50 dBZ core at 4 km, km2", fA(o1.area4[1]), fA(o2.area4[1]), s50a4, fA, "", null);
            Row("40 dBZ area at 7 km, km2", fA(o1.area7[0]), fA(o2.area7[0]) + cone, s40a7, fA, "", null);
            Row("50 dBZ core at 7 km, km2", fA(o1.area7[1]), fA(o2.area7[1]) + cone, s50a7, fA, "", null);
            Row("Strongest echo, dBZ", fDb(o1.maxDbz), fDb(o2.maxDbz), sMax, fDb, "", null);
            Row("Height of the strongest echo, km", fKm(o1.maxDbzZ), fKm(o2.maxDbzZ), sMaxZ, fKm, "", null);
            rows.AppendLine($"| Vault (bounded weak echo) found | yes | yes{cone} | {withVault.Count} of {mature.Count} volumes | | at least half | {(vaultFrac >= 0.5f ? "yes" : "**no**")} |");
            if (vaultFrac < 0.5f) { allOk = false; misses.Add("vault found"); }
            Row("Vault base, km", fKm(o1.vaultBase), fKm(o2.vaultBase), sVb, fKm, "", null);
            Row("Vault top, km", fKm(o1.vaultTop), fKm(o2.vaultTop), sVt, fKm, "", null);
            Row("Vault depth, km", fKm(o1.vaultDepth), fKm(o2.vaultDepth), sVd, fKm, "1.0-4.5*", withVault.Count > 0 && sVd[0] >= 1000f && sVd[0] <= 4500f);
            Row("Echo capping the vault, dBZ", fDb(o1.vaultCap), fDb(o2.vaultCap), sVc, fDb, "", null);

            var csv = new StringBuilder("t_min,max_dbz,max_dbz_z_m,top18_m,top30_m,top50_m,a40_1km,a50_1km,a55_1km,a60_1km,a40_4km,a50_4km,a40_7km,a50_7km,vault_base_m,vault_top_m,vault_cap_dbz\n");
            foreach (var v in ddcRadar)
                csv.AppendLine(string.Join(",", new[] { F(v.t / 60, "0"), F(v.maxDbz, "0.0"), F(v.maxDbzZ, "0"), F(v.top18, "0"), F(v.top30, "0"), F(v.top50, "0"),
                    F(v.area1[0], "0"), F(v.area1[1], "0"), F(v.area1[2], "0"), F(v.area1[3], "0"), F(v.area4[0], "0"), F(v.area4[1], "0"), F(v.area7[0], "0"), F(v.area7[1], "0"),
                    v.vault ? F(v.vaultBase, "0") : "", v.vault ? F(v.vaultTop, "0") : "", v.vault ? F(v.vaultCap, "0.0") : "" }));
            File.WriteAllText(Path.Combine(OutDir, "ddc_20160524_radar.csv"), csv.ToString());

            tail.AppendLine("## Dodge City 2016-05-24: model storm against the KDDC radar");
            tail.AppendLine();
            tail.AppendLine("The model's rain, snow and hail are turned into reflectivity with the model's own formula, averaged onto the radar tool's 1 km x 1 km x 500 m grid in a 60 x 60 km box around the strongest echo at 500 m, " +
                            "and measured with the same rules as `tools/fetch_nexrad.py` (echo tops, core areas, vault search). " +
                            $"Model: every 5 minutes from 60 to 120 min ({mature.Count} volumes), the mature storm. The model's clock starts at the 22Z sounding with the storm already firing, so it is not matched to a real time. " +
                            "At 22:48Z the storm was 30 km south-west of KDDC, a good view. At 23:35Z it sat on the radar, so only that volume's low-level numbers are good.");
            tail.AppendLine();
            tail.AppendLine("| Quantity | KDDC 22:48Z | KDDC 23:35Z | Model median | Model range | Tolerance | Within |");
            tail.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            tail.Append(rows);
            tail.AppendLine();
            tail.AppendLine("Every 5 minutes from 30 min (also in `ddc_20160524_radar.csv`):");
            tail.AppendLine();
            tail.AppendLine(CsvToTable(csv.ToString()));
            string sum = $"mature storm (60-120 min, {mature.Count} volumes), model median against KDDC 22:48Z: 18 dBZ top {fKm(s18[0])} km (15.5), 50 dBZ top {fKm(s50[0])} km (9.5), 40 dBZ area at 1 km {fA(s40a1[0])} km2 (226; 467 at 23:35Z), " +
                         $"50 dBZ core at 1 km {fA(s50a1[0])} km2 (27; 142), vault in {withVault.Count} of {mature.Count} volumes, median {fKm(sVb[0])}-{fKm(sVt[0])} km (2.5-5.0), strongest echo {fDb(sMax[0])} dBZ (55.1; not tested)";
            if (biases.Count > 0) sum += "; known biases outside the radar values (reported, not gated): " + string.Join(", ", biases);
            return Verdict(allOk, sum + (allOk ? "" : "; outside tolerance: " + string.Join(", ", misses)));
        }

        // ------------------------------------------------------------ sensitivity runs (not part of "all")
        struct CaseStats { public float peakW, peakWLate, peakZ1, coldest, coldest1km, coldMedLate, wEnd; public bool nan; public string series; }

        static CaseStats RunCase(StormRun run, float tEnd)
        {
            var cs = new CaseStats(); var ser = new StringBuilder(); var late = new List<float>();
            using (run)
            {
                for (int frame = 30; frame * 10f <= tEnd + 1e-3f; frame += 30)
                {
                    run.AdvanceTo(frame * 10f); run.Pump(true);
                    for (int i = frame - 29; i <= frame; i++)
                    {
                        var f = run.timeline.Get(i); if (f == null) continue;
                        if (float.IsNaN(f.wMax)) cs.nan = true;
                        cs.peakW = Mathf.Max(cs.peakW, f.wMax); if (f.t >= 1200) cs.peakWLate = Mathf.Max(cs.peakWLate, f.wMax);
                        if (f.t >= 1200) cs.peakZ1 = Mathf.Max(cs.peakZ1, f.meso1.zeta);
                        cs.coldest = Mathf.Min(cs.coldest, f.coldPoolMin); cs.coldest1km = Mathf.Min(cs.coldest1km, f.coldPoolMin1km);
                        if (f.t >= 3600) late.Add(f.coldPoolMin);
                    }
                    var fr = run.timeline.Get(frame);
                    cs.wEnd = fr.wMax;
                    if (frame % 90 == 0) ser.Append($"{fr.t / 60:0}:{fr.wMax:0}/{fr.coldPoolMin:0.0} ");
                }
            }
            cs.series = ser.ToString();
            late.Sort(); cs.coldMedLate = late.Count > 0 ? late[late.Count / 2] : 0;
            return cs;
        }

        static string Tune(StringBuilder tail)
        {
            var sb = new StringBuilder("## Sensitivity: sub-grid mixing and microphysics (average tier, 2 h)\n\nDiagnostic switches (never used in real runs): 1 no rain evaporation, 2 no latent cooling from hail melting, 4 no hail sublimation, 8 snow melting with LFO's vapour term, 16 no latent cooling from snow melting.\n\n| Case | Smagorinsky cs | n0r for evaporation | n0r for fall speed | Switches | Hail wet growth | Melting-hail vapour exchange | Peak w | Peak w after 20 min | Peak 1 km meso after 20 min | Coldest cold pool (grid point / 1 km mean) | Median coldest, 60-120 min | w at 2 h | Every 15 min (min:w/cold pool) |\n| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |\n");
            var wk = new WeismanKlempProfile(0.014, 30);
            wk.ToEnvironmentGrid(out var sd, out var wd);
            var rm = wk.BunkersRightMover();
            string json = File.ReadAllText(Path.Combine(RepoRoot, "data", "raw", "soundings", "ddc_20160524_hrrr.json"));
            var args = System.Environment.GetCommandLineArgs();
            string cfg = "off:8e6,on:8e6,on:2e6,on:1e6";
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dynTune") cfg = args[i + 1];
            foreach (var item in cfg.Split(','))
            {
                var parts = item.Split(':'); bool smag = parts[0] == "on"; float n0 = float.Parse(parts[1], Inv);
                float csm = parts.Length > 2 ? float.Parse(parts[2], Inv) : 0.25f;
                float n0f = parts.Length > 3 ? float.Parse(parts[3], Inv) : -1f;
                int diag = parts.Length > 4 ? int.Parse(parts[4], Inv) : 0;
                int wet = parts.Length > 5 ? int.Parse(parts[5], Inv) : -1;
                int mvap = parts.Length > 6 ? int.Parse(parts[6], Inv) : -1;
                string cases = "WK,DDC";
                for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dynTuneCases") cases = args[i + 1];
                foreach (var name in cases.Split(','))
                {
                    var g = AverageGrid(); var p = SimParams.ForGrid(g); p.smagorinsky = smag; p.n0rEvap = n0; p.smagCs = csm; if (n0f >= 0) p.n0rFall = n0f; p.microDiag = diag; if (wet >= 0) p.hailWetGrowth = wet == 1; if (mvap >= 0) p.hailMeltVapour = mvap == 1;
                    var setup = new RunSetup { tier = SimTier.Average, runLength = 7200 };
                    var run = name == "WK" ? StormRun.FromProfile(wk, sd, wd, new Vector2((float)rm.x, (float)rm.y), setup, g, p)
                                           : StormRun.FromSoundingJson(json, setup, g, p);
                    var c = RunCase(run, 7200);
                    GC.Collect(); GC.WaitForPendingFinalizers();   // a run holds about 1 GB of frames and checkpoints
                    string line = $"| {name} | {(smag ? csm.ToString("0.00", Inv) : "off")} | {n0:0.#E+0} | {(p.n0rFall > 0 ? p.n0rFall.ToString("0.#E+0", Inv) : "8E+6")} | {diag} | {(p.hailWetGrowth ? "on" : "off")} | {(p.hailMeltVapour ? "on" : "off")} | {c.peakW:0.0} | {c.peakWLate:0.0} | {c.peakZ1:0.0000} | {c.coldest:0.0} / {c.coldest1km:0.0} | {c.coldMedLate:0.0} | {c.wEnd:0.0} | {c.series}{(c.nan ? " NaN" : "")} |";
                    sb.AppendLine(line); Log("tune " + line);
                }
            }
            tail.Append(sb); tail.AppendLine();
            File.WriteAllText(Path.Combine(OutDir, "sensitivity.md"), sb.ToString());
            return "see sensitivity.md";
        }

        static string Perf(StringBuilder tail)
        {
            var sb = new StringBuilder();
            sb.AppendLine("## Step time on this PC");
            sb.AppendLine();
            sb.AppendLine($"GPU {SystemInfo.graphicsDeviceName}, {SystemInfo.graphicsDeviceType}. WK supercell environment, timed after 10 min of storm (forced updraft active).");
            sb.AppendLine();
            sb.AppendLine("| Tier | Grid | dt | Step (no recording) | Step (with timeline) | Real time costs | Max speed at 50 % GPU | VRAM | Checkpoint |");
            sb.AppendLine("| --- | --- | --- | --- | --- | --- | --- | --- | --- |");
            var summary = new List<string>();
            foreach (var tier in new[] { SimTier.Average, SimTier.High })
            {
                var wk = new WeismanKlempProfile(0.014, 30);
                wk.ToEnvironmentGrid(out var sd, out var wd);
                var rm = wk.BunkersRightMover();
                var s = new RunSetup { tier = tier, runLength = 7200 };
                var gt = tier == SimTier.Average ? AverageGrid() : null;
                using (var run = StormRun.FromProfile(wk, sd, wd, new Vector2((float)rm.x, (float)rm.y), s, gt))
                {
                    run.AdvanceTo(600); run.Pump(true); run.model.Sync();
                    int n = 60;
                    run.recording = false; run.checkpointing = false;
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    run.Advance(n); run.model.Sync();
                    double raw = sw.Elapsed.TotalMilliseconds / n;
                    run.recording = true;
                    sw.Restart();
                    run.Advance(n); run.Pump(true); run.model.Sync();
                    double rec = sw.Elapsed.TotalMilliseconds / n;
                    run.checkpointing = true;
                    var ck = run.model.QuantiseAndCapture(); UnityEngine.Rendering.AsyncGPUReadback.WaitAllRequests();
                    long ckBytes = 0; foreach (var r in ck) ckBytes += r.layerDataSize;
                    ckBytes += 2L * run.g.Plane * 4;
                    double rtCost = rec / run.p.dt;                        // ms of GPU per sim-second
                    // at 60 fps with a 4 ms per-frame budget (half of the 8 ms storm budget): steps per second = 60 * 4 / rec
                    double speedHalf = 60.0 * 4.0 / rec * run.p.dt;
                    sb.AppendLine($"| {tier} | {run.g.NX}x{run.g.NY}x{run.g.NZ}, {run.g.dx:0} m, dz {run.g.dzMin:0}-{run.g.dzMax:0} m | {run.p.dt:0.###} s | {raw:0.0} ms | {rec:0.0} ms | {rtCost:0.0} ms GPU per sim-second | {speedHalf:0.0}x real time (4 ms/frame at 60 fps) | {run.model.VramBytes / 1e6:0} MB | {ckBytes / 1e6:0} MB |");
                    summary.Add($"{tier} {raw:0.0}/{rec:0.0} ms per step, {speedHalf:0.0}x at 4 ms/frame");
                }
            }
            tail.Append(sb); tail.AppendLine();
            File.WriteAllText(Path.Combine(OutDir, "step_times.md"), sb.ToString());
            return string.Join("; ", summary);
        }
    }
}
