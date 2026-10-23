// A game scenario: the storms of one hour-long game on a 30 x 30 mile map, laid out from one seed. Two to four supercells
// enter from the map's upstream side in separate lanes, a few minutes apart, and cross it at their storm motion (most run
// on past the far edge before they die; the storms are scaled down in a later project). Every storm puts down at least
// one tornado. How strong a game gets is decided once for the whole game: in about 60 % of games no tornado exceeds EF2,
// in 20 % the strongest is an EF3, and in 20 % an EF4 or (rarely) an EF5. The strongest tornado belongs to one storm
// that starts early; the others are capped at that rating.
//
// Map frame: km in the environment's frame, x east and y north, south-west corner at (0, 0). Times: game seconds from
// the start; a storm's own time is the game time minus its start.
using System;
using System.Collections.Generic;
using System.Linq;

namespace StormLab.Supercell
{
    public sealed class ScenarioStorm
    {
        public int index, seed;
        /// <summary>where it initiates, km in the map frame; game seconds when it initiates</summary>
        public Vec2 spawnKm; public double startS;
        public DesignedStormOptions options;
        public DesignedStorm storm;
        /// <summary>the storm's own time at game time t, s (negative before it initiates)</summary>
        public double StormTime(double gameS) => gameS - startS;
        public IEnumerable<TornadoGene> Tornadoes => storm.cycles.Where(c => c.tornado != null && c.tornado.lifeS > 0).Select(c => c.tornado);
    }

    public sealed class StormScenario
    {
        public const double MapMiles = 30, MapKm = MapMiles * 1.609344, GameS = 3600;
        /// <summary>no storm starts later than this into the game, s</summary>
        public const double LatestStartS = 12 * 60;

        public readonly int seed;
        public readonly ClimoRegion region;
        public readonly DesignedPrecipitation precipitation;
        public readonly IEnvironmentSource source;
        /// <summary>the strongest rating the game is built to reach (2: no tornado above EF2)</summary>
        public readonly int topEf;
        public readonly List<ScenarioStorm> storms = new List<ScenarioStorm>();

        public StormScenario(IEnvironmentSource env, int seed, ClimoRegion region, DesignedPrecipitation precipitation = DesignedPrecipitation.Auto)
        {
            this.seed = seed; this.region = region; this.precipitation = precipitation; source = env;
            var r = new StormRng(seed, 500);
            double uN = r.U(), uTier = r.U(), uFive = r.U(), uHead = r.U();
            int n = uN < 0.35 ? 2 : uN < 0.75 ? 3 : 4;
            topEf = uTier < 0.2 ? (uFive < 0.15 ? 5 : 4) : uTier < 0.4 ? 3 : 2;

            // storm motion over the map (the environment at the map's centre)
            var centre = new Vec2(MapKm / 2, MapKm / 2);
            var motion = StormCharacter.From(env.At(centre.x, centre.y, 0)).motion;
            double m = Math.Sqrt(motion.x * motion.x + motion.y * motion.y);
            var d = m > 0.5 ? new Vec2(motion.x / m, motion.y / m) : new Vec2(1, 0);
            var across = new Vec2(-d.y, d.x);

            // lanes across the storms' motion, shuffled so the first storm is not always the southern one
            var lanes = Enumerable.Range(0, n).Select(i => -0.36 * MapKm + 0.72 * MapKm * (i + 0.5) / n + (r.U() - 0.5) * 0.08 * MapKm).ToList();
            for (int i = n - 1; i > 0; i--) { int j = (int)(r.U() * (i + 1)); (lanes[i], lanes[j]) = (lanes[j], lanes[i]); }
            var starts = new List<double> { 0 };
            for (int i = 1; i < n; i++) starts.Add(60 * r.Range(2, LatestStartS / 60));
            starts.Sort();

            // the storm with the game's strongest tornado starts in the first 8 min, so the tornado has time to run
            var early = Enumerable.Range(0, n).Where(i => starts[i] <= 8 * 60).ToList();
            int head = topEf >= 3 ? early[Math.Min((int)(uHead * early.Count), early.Count - 1)] : -1;

            for (int i = 0; i < n; i++)
            {
                // enter the map on its upstream side, 3 km in, in this storm's lane
                var lane = new Vec2(centre.x + across.x * lanes[i], centre.y + across.y * lanes[i]);
                double tx = d.x > 1e-6 ? lane.x / d.x : d.x < -1e-6 ? (MapKm - lane.x) / -d.x : double.PositiveInfinity;
                double ty = d.y > 1e-6 ? lane.y / d.y : d.y < -1e-6 ? (MapKm - lane.y) / -d.y : double.PositiveInfinity;
                double back = Math.Max(Math.Min(tx, ty) - 3, 0);
                var spawn = new Vec2(lane.x - d.x * back, lane.y - d.y * back);
                var options = new DesignedStormOptions { maxEf = topEf, forceEf = i == head ? topEf : -1, horizonS = GameS - starts[i] };
                int baseSeed = 1 + (int)(new StormRng(seed, 600 + i).Next() % 999983UL);
                // the storm must put down a tornado (and the headline storm its rated one); try the next seed if not
                DesignedStorm storm = null; int stormSeed = baseSeed;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    stormSeed = baseSeed + 7919 * attempt;
                    storm = new DesignedStorm(env, spawn, stormSeed, region, precipitation, options);
                    var tors = storm.cycles.Where(c => c.tornado != null).Select(c => c.tornado).ToList();
                    if (tors.Count > 0 && (options.forceEf < 0 || tors.Any(t => t.ef == options.forceEf))) break;
                }
                storms.Add(new ScenarioStorm { index = i, seed = stormSeed, spawnKm = spawn, startS = starts[i], options = options, storm = storm });
            }
        }

        /// <summary>the strongest tornado of the game</summary>
        public int MaxEf => storms.SelectMany(s => s.Tornadoes).Select(t => t.ef).DefaultIfEmpty(-1).Max();
        public int TornadoCount => storms.Sum(s => s.Tornadoes.Count());
        /// <summary>every storm a supercell with at least one tornado</summary>
        public bool EveryStormTornadic => storms.All(s => s.storm.kind == StormKind.Supercell && s.Tornadoes.Any());

        public override string ToString() =>
            $"{storms.Count} storms, {TornadoCount} tornadoes, strongest EF{MaxEf} (game built for EF{topEf} at most)";
    }
}
