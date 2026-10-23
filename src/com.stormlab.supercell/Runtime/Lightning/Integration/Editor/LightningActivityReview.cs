using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using StormLab.Supercell.Designer;
using UnityEditor;
using UnityEngine;

namespace StormLab.Supercell.Lightning.EditorTools
{
    /// <summary>Samples storm-driven lightning over designed storms' lives: CSV curves and shape checks. No GPU needed.</summary>
    public static class LightningActivityReview
    {
        [MenuItem("Supercell Lab/Lightning/Check Storm-Driven Activity")]
        public static void Run()
        {
            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../validation/lightning/out/activity"));
            Directory.CreateDirectory(folder);
            var results = new List<string>();
            try
            {
                var k = new StormLightningTuning();
                foreach (int seed in new[] { 1, 2, 3, 4 }) Supercell(folder, "plains", ClimoRegion.Plains, seed, k, results);
                Supercell(folder, "dixie", ClimoRegion.Dixie, 1, k, results);
                Supercell(folder, "lp", ClimoRegion.Plains, 1, k, results);
                StrengthAndCrawlers(k, results);
                Pulse(folder, k, results);
                File.WriteAllLines(Path.Combine(folder, "checks.txt"), results);
                Debug.Log("PASS: storm-driven lightning activity. " + string.Join("; ", results) + " CSVs: " + folder);
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(folder, "failure.txt"), e.ToString()); throw; }
        }

        static DesignedStorm Make(string preset, int seed, ClimoRegion region = ClimoRegion.Plains) =>
            new DesignedStorm(new FixedEnvironment(Environment.Make(StormPreset.All.First(p => p.key == preset).ToEnv())),
                new Vec2(0, 0), seed, region);

