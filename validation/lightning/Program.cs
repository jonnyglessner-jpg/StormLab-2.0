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
            Check(main.Length == 129 && flash.Channels.Length <= (kind == FlashKind.Crawler ? 23 : 12), "Geometry budget");
            Check(kind == FlashKind.InCloud ? main.Last().Y == 120 + settings.upperChargeHeightM :
                kind == FlashKind.Crawler ? flash.Channels.All(c => c.points.All(p => p.Y >= 120 + 800)) : main.Last().Y == 120, "Endpoint placement");
            Check(flash.Channels.All(c => c.points.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && float.IsFinite(p.Z) && p.Y >= 120)), "Finite above-ground geometry");
            Check(flash.Intensity(-1, -.1) == 0 && flash.Intensity(2, 3) == 0, "Flash lifetime");
            Check(flash.Intensity(0, flash.LeadSeconds + .065) == 1, "Exposure must retain a skipped stroke");
            Check(flash.Intensity(flash.LeadSeconds + .005, flash.LeadSeconds + .005) > 0, "Return-stroke decay");
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
            Check(path[0]==anchors.origin && (kind==FlashKind.Crawler || anchored.LightPosition==anchors.origin),"Cloud adapter origin preserved");
            Check(kind==FlashKind.InCloud ? path.Last()==anchors.inCloudTarget :
                kind==FlashKind.Crawler ? anchored.Channels.All(c=>c.points.All(p=>p.Y>120+p.X*.001f+800)) :
                Math.Abs(path.Last().Y-(120+path.Last().X*.001f))<.001f,
                "Cloud anchors preserve in-cloud targets and terrain attachments; crawlers stay off the ground");
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
        LightningSchedule.Query(settings, 0, 3600, all, storm);
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
        LightningSchedule.Query(settings, 0, 3600, full, t => new LightningActivity(1000, 0, 0));
        var candidates = full.ToDictionary(e => e.id, e => (e.time, e.seed));
        Check(Math.Abs(full.Count - 120 * 60) < 5 * Math.Sqrt(120 * 60), "Rates above the ceiling are clipped to it");
        Check(all.All(e => candidates.TryGetValue(e.id, out var c) && c.time == e.time && c.seed == e.seed),
            "Acceptance must not shift candidate times or seeds");

        // Flash-type split follows the fractions at each time.
        LightningSchedule.Query(settings, -1e-6, 600, part, t => new LightningActivity(60, 0, 0));
        Check(part.Count > 0 && part.All(e => e.kind == FlashKind.InCloud), "No ground fraction: all in-cloud");
        LightningSchedule.Query(settings, -1e-6, 600, part, t => new LightningActivity(60, 1, 1));
        Check(part.Count > 0 && part.All(e => e.kind == FlashKind.PositiveGround), "All ground and positive");
        LightningSchedule.Query(settings, 0, 3600, part, t => new LightningActivity(100, .5f, .2f));
        double groundShare = part.Count(e => e.kind != FlashKind.InCloud) / (double)part.Count;
        double positiveShare = part.Count(e => e.kind == FlashKind.PositiveGround) / (double)part.Count(e => e.kind != FlashKind.InCloud);
        Check(Math.Abs(groundShare - .5) < .05 && Math.Abs(positiveShare - .2) < .05, "Flash-type fractions");
        LightningSchedule.Query(settings, -1e-6, 600, part, t => LightningActivity.None);
        Check(part.Count == 0, "Quiet storm: no flashes");
        // Crawlers replace in-cloud flashes only, drawn after the type draws: ground flashes are unchanged.
        var plain = new List<FlashEvent>(); var crawling = new List<FlashEvent>();
        LightningSchedule.Query(settings, 0, 1200, plain, t => new LightningActivity(100, .3f, .1f));
        LightningSchedule.Query(settings, 0, 1200, crawling, t => new LightningActivity(100, .3f, .1f, .8f, .4f));
        Check(plain.Where(e => e.kind != FlashKind.InCloud).Select(e => (e.id, e.kind)).SequenceEqual(crawling.Where(e => e.kind != FlashKind.InCloud && e.kind != FlashKind.Crawler).Select(e => (e.id, e.kind))),
            "Crawlers leave the ground flashes as they were");
        double crawlerShare = crawling.Count(e => e.kind == FlashKind.Crawler) / (double)crawling.Count(e => e.kind == FlashKind.InCloud || e.kind == FlashKind.Crawler);
        Check(Math.Abs(crawlerShare - .4) < .06 && crawling.All(e => e.reach == .8f) && plain.All(e => e.reach == 0), $"Crawler share {crawlerShare:0.00} and reach carried on the events");
        // Reach spreads ground strikes further from the updraft and adds branches; crawlers never touch the ground.
        double Spread(float reach) { double s = 0; for (uint seed = 1; seed <= 200; seed++) { var f = LightningFlash.Create(new FlashEvent(seed, 0, seed, FlashKind.NegativeGround, reach), settings, Vector3.Zero, 0); var a = f.Channels[0].points[0]; var b = f.Channels[0].points[128]; s += Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z)); } return s / 200; }
        Check(Spread(1) > 1.8 * Spread(0), $"Reach spreads ground strikes ({Spread(0):0} m -> {Spread(1):0} m)");
        Check(LightningFlash.Create(new FlashEvent(1, 0, 1, FlashKind.InCloud, 1), settings, Vector3.Zero, 0).Channels.Length >
            LightningFlash.Create(new FlashEvent(1, 0, 1, FlashKind.InCloud, 0), settings, Vector3.Zero, 0).Channels.Length, "Reach adds branches");
        int toAir = 0;
        for (uint seed = 1; seed <= 200; seed++)
        {
            var f = LightningFlash.Create(new FlashEvent(seed, 0, seed, FlashKind.Crawler, seed % 2), settings, new Vector3(5000, 0, 0), 0);
            Check(f.Channels.All(c => c.points.All(p => p.Y >= 800)) && f.LeadSeconds > LightningFlash.LeaderSeconds && f.Duration < 1.5f, "Crawlers stay aloft and creep");
            if (f.Channels[0].points[128].Y < settings.upperChargeHeightM - 2000) toAir++;
        }
        Check(toAir > 40 && toAir < 110, $"Some crawlers leave the storm's side into clear air ({toAir}/200)");
        try { LightningSchedule.Query(settings, 0, 60, part, t => new LightningActivity(float.NaN, 0, 0)); throw new Exception("Accepted NaN activity"); }
        catch (ArgumentOutOfRangeException) { checks++; }
        settings.maxFlashesPerMinute = 0;
        try { settings.Validate(); throw new Exception("Accepted zero ceiling"); } catch (ArgumentOutOfRangeException) { checks++; }
    }
}
