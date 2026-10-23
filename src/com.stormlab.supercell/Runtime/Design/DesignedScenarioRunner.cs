// The game's entry point for a whole game: lay out a StormScenario (2-4 storms on a 30 x 30 mile map) from one seed and
// draw every storm on one game clock. Each storm gets a child GameObject with its own StormSkin (drawn alongside the
// others) and DesignedStormRunner, so lightning connects to each as usual. A storm's child is switched off before it
// initiates and after it has gone.
//
//   var game = gameObject.AddComponent<DesignedScenarioRunner>();
//   game.Spawn(environmentSource, scenarioSeed: 1234, ClimoRegion.Plains);
//   game.GameTime, game.Runners[i] (each storm's queries: Tornado, UpdraftWorld, TornadoWindWorld...), game.Scenario
//
// Coordinates: the map's south-west corner is at environment km (0, 0); worldOriginKm is the environment point at Unity
// world (0, 0). Every storm's runner shares it.
using System.Collections.Generic;
using StormLab.Supercell.Rendering;
using UnityEngine;

namespace StormLab.Supercell
{
    [DefaultExecutionOrder(85)]
    public class DesignedScenarioRunner : MonoBehaviour
    {
        [Header("Scenario (used by Spawn() with no arguments, e.g. on Start)")]
        [Tooltip("StormPreset key for a fixed environment: plains, lp, dixie, ohio")]
        public string preset = "plains";
        public DesignedPrecipitation precipitation = DesignedPrecipitation.Auto;
        public ClimoRegion region = ClimoRegion.Plains;
        public int seed = 1;
        [Tooltip("the environment point (km) at Unity world (0, 0); the map's south-west corner is at (0, 0) km")]
        public Vector2 worldOriginKm = Vector2.zero;
        public bool spawnOnStart = true;

        [Header("Clock")]
        public bool playing = true;
        [Tooltip("game seconds per real second (1 = real time; thunder plays only at 1x)")]
        public float timelapse = 1f;
        [Tooltip("game time, s (0 to StormScenario.GameS)")]
        public float gameTime;

        [Header("Storms")]
        public SkinTier tier = SkinTier.Average;
        public bool nearField = true;
        public bool enableLightning = true;
        [Tooltip("optional: every storm's skin copies this skin's sun, exposure and quality settings each frame")]
        public StormSkin lookFrom;

        public StormScenario Scenario { get; private set; }
        /// <summary>one runner per scenario storm, in the scenario's order</summary>
        public IReadOnlyList<DesignedStormRunner> Runners => runners;
        public float GameTime => gameTime;
        readonly List<DesignedStormRunner> runners = new List<DesignedStormRunner>();

        void Start() { if (spawnOnStart && Scenario == null) SpawnFromInspector(); }

        public void SpawnFromInspector()
        {
            var pr = StormPreset.Resolve(preset);
            preset = pr.key;
            Spawn(new FixedEnvironment(Environment.Make(pr.ToEnv())), seed, region);
        }

        /// <summary>Lay out a new game and start it from the beginning.</summary>
        public StormScenario Spawn(IEnvironmentSource source, int scenarioSeed, ClimoRegion climoRegion)
        {
            Clear();
            seed = scenarioSeed; region = climoRegion;
            Scenario = new StormScenario(source, scenarioSeed, climoRegion, precipitation);
            foreach (var s in Scenario.storms)
            {
                var go = new GameObject($"Storm {s.index + 1}");
                go.transform.SetParent(transform, false);
                var skin = go.AddComponent<StormSkin>();
                skin.drawAlongside = true; skin.tier = tier; skin.nearField = nearField;
                CopyLook(skin);
                var r = go.AddComponent<DesignedStormRunner>();
                r.skin = skin; r.spawnOnStart = false; r.worldOriginKm = worldOriginKm; r.enableLightning = enableLightning;
                r.Spawn(s.storm);
                runners.Add(r);
            }
            gameTime = 0;
            Apply();
            return Scenario;
        }

        /// <summary>Remove the storms (their skins release their GPU resources).</summary>
        public void Clear()
        {
            foreach (var r in runners)
            {
                if (!r) continue;
                r.gameObject.SetActive(false);   // stop drawing now; Destroy is deferred in Play
                if (Application.isPlaying) Destroy(r.gameObject); else DestroyImmediate(r.gameObject);
            }
            runners.Clear();
            Scenario = null;
        }

