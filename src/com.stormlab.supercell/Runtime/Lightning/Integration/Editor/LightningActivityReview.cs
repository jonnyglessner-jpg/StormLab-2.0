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
            // A plausible band for a supercell's total flash rate, which the 120/min ceiling clips only during the strongest
            // jumps, so the life cycle stays visible.
            Require(peak.a.flashesPerMinute > 15 && peak.a.flashesPerMinute < 160 && peak.t > 15 * 60,
                $"{name}: mature supercell peak {peak.a.flashesPerMinute:0} flashes/min at {peak.t / 60:0} min", results);
            var settingsCeiling = new LightningSettings().maxFlashesPerMinute;
            double clipped = curve.Count(p => p.a.flashesPerMinute >= settingsCeiling) / (double)curve.Count(p => p.a.flashesPerMinute > 0);
            Require(clipped < .1, $"{name}: at the {settingsCeiling:0}/min ceiling {clipped:0%} of its lightning life", results);
            Require(At(storm.lifeS).flashesPerMinute < .05f * peak.a.flashesPerMinute, $"{name}: lightning ends with the storm", results);
            Require(At(30 * 60).groundFraction > At(12 * 60).groundFraction, $"{name}: ground share rises once rain reaches the ground", results);
            double late = storm.lifeS - .4 * storm.genome.decayS;
            Require(At(late).positiveGroundFraction > At(peak.t).positiveGroundFraction, $"{name}: positive share rises as the storm decays", results);
            // Against the same storm without jumps, so cycle-to-cycle updraft changes cannot hide or fake one.
            var calm = new StormLightningTuning { jumpBeforeTornado = 0, jumpNewCycle = 0 };
            var tornadoOnly = new StormLightningTuning { jumpNewCycle = 0 };
            foreach (var c in storm.cycles)
            {
                if (c.tornado == null) continue;
                double f = c.tornado.formS, lead = f - 480;
                float with = DesignedLightning.At(storm, lead, tornadoOnly).flashesPerMinute, without = DesignedLightning.At(storm, lead, calm).flashesPerMinute;
                if (without < 1) { results.Add($"SKIP {name}: tornado at {f / 60:0} min comes before the lightning"); continue; }
                Require(with > 1.4f * without, $"{name}: lightning jump 8 min before the tornado at {f / 60:0} min ({without:0} -> {with:0}/min)", results);
                // 15 min after touchdown the jump is over, unless the next tornado's jump has begun.
                if (storm.cycles.Any(o => o.tornado != null && o.tornado.formS > f && o.tornado.formS < f + 2100)) continue;
                float after = DesignedLightning.At(storm, f + 900, tornadoOnly).flashesPerMinute, afterCalm = DesignedLightning.At(storm, f + 900, calm).flashesPerMinute;
                Require(after <= 1.01f * afterCalm + .01f, $"{name}: jump over 15 min after the tornado at {f / 60:0} min", results);
            }
            // The real adapter must keep the schedule frame-rate independent.
            var settings = new LightningSettings { seed = seed };
            var whole = new List<FlashEvent>(); var part = new List<FlashEvent>(); var split = new List<FlashEvent>();
            Func<double, LightningActivity> activity = t => DesignedLightning.At(storm, t, k);
            LightningSchedule.Query(settings, peak.t - 300, peak.t + 300, whole, activity);
            for (double t = peak.t - 300; t < peak.t + 300; t += .7) { LightningSchedule.Query(settings, t, Math.Min(t + .7, peak.t + 300), part, activity); split.AddRange(part); }
            Require(whole.Count > 0 && whole.Select(e => e.id).SequenceEqual(split.Select(e => e.id)), $"{name}: designed schedule replays across frame partitions", results);
        }

        static void Pulse(string folder, StormLightningTuning k, List<string> results)
        {
            var storm = Make("pulse", 1);
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
            var csv = new StringBuilder("minutes,flashes_per_minute,ground_fraction,positive_of_ground\n");
            for (double t = 0; t <= storm.lifeS; t += 30)
            {
                var a = DesignedLightning.At(storm, t, k);
                curve.Add((t, a));
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0:0.0},{1:0.00},{2:0.000},{3:0.000}",
                    t / 60, a.flashesPerMinute, a.groundFraction, a.positiveGroundFraction));
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