        static void Supercell(string folder, string preset, ClimoRegion region, int seed, StormLightningTuning k, List<string> results)
        {
            var storm = Make(preset, seed, region);
            string name = $"{preset} seed {seed}";
            Require(storm.kind == StormKind.Supercell, $"{name} is a supercell", results);
            var curve = Sample(storm, k, Path.Combine(folder, $"designed_{preset}_{seed}.csv"));
            LightningActivity At(double t) => DesignedLightning.At(storm, t, k);
            var peak = curve.OrderByDescending(p => p.a.flashesPerMinute).First();
            Require(At(0).flashesPerMinute == 0, $"{name}: no lightning at initiation", results);
            // A plausible band for a supercell's total flash rate, which the 200/min ceiling clips only during the strongest
            // jumps, so the life cycle stays visible.
            Require(peak.a.flashesPerMinute > 15 && peak.a.flashesPerMinute < 220 && peak.t > 8 * 60,
                $"{name}: mature supercell peak {peak.a.flashesPerMinute:0} flashes/min at {peak.t / 60:0} min", results);
            var settingsCeiling = new LightningSettings().maxFlashesPerMinute;
            double clipped = curve.Count(p => p.a.flashesPerMinute >= settingsCeiling) / (double)curve.Count(p => p.a.flashesPerMinute > 0);
            Require(clipped < .15, $"{name}: at the {settingsCeiling:0}/min ceiling {clipped:0%} of its lightning life", results);
            Require(At(storm.lifeS).flashesPerMinute < .05f * peak.a.flashesPerMinute, $"{name}: lightning ends with the storm", results);
            Require(At(20 * 60).groundFraction > At(5 * 60).groundFraction, $"{name}: ground share rises once rain reaches the ground", results);
            double late = storm.lifeS - .4 * storm.genome.decayS;
            Require(At(late).positiveGroundFraction > At(peak.t).positiveGroundFraction, $"{name}: positive share rises as the storm decays", results);
            // Against the same storm without jumps, so cycle-to-cycle updraft changes cannot hide or fake one.
            var calm = new StormLightningTuning { jumpWallCloud = 0, jumpPreTornado = 0, tornadoLull = 0 };
            var wallOnly = new StormLightningTuning { jumpPreTornado = 0, tornadoLull = 0 };
            double K = DesignedStorm.CycleK;
            foreach (var c in storm.cycles)
            {
                // Other cycles' jumps overlap this one's when cycles crowd together; judge only the cycles that stand alone.
                bool crowded = storm.cycles.Any(o => o != c && o.wallS < Math.Max(c.endS, c.tornado?.EndS ?? 0) + 300 && Math.Max(o.endS, o.tornado?.EndS ?? 0) > c.wallS - 300);
                double wallUp = c.wallS + 420 * K;
                float wCalm = DesignedLightning.At(storm, wallUp, calm).flashesPerMinute, wFull = DesignedLightning.At(storm, wallUp, wallOnly).flashesPerMinute;
                if (wCalm < 1) { results.Add($"SKIP {name}: wall cloud at {c.wallS / 60:0} min comes before the lightning"); continue; }
                Require(wFull > 1.08f * wCalm, $"{name}: rate climbs as the wall cloud at {c.wallS / 60:0.0} min develops ({wCalm:0} -> {wFull:0}/min)", results);
                if (c.tornado == null) continue;
                double f = c.tornado.formS, L = c.tornado.lifeS;
                float jCalm = DesignedLightning.At(storm, f - 30, calm).flashesPerMinute;
                float jWall = DesignedLightning.At(storm, f - 30, wallOnly).flashesPerMinute;
                var jump = At(f - 30);
                Require(jump.flashesPerMinute > 1.25f * jWall && jump.flashesPerMinute > 1.4f * jCalm,
                    $"{name}: bigger jump just before the EF{c.tornado.ef} tornado at {f / 60:0.0} min ({jCalm:0} calm, {jWall:0} wall cloud, {jump.flashesPerMinute:0}/min)", results);
                Require(jump.reach > At(c.wallS + 60).reach && jump.reach > .5f, $"{name}: flashes reach furthest before touchdown (reach {jump.reach:0.00})", results);
                if (crowded) continue;
                var waning = At(f + .85 * L);
                Require(waning.flashesPerMinute < .75f * jump.flashesPerMinute && waning.reach < .5f * jump.reach,
                    $"{name}: activity tapers as the tornado goes on ({jump.flashesPerMinute:0} -> {waning.flashesPerMinute:0}/min)", results);
                float after = At(c.tornado.EndS + 300).flashesPerMinute, afterCalm = DesignedLightning.At(storm, c.tornado.EndS + 300, calm).flashesPerMinute;
                Require(after <= 1.01f * afterCalm + .01f, $"{name}: jump over 5 min after the tornado at {f / 60:0} min lifts", results);
            }
            // The real adapter must keep the schedule frame-rate independent.
            var settings = new LightningSettings { seed = seed };
            var whole = new List<FlashEvent>(); var part = new List<FlashEvent>(); var split = new List<FlashEvent>();
            Func<double, LightningActivity> activity = t => DesignedLightning.At(storm, t, k);
            LightningSchedule.Query(settings, peak.t - 300, peak.t + 300, whole, activity);
            for (double t = peak.t - 300; t < peak.t + 300; t += .7) { LightningSchedule.Query(settings, t, Math.Min(t + .7, peak.t + 300), part, activity); split.AddRange(part); }
            Require(whole.Count > 0 && whole.Select(e => e.id).SequenceEqual(split.Select(e => e.id)), $"{name}: designed schedule replays across frame partitions", results);
        }

