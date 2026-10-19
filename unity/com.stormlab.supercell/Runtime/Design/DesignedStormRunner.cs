// The game's entry point for a designed storm: spawn one from an environment, a spawn point and a seed, draw it with a
// StormSkin, and ask it where it is and what it is doing. Real time by default (timelapse 1).
//
//   var runner = gameObject.AddComponent<DesignedStormRunner>();
//   runner.Spawn(environmentSource, spawnKm: new Vector2(120, 80), seed: 1234, ClimoRegion.Plains);
//   runner.StormTime, runner.UpdraftWorld, runner.MotionWorld, runner.Tornado, runner.TornadoWindWorld(playerPos), runner.Stage
//
// Coordinates: the environment's frame is km (x east, y north); worldOriginKm is the environment point at Unity world
// (0, 0) (1 unit = 1 m, +x east, +z north). The skin is placed so its grid lines up with that.
using StormLab.Supercell.Designer;
using StormLab.Supercell.Rendering;
using UnityEngine;

namespace StormLab.Supercell
{
    [DefaultExecutionOrder(90)]
    public class DesignedStormRunner : MonoBehaviour
    {
        [Tooltip("the skin that draws the storm (added to this GameObject if empty)")]
        public StormSkin skin;
        [Header("Spawn (used by Spawn() with no arguments, e.g. on Start)")]
        [Tooltip("StormPreset key for a fixed environment: plains, hail, dixie, ohio, lp, capped, pulse")]
        public string preset = "plains";
        public ClimoRegion region = ClimoRegion.Plains;
        public int seed = 1;
        [Tooltip("where the storm initiates, km in the environment's frame")]
        public Vector2 spawnKm = Vector2.zero;
        [Tooltip("the environment point (km) at Unity world (0, 0)")]
        public Vector2 worldOriginKm = Vector2.zero;
        public bool spawnOnStart = true;
        [Tooltip("storm seconds per real second (1 = real time)")]
        public float timelapse = 1f;

        public DesignedStorm Storm { get; private set; }
        public IEnvironmentSource Source { get; private set; }

        void Start() { if (spawnOnStart && Storm == null) SpawnFromInspector(); }

        public void SpawnFromInspector()
        {
            var pr = System.Array.Find(StormPreset.All, p => p.key == preset) ?? StormPreset.All[0];
            Spawn(new FixedEnvironment(Environment.Make(pr.ToEnv())), spawnKm, seed, region);
        }

        /// <summary>Lay out a new storm and start drawing it from initiation.</summary>
        public DesignedStorm Spawn(IEnvironmentSource source, Vector2 spawn, int stormSeed, ClimoRegion climoRegion)
        {
            Source = source; spawnKm = spawn; seed = stormSeed; region = climoRegion;
            Storm = new DesignedStorm(source, new Vec2(spawn.x, spawn.y), stormSeed, climoRegion);
            if (!skin) skin = GetComponent<StormSkin>();
            if (!skin) skin = gameObject.AddComponent<StormSkin>();
            var wind = new KinematicWind(Storm);
            skin.BindDesigned(Storm, wind);
            skin.groundOrigin = (wind.OriginKm - worldOriginKm) * 1000f;
            skin.playbackTime = 0; skin.timelapse = timelapse; skin.playing = true;
            return Storm;
        }

        void Update() { if (skin) skin.timelapse = timelapse; }

        // ---------------------------------------------------------------- queries for the game
        /// <summary>storm time, s since initiation</summary>
        public float StormTime => skin ? skin.playbackTime : 0f;
        public bool Alive => Storm != null && StormTime < Storm.lifeS;
        public string Stage => skin ? skin.Params.stage : "";
        public string Caption => skin ? skin.Params.caption : "";

        Vector3 KmToWorld(Vec2 p, float zM = 0f) => new Vector3((float)(p.x - worldOriginKm.x) * 1000f, (skin ? skin.groundHeight : 0f) + zM, (float)(p.y - worldOriginKm.y) * 1000f);
        Vec2 WorldToKm(Vector3 w) => new Vec2(w.x / 1000.0 + worldOriginKm.x, w.z / 1000.0 + worldOriginKm.y);

        /// <summary>the updraft at cloud base, world position (m)</summary>
        public Vector3 UpdraftWorld => Storm == null ? Vector3.zero : KmToWorld(Storm.UpdraftAt(StormTime), (float)Storm.CharacterAt(StormTime).zb * 1000f);
        /// <summary>storm motion over the ground, world m/s</summary>
        public Vector3 MotionWorld { get { if (Storm == null) return Vector3.zero; var m = Storm.MotionAt(StormTime); return new Vector3((float)m.x, 0, (float)m.y); } }
        /// <summary>what the environment says about the storm now (size, tilt, base, LP/classic/HP, rotation)</summary>
        public StormCharacter Character => Storm != null ? Storm.CharacterAt(StormTime) : default;

        /// <summary>the tornado now (positions in km over the ground; see TornadoGroundWorld)</summary>
        public TornadoNow Tornado => Storm != null ? Storm.TornadoAt(StormTime) : default;
        public Vector3 TornadoGroundWorld { get { var t = Tornado; return t.on ? KmToWorld(t.ground) : Vector3.zero; } }
        /// <summary>ground-relative tornado wind at a world position (m/s, world x/z), zero outside its circulation</summary>
        public Vector3 TornadoWindWorld(Vector3 world)
        {
            if (Storm == null) return Vector3.zero;
            var w = Storm.TornadoWind(StormTime, WorldToKm(world));
            return new Vector3(w.x, 0, w.y);
        }
    }
}
