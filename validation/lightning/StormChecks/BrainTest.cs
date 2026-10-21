// Feeds BrainLightning hand-built timeline frames on a real grid and base state, with an independently computed answer.
using System; using System.Collections.Generic; using System.Linq;
using StormLab.Supercell; using StormLab.Supercell.Dynamics; using StormLab.Supercell.Lightning;
using UnityEngine;

static class BrainTest
{
    static int checks;
    static void Check(bool ok, string what) { checks++; if (!ok) throw new Exception("FAIL " + what); Console.WriteLine("PASS " + what); }

    const int N = 60, NL = 19, W = 40, I0 = 8, J0 = 12;
    static SimGrid g; static BaseState b; static Timeline tl;

    // Core of radius r blocks: w 40 m/s column max, qh 3 g/kg at 3-10 km; cloud ice 0.5 g/kg at 8-12 km over radius 2r;
    // 40 dBZ at 1 km inside the core once rain has started.
    static TimelineFrame Frame(int index, int r, bool rain)
    {
        var f = new TimelineFrame { index = index, t = index * 10, valid = true, winI0 = I0, winJ0 = J0, winNX = W, winNY = W, wMax = r > 0 ? 40 : 0 };
        f.wColumn = new float[g.NZ + 1];
        for (int k = 0; k <= g.NZ; k++) f.wColumn[k] = r > 0 ? 40 * (float)Math.Sin(Math.PI * Math.Min(1, g.zw[k] / 16000)) : 0;
        f.map2D = new byte[Map2D.Count * N * N]; f.q3D = new byte[Q3D.Count * NL * W * W];
        for (int j = 0; j < W; j++) for (int i = 0; i < W; i++)
        {
            double d = Math.Sqrt((i - 20) * (i - 20) + (j - 20) * (j - 20));
            int c = (J0 + j) * N + I0 + i;
            bool core = d < r, anvil = d < 2 * r;
            f.map2D[(int)Map2D.Field.WMax * N * N + c] = Map2D.Encode(Map2D.Field.WMax, core ? 40 : 0);
            f.map2D[(int)Map2D.Field.Dbz1km * N * N + c] = Map2D.Encode(Map2D.Field.Dbz1km, core && rain ? 50 : 0);
            for (int L = 0; L < NL; L++)
            {
                f.q3D[(((int)Q3D.Field.Qh * NL + L) * W + j) * W + i] = Q3D.Encode(core && L >= 3 && L <= 10 ? 3e-3f : 0);
                f.q3D[(((int)Q3D.Field.Qi * NL + L) * W + j) * W + i] = Q3D.Encode(anvil && L >= 8 && L <= 12 ? 5e-4f : 0);
            }
        }
        return f;
    }

    // Independent expected rate with default tuning: McCaul F1 and F2 per km² per 5 min, summed, per minute.
    static double Expected(int r)
    {
        if (r == 0) return 0;
        float z15 = 0;
        for (int k = 1; k < g.NZ; k++)
        {
            float t0 = b.th0[k - 1] * b.pi0[k - 1], t1 = b.th0[k] * b.pi0[k];
            if (t0 > 258.15f && t1 <= 258.15f) { z15 = g.zc[k - 1] + (g.zc[k] - g.zc[k - 1]) * (t0 - 258.15f) / (t0 - t1); break; }
        }
        double shape = Math.Sin(Math.PI * z15 / 16000);   // core w(z15) / max of the sin profile
        double w15 = Map2D.Decode(Map2D.Field.WMax, Map2D.Encode(Map2D.Field.WMax, 40)) * shape;
        double qh = Q3D.Decode(Q3D.Encode(3e-3f)) * 1000, qi = Q3D.Decode(Q3D.Encode(5e-4f));
        int core = 0, anvil = 0;
        for (int j = 0; j < W; j++) for (int i = 0; i < W; i++)
        { double d = Math.Sqrt((i - 20) * (i - 20) + (j - 20) * (j - 20)); if (d < r) core++; if (d < 2 * r) anvil++; }
        double rho(double z) { for (int k = 1; k < g.NZ; k++) if (z <= g.zc[k]) return b.rho0[k - 1] + (b.rho0[k] - b.rho0[k - 1]) * (z - g.zc[k - 1]) / (g.zc[k] - g.zc[k - 1]); return b.rho0[g.NZ - 1]; }
        double hailColumn = 0, iceColumn = 0;
        for (int L = 3; L <= 10; L++) hailColumn += rho(L * 1000) * qh / 1000 * 1000;
        for (int L = 8; L <= 12; L++) iceColumn += rho(L * 1000) * qi * 1000;
        double f1 = .042 * core * w15 * qh, f2 = .2 * (core * hailColumn + anvil * iceColumn);
        Console.WriteLine($"  z15 {z15:0} m, w15 {w15:0.0} m/s, core {core} km², F1 {f1 / 5:0.0}/min, F2 {f2 / 5:0.0}/min");
        return (.95 * f1 + .05 * f2) / 5;
    }