        // Over many storms: stronger cycles jump harder, and crawlers come only with EF3+ tornadoes.
        static void StrengthAndCrawlers(StormLightningTuning k, List<string> results)
        {
            var calm = new StormLightningTuning { jumpWallCloud = 0, jumpPreTornado = 0, tornadoLull = 0 };
            var weak = new List<double>(); var strong = new List<double>(); int strongCycles = 0, withCrawlers = 0, weakCycles = 0, weakWithCrawlers = 0;
            for (int seed = 1; seed <= 80; seed++)
            {
                var storm = Make("plains", seed);
                foreach (var c in storm.cycles)
                {
                    var tg = c.tornado; if (tg == null) continue;
                    double f = tg.formS;
                    float without = DesignedLightning.At(storm, f - 30, calm).flashesPerMinute;
                    if (without < 1) continue;
                    double ratio = DesignedLightning.At(storm, f - 30, k).flashesPerMinute / without;
                    (tg.ef >= 3 ? strong : tg.ef <= 1 ? weak : null)?.Add(ratio);
                    double crawl = 0;
                    for (double t = c.wallS; t <= tg.EndS; t += 20) crawl = Math.Max(crawl, DesignedLightning.At(storm, t, k).crawlerFraction);
                    bool otherStrong = storm.cycles.Any(o => o != c && o.tornado != null && o.tornado.ef >= 3 && o.wallS < tg.EndS && o.tornado.EndS > c.wallS);
                    if (tg.ef >= 3) { strongCycles++; if (crawl > .05) withCrawlers++; }
                    else if (!otherStrong) { weakCycles++; if (crawl > 0) weakWithCrawlers++; }
                }
            }
            Require(weak.Count > 5 && strong.Count > 5 && strong.Average() > 1.3 * weak.Average() - .3,
                $"stronger cycles jump harder: x{weak.DefaultIfEmpty(0).Average():0.00} before EF0-1 tornadoes, x{strong.DefaultIfEmpty(0).Average():0.00} before EF3+ ({weak.Count}/{strong.Count} cycles)", results);
            Require(strongCycles > 0 && withCrawlers == strongCycles && weakWithCrawlers == 0,
                $"crawlers in every EF3+ cycle ({withCrawlers}/{strongCycles}) and in none of {weakCycles} weaker ones", results);
        }

        static void Pulse(string folder, StormLightningTuning k, List<string> results)
        {
            // Synthetic regression fixture; pulse storms are not selectable game presets.
            var pulse = new StormPreset { T0F = 90, TdF = 72, moistFt = 1500, lapse = 6, cap = 0, llj = 8, shear = 10, upper = 24 };
            var storm = new DesignedStorm(new FixedEnvironment(Environment.Make(pulse.ToEnv())), new Vec2(0, 0), 1, ClimoRegion.Plains);
            if (storm.kind != StormKind.Pulse) { results.Add($"SKIP pulse preset is {storm.kind}, not a pulse storm"); return; }
            var curve = Sample(storm, k, Path.Combine(folder, "designed_pulse.csv"));
            var peak = curve.OrderByDescending(p => p.a.flashesPerMinute).First();
            // A plausible band for an ordinary storm.
            Require(peak.a.flashesPerMinute > 3 && peak.a.flashesPerMinute < 40 && peak.t > 10 * 60 && peak.t < 34 * 60,
                $"pulse storm peaks at {peak.t / 60:0} min ({peak.a.flashesPerMinute:0}/min), before the collapse", results);
            Require(DesignedLightning.At(storm, 50 * 60, k).flashesPerMinute < .05f * peak.a.flashesPerMinute, "pulse storm goes quiet as it collapses", results);
        }

        static List<(double t, LightningActivity a)> Sample(DesignedStorm storm, StormLightningTuning k, string path)
        {
            var curve = new List<(double, LightningActivity)>();
            var csv = new StringBuilder("minutes,flashes_per_minute,ground_fraction,positive_of_ground,reach,crawler_fraction\n");
            for (double t = 0; t <= storm.lifeS; t += 30)
            {
                var a = DesignedLightning.At(storm, t, k);
                curve.Add((t, a));
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.00},{2:0.000},{3:0.000},{4:0.000},{5:0.000}",
                    t / 60, a.flashesPerMinute, a.groundFraction, a.positiveGroundFraction, a.reach, a.crawlerFraction));
            }
            File.WriteAllText(path, csv.ToString());
            return curve;
        }

        static double Mean(List<(double t, LightningActivity a)> curve, double from, double to)
        {
            var inside = curve.Where(p => p.t >= from && p.t <= to).Select(p => (double)p.a.flashesPerMinute).ToList();
            return inside.Count > 0 ? inside.Average() : 0;
        }

        static void Require(bool condition, string message, List<string> results)
        { if (!condition) throw new Exception(message); results.Add("PASS " + message); }
    }
}