        void OnDestroy() { Clear(); }

        void Update()
        {
            if (Scenario == null) return;
            if (playing) gameTime = Mathf.Min(gameTime + Time.deltaTime * timelapse, (float)StormScenario.GameS);
            Apply();
        }

        /// <summary>Show every storm at the current game time (call after setting gameTime from code).</summary>
        public void Apply()
        {
            if (Scenario == null) return;
            for (int i = 0; i < runners.Count; i++)
            {
                var r = runners[i]; if (!r) continue;
                double t = StormTime(i);
                bool alive = t >= 0 && t <= Scenario.storms[i].storm.lifeS;
                if (r.gameObject.activeSelf != alive) r.gameObject.SetActive(alive);
                if (!alive) continue;
                r.skin.playing = false;
                r.skin.playbackTime = (float)t;
                CopyLook(r.skin);
            }
        }

        void CopyLook(StormSkin skin)
        {
            if (!lookFrom || skin == lookFrom) return;
            skin.sunSource = lookFrom.sunSource; skin.sun = lookFrom.sun; skin.moon = lookFrom.moon;
            skin.sunAzimuth = lookFrom.sunAzimuth; skin.sunElevation = lookFrom.sunElevation;
            skin.solarHour = lookFrom.solarHour; skin.latitude = lookFrom.latitude; skin.dayOfYear = lookFrom.dayOfYear;
            skin.exposure = lookFrom.exposure; skin.toneMap = lookFrom.toneMap; skin.drawBackdrop = lookFrom.drawBackdrop;
            skin.renderScale = lookFrom.renderScale; skin.maxSteps = lookFrom.maxSteps; skin.debugFlags = lookFrom.debugFlags;
            skin.groundHeight = lookFrom.groundHeight;
        }

        // ---------------------------------------------------------------- queries for the game
        /// <summary>storm i's own time now, s (negative before it initiates)</summary>
        public double StormTime(int i) => Scenario == null ? 0 : Scenario.storms[i].StormTime(gameTime);
        /// <summary>storm i's time clamped to its life (what to evaluate it at)</summary>
        public double StormTimeClamped(int i) => Scenario == null ? 0 : System.Math.Max(0, System.Math.Min(StormTime(i), Scenario.storms[i].storm.lifeS));
        public bool Alive(int i) => Scenario != null && StormTime(i) >= 0 && StormTime(i) <= Scenario.storms[i].storm.lifeS;

        Vector3 KmToWorld(Vec2 p, float zM = 0f) =>
            new Vector3((float)(p.x - worldOriginKm.x) * 1000f, (lookFrom ? lookFrom.groundHeight : 0f) + zM, (float)(p.y - worldOriginKm.y) * 1000f);
        /// <summary>storm i's updraft at cloud base, world position (m), valid before and after its life too</summary>
        public Vector3 UpdraftWorld(int i)
        {
            var st = Scenario.storms[i].storm; double t = StormTimeClamped(i);
            return KmToWorld(st.UpdraftAt(t), (float)st.CharacterAt(t).zb * 1000f);
        }
        /// <summary>storm i's tornado now (km over the ground; off before and after the storm's life)</summary>
        public TornadoNow Tornado(int i) => Alive(i) ? Scenario.storms[i].storm.TornadoAt(StormTime(i)) : default;
        public Vector3 TornadoGroundWorld(int i) { var t = Tornado(i); return t.on ? KmToWorld(t.ground) : Vector3.zero; }
        /// <summary>ground-relative tornado wind of every storm at a world position (m/s, world x/z)</summary>
        public Vector3 TornadoWindWorld(Vector3 world)
        {
            var sum = Vector3.zero;
            for (int i = 0; i < runners.Count; i++)
            {
                if (!Alive(i)) continue;
                var w = Scenario.storms[i].storm.TornadoWind(StormTime(i), new Vec2(world.x / 1000.0 + worldOriginKm.x, world.z / 1000.0 + worldOriginKm.y));
                sum += new Vector3(w.x, 0, w.y);
            }
            return sum;
        }
    }
}