    public static void Run()
    {
        g = SimGrid.Make(N, N, 60, 1000);
        var env = StormLab.Supercell.Environment.Make(StormPreset.All.First(p => p.key == "plains").ToEnv());
        b = BaseState.FromEnvironment(g, env);
        tl = new Timeline(g, N, N, NL, 1000, Vector2.zero, capacity: 12);
        Check(tl.NL == NL, "test grid matches the model's 19 layers");
        var run = new StormRun(g, b, tl);
        // Grow to r 6 by frame 30, hold, collapse to 0 by frame 80; rain from frame 20.
        int R(int i) => i < 30 ? i / 5 : i < 50 ? 6 : Math.Max(0, 6 - (i - 50) / 5);
        for (int i = 0; i < 10; i++) tl.Store(Frame(i, R(i), i >= 20));
        // Attached mid-run: backfills what the ring still holds (all 10 here).
        var brain = new BrainLightning(run, ClimoRegion.Plains, new StormLightningTuning());
        for (int i = 10; i <= 80; i++) tl.Store(Frame(i, R(i), i >= 20));
        Check(tl.Get(5) == null, "ring buffer has evicted early frames");

        double want = Expected(6), got = brain.At(400).flashesPerMinute;
        Check(Math.Abs(got - want) < 1e-3 * want, $"mature core rate {got:0.00}/min matches the hand calculation {want:0.00}");
        Check(brain.At(450).flashesPerMinute == brain.At(400).flashesPerMinute, "steady core gives a steady rate");
        Check(Math.Abs(brain.At(15).flashesPerMinute - (float)(.5 * Expected(0) + .5 * Expected(0))) < 1e-4 && brain.At(55).flashesPerMinute > 0,
            "evicted early frames still answer from their summaries");
        double a = brain.At(100).flashesPerMinute, c = brain.At(110).flashesPerMinute, mid = brain.At(105).flashesPerMinute;
        Check(Math.Abs(mid - (a + c) / 2) < 1e-3 * Math.Max(1, c), "blends linearly between frames");
        Check(brain.At(800).flashesPerMinute == 0, "no core, no lightning");
        Check(brain.At(5000).flashesPerMinute == brain.At(800).flashesPerMinute, "past the last frame it holds the last one");

        var early = brain.At(150); var mature = brain.At(400); var decaying = brain.At(640);
        Check(early.groundFraction < mature.groundFraction, $"ground share rises once rain reaches the ground ({early.groundFraction:0.000} -> {mature.groundFraction:0.000})");
        Check(decaying.positiveGroundFraction > mature.positiveGroundFraction, $"positive share rises as the core collapses ({mature.positiveGroundFraction:0.000} -> {decaying.positiveGroundFraction:0.000})");

        // Replaying a frame with different content changes it; restoring it restores the answer and the running peak.
        double before = brain.At(640).positiveGroundFraction;
        tl.Store(Frame(40, 10, true));
        Check(brain.At(400).flashesPerMinute > got, "a replayed frame replaces its summary");
        tl.Store(Frame(40, 6, true));
        Check(brain.At(400).flashesPerMinute == got && brain.At(640).positiveGroundFraction == before, "restoring it restores rate and peak");

        // The schedule with the brain adapter is frame-rate independent.
        var settings = new LightningSettings();
        var whole = new List<FlashEvent>(); var part = new List<FlashEvent>(); var split = new List<FlashEvent>();
        LightningSchedule.Query(settings, 0, 800, whole, brain.At);
        for (double t = 0; t < 800; t += .37) { LightningSchedule.Query(settings, t, Math.Min(800, t + .37), part, brain.At); split.AddRange(part); }
        Check(whole.Count > 0 && whole.Select(e => e.id).SequenceEqual(split.Select(e => e.id)), $"brain schedule replays across frame partitions ({whole.Count} flashes)");

        brain.Dispose(); tl.Store(Frame(81, 6, true));
        Check(brain.At(810).flashesPerMinute == brain.At(800).flashesPerMinute, "disposed adapter stops listening");
        Console.WriteLine($"PASS: {checks} brain lightning checks");
    }
}
