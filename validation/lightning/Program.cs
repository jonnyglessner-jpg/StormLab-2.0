using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using StormLab.Supercell.Lightning;

static class Program
{
    static int checks;
    static void Check(bool value, string label)
    { checks++; if (!value) throw new Exception(label); }

    static void Main()
    {
        var settings = new LightningSettings { flashesPerMinute = 60 };
        var whole = new List<FlashEvent>(); var part = new List<FlashEvent>(); var split = new List<FlashEvent>();
        LightningSchedule.Query(settings, -1e-6, 600, whole);
        double previous = -1e-6;
        // Irregular render intervals crossing schedule-bin boundaries must produce identical events.
        for (double t = .071; previous < 600; t += .071)
        {
            double end = Math.Min(600, t);
            LightningSchedule.Query(settings, previous, end, part); split.AddRange(part); previous = end;
        }
        Check(whole.Count == split.Count && whole.Count > 400 && whole.Count < 800, "Poisson rate and split count");
        for (int i = 0; i < whole.Count; i++)
            Check(whole[i].id == split[i].id && whole[i].time == split[i].time && whole[i].seed == split[i].seed,
                "Frame partition changed schedule");
        LightningSchedule.Query(settings, 250, 300, part);
        Check(part.Select(e => e.id).SequenceEqual(whole.Where(e => e.time > 250 && e.time <= 300).Select(e => e.id)), "Seek consistency");
        Check(whole.Select(e => e.id).Distinct().Count() == whole.Count, "Duplicate event id");
        foreach (FlashKind kind in Enum.GetValues<FlashKind>())
        for (uint seed = 1; seed <= 80; seed++)
        {
            var e = new FlashEvent(seed, 12, seed, kind);
            var flash = LightningFlash.Create(e, settings, new Vector3(12000, 120, -9000), 120);
            var again = LightningFlash.Create(e, settings, new Vector3(12000, 120, -9000), 120);
            var main = flash.Channels[0].points;
            Check(flash.Channels.SelectMany(c => c.points).SequenceEqual(again.Channels.SelectMany(c => c.points)), "Geometry replay");
            Check(main.Length == 129 && flash.Channels.Length <= 12, "Geometry budget");
            Check(kind == FlashKind.InCloud ? main.Last().Y == 120 + settings.upperChargeHeightM : main.Last().Y == 120, "Endpoint placement");
            Check(flash.Channels.All(c => c.points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && p.Y >= 120)), "Finite above-ground geometry");
            Check(flash.Intensity(-1, -.1) == 0 && flash.Intensity(2, 3) == 0, "Flash lifetime");
            Check(flash.Intensity(0, .1) == 1, "Exposure must retain a skipped stroke");
            Check(flash.Intensity(.04, .04) > 0, "Return-stroke decay");
            Check(flash.StrokeTimes.Zip(flash.StrokeTimes.Skip(1), (a, b) => b > a).All(v => v), "Stroke ordering");
        }
        Check(Math.Abs(LightningAcoustics.ArrivalSeconds(new Vector3(343.42f, 0, 0), Vector3.Zero) - 1) < .0001, "Acoustic travel time");
        Check(LightningAcoustics.SoundSpeed(-20) < LightningAcoustics.SoundSpeed(20), "Temperature affects sound speed");
        var terrainFlash = LightningFlash.Create(new FlashEvent(0, 0, 42, FlashKind.NegativeGround), settings,
            Vector3.Zero, 0, p => 300 + p.X * .01f);
        var attachment = terrainFlash.Channels[0].points.Last();
        Check(Math.Abs(attachment.Y - (300 + attachment.X * .01f)) < .001f, "Terrain attachment adapter");
        foreach (FlashKind kind in Enum.GetValues<FlashKind>())
        {
            var e = new FlashEvent(0,0,42,kind);
            var anchors = new LightningAnchors(new Vector3(16000,12500,-4000),new Vector3(20000,11000,-6000));
            var anchored = LightningFlash.Create(e,settings,Vector3.Zero,120,p=>120+p.X*.001f,anchors);
            var path = anchored.Channels[0].points;
            Check(path[0]==anchors.origin && anchored.LightPosition==anchors.origin,"Cloud adapter origin preserved");
            Check(kind==FlashKind.InCloud ? path.Last()==anchors.inCloudTarget : Math.Abs(path.Last().Y-(120+path.Last().X*.001f))<.001f,
                "Cloud anchors preserve in-cloud targets and terrain attachments");
            Check(path.All(p=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z)),"Long anvil channel finite");
        }
        var thunderFlash = LightningFlash.Create(whole[0], settings, Vector3.Zero, 0);
        var sound = LightningThunder.Synthesize(thunderFlash, new Vector3(0, 2, -10000));
        var soundAgain = LightningThunder.Synthesize(thunderFlash, new Vector3(0, 2, -10000));
        Check(sound.samples.SequenceEqual(soundAgain.samples), "Thunder replay");
        Check(sound.delaySeconds > 0 && sound.samples.Length <= 30 * ThunderSound.SampleRate, "Thunder delay and memory cap");
        Check(sound.samples.All(v => float.IsFinite(v) && Math.Abs(v) <= 1) && sound.samples.Any(v => Math.Abs(v) > .001), "Audible bounded samples");
        StormDrivenChecks();
        settings.flashesPerMinute = 0; LightningSchedule.Query(settings, 0, 60, part); Check(part.Count == 0, "Disabled rate");
        settings.flashesPerMinute = float.NaN;
        try { settings.Validate(); throw new Exception("Accepted NaN"); } catch (ArgumentOutOfRangeException) { checks++; }
        Console.WriteLine($"PASS: {checks} lightning checks; {whole.Count} seeded flashes over ten minutes.");
    }

    // Storm-driven activity: thinning a fixed candidate stream keeps replay, seeking and frame partitioning exact.
    static void StormDrivenChecks()
    {
        var settings = new LightningSettings { maxFlashesPerMinute = 120 };
        var all = new List<FlashEvent>(); var part = new List<FlashEvent>(); var split = new List<FlashEvent>();
        LightningSchedule.Query(settings, -1e-6, 600, part);
        LightningSchedule.Query(settings, -1e-6, 600, all, null);
        Check(part.Select(e => (e.id, e.time, e.seed, e.kind)).SequenceEqual(all.Select(e => (e.id, e.time, e.seed, e.kind))),
            "No activity function keeps the fixed-setting schedule");

        // A storm that grows, jumps and decays over an hour.
        Func<double, LightningActivity> storm = t => new LightningActivity(
            (float)(90 * Math.Exp(-Math.Pow((t - 1800) / 900, 2)) + (t > 2400 && t < 2700 ? 60 : 0)),
            (float)(.1 + .2 * t / 3600), (float)(t > 3000 ? .4 : .08));
        LightningSchedule.Query(settings, -1e-6, 3600, all, storm);
        split.Clear();
        double previous = -1e-6;
        for (double t = .053; previous < 3600; t += .053)
        {
            double end = Math.Min(3600, t);
            LightningSchedule.Query(settings, previous, end, part, storm); split.AddRange(part); previous = end;
        }
        Check(all.Select(e => (e.id, e.time, e.seed, e.kind)).SequenceEqual(split.Select(e => (e.id, e.time, e.seed, e.kind))),
            "Frame partition changed the storm-driven schedule");
        LightningSchedule.Query(settings, 1700, 1760, part, storm);
        Check(part.Select(e => e.id).SequenceEqual(all.Where(e => e.time > 1700 && e.time <= 1760).Select(e => e.id)),
            "Storm-driven seek consistency");
        Check(all.Select(e => e.id).Distinct().Count() == all.Count, "Storm-driven duplicate id");
        // Expected count is the integral of the rate; allow five standard deviations.
        double expected = 0;
        for (double t = .5; t < 3600; t += 1) expected += Math.Min(storm(t).flashesPerMinute, 120) / 60;
        Check(Math.Abs(all.Count - expected) < 5 * Math.Sqrt(expected), $"Storm-driven rate: {all.Count} flashes, expected {expected:0}");
        int early = all.Count(e => e.time < 600), peak = all.Count(e => e.time >= 1500 && e.time < 2100);
        Check(peak > 5 * Math.Max(1, early), "Rate follows the storm's growth");

        // Accepted flashes are a subset of the ceiling's candidates, with the same times and geometry seeds.
        var full = new List<FlashEvent>();
        LightningSchedule.Query(settings, -1e-6, 3600, full, t => new LightningActivity(1000, 0, 0));
        var candidates = full.ToDictionary(e => e.id, e => (e.time, e.seed));
        Check(Math.Abs(full.Count - 120 * 60) < 5 * Math.Sqrt(120 * 60), "Rates above the ceiling are clipped to it");
        Check(all.All(e => candidates.TryGetValue(e.id, out var c) && c.time == e.time && c.seed == e.seed),
            "Acceptance must not shift candidate times or seeds");

        // Flash-type split follows the fractions at each time.
        LightningSchedule.Query(settings, -1e-6, 600, part, t => new LightningActivity(60, 0, 0));
        Check(part.Count > 0 && part.All(e => e.kind == FlashKind.InCloud), "No ground fraction: all in-cloud");
        LightningSchedule.Query(settings, -1e-6, 600, part, t => new LightningActivity(60, 1, 1));
        Check(part.Count > 0 && part.All(e => e.kind == FlashKind.PositiveGround), "All ground and positive");
        LightningSchedule.Query(settings, -1e-6, 3600, part, t => new LightningActivity(100, .5f, .2f));
        double groundShare = part.Count(e => e.kind != FlashKind.InCloud) / (double)part.Count;
        double positiveShare = part.Count(e => e.kind == FlashKind.PositiveGround) / (double)part.Count(e => e.kind != FlashKind.InCloud);
        Check(Math.Abs(groundShare - .5) < .05 && Math.Abs(positiveShare - .2) < .05, "Flash-type fractions");
        LightningSchedule.Query(settings, -1e-6, 600, part, t => LightningActivity.None);
        Check(part.Count == 0, "Quiet storm: no flashes");
        try { LightningSchedule.Query(settings, 0, 60, part, t => new LightningActivity(float.NaN, 0, 0)); throw new Exception("Accepted NaN activity"); }
        catch (ArgumentOutOfRangeException) { checks++; }
        settings.maxFlashesPerMinute = 0;
        try { settings.Validate(); throw new Exception("Accepted zero ceiling"); } catch (ArgumentOutOfRangeException) { checks++; }
    }
}
