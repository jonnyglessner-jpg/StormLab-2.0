// The volumetric skin (DESIGN.md 2.5): draws the storm the brain simulates, or a designed storm (DesignedStormRunner,
// or BindDesigned). Add it next to a SupercellRunner (or call Bind with a StormRun); under HDRP it installs the custom
// pass that draws it (StormSkinCustomPass). Every frame it reads the timeline at the playback
// time through the FeatureDesigner, repaints part of the target volume, advects the live volume with the simulated wind,
// relights part of it, and the custom pass ray-marches it for each camera.
//
// Output: by default the storm is the game's weather. Its light is in HDRP's physical units (luminanceScale, times the
// camera's exposure), the game's sky and ground show behind it, and HDRP's tonemapping and exposure adaptation apply.
// UseStandaloneLook() gives the lab's own previews instead: 1.0's fields and sky behind the storm and 1.0's display
// curve, for a volume with HDRP's tonemapping off.
//
// World placement: 1 Unity unit = 1 m, +x east, +z north, +y up. The sim grid's south-west corner is at groundOrigin
// (world x, z) at t = 0 and moves with the grid's frame motion; the ground is at groundHeight.
using System.Collections.Generic;
using StormLab.Supercell.Designer;
using StormLab.Supercell.Dynamics;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Rendering
{
    public enum SunSource { Light, TimeOfDay, Fixed }

    [ExecuteAlways, DefaultExecutionOrder(100)]
    public class StormSkin : MonoBehaviour
    {
        [Tooltip("the brain; leave empty and call Bind() to drive the skin from code")]
        public SupercellRunner runner;

        [Header("Placement")]
        [Tooltip("world x, z (m) of the simulation grid's south-west corner at t = 0")]
        public Vector2 groundOrigin = new Vector2(-40000f, -40000f);
        public float groundHeight = 0f;

        [Header("Playback")]
        [Tooltip("model time shown, s")]
        public float playbackTime;
        [Tooltip("sim-seconds per second (timelapse 1x, 2x, ... 60x)")]
        public float timelapse = 1f;
        public bool playing = true;

        [Header("Quality")]
        public SkinTier tier = SkinTier.Average;
        [Tooltip("frames over which the target volume is repainted")]
        [Range(1, 16)] public int buildSlabs = 12;
        [Tooltip("frames over which the light grid is recomputed")]
        [Range(1, 8)] public int lightSlabs = 4;
        [Tooltip("ray-march resolution relative to the camera")]
        [Range(0.25f, 1f)] public float renderScale = 0.5f;
        [Range(64, 1024)] public int maxSteps = 384;
        [Tooltip("weight of the new frame in the temporal resolve (1 = off)")]
        [Range(0.05f, 1f)] public float temporalBlend = 0.25f;
        [Tooltip("march half the pixels each frame, alternating, and fill the rest from their neighbours and the history (about half the cost)")]
        public bool checkerboard = true;
        [Tooltip("the 30 m near-field volume round the low-level mesocyclone: flat base, stacked plates, wall cloud (DESIGN.md 2.5)")]
        public bool nearField = true;
        [Tooltip("frames over which the near field is repainted, and over which its light is recomputed")]
        [Range(1, 16)] public int nearSlabs = 8;
        [Range(1, 8)] public int nearLightSlabs = 4;

        [Header("Motion")]
        [Tooltip("carry the cloud with the simulated wind")]
        public bool advect = true;
        [Tooltip("how fast the advected cloud is pulled back to the designed shape, s")]
        public float relaxSeconds = 60f;

        [Header("Sun")]
        [Tooltip("Light: the 'sun' Light below (1.0's evening sun, azimuth 272, 15 degrees up, if none). TimeOfDay: from the local " +
                 "solar time, latitude and day below, and the 'sun' Light is turned to match. Fixed: the azimuth and elevation below")]
        public SunSource sunSource = SunSource.Light;
        [Tooltip("directional light for the sun (empty: 1.0's evening sun, low in the west)")]
        public Light sun;
        [Tooltip("local solar time, hours (TimeOfDay). 18.8 at Dodge City in late May puts the sun 3-4 degrees up: the reference photos' sunset light")]
        [Range(0f, 24f)] public float solarHour = 18.8f;
        [Range(-66f, 66f)] public float latitude = 37.75f;
        [Range(1, 366)] public int dayOfYear = 145;
        [Tooltip("compass bearing of the sun, degrees (Fixed)")]
        public float sunAzimuth = 272f;
        [Tooltip("height of the sun above the horizon, degrees (Fixed)")]
        public float sunElevation = 15f;

        [Header("Look")]
        [Tooltip("brightness through 1.0's display curve (toneMap on only)")]
        public float exposure = 1.7f;
        [Tooltip("1.0's display curve, for the lab's standalone previews (HDRP tonemapping off). Off: the storm's light in " +
                 "HDRP's physical units, for the game's own exposure and tonemapping")]
        public bool toneMap = false;
        [Tooltip("luminance (cd/m², nits) of the skin's unit of light: white cloud in 1.0's evening sun, 15 degrees up. " +
                 "0: from the sun Light's illuminance (lux) as luminance = lux x " + nameof(SunToLuminance) + " / pi")]
        public float luminanceScale = 0f;
        /// <summary>the share of the sun Light's lux that reaches the ground with the sun 15 degrees up, under HDRP's
        /// physically based sky (Kasten-Young air mass ~3.8 with a clear-day extinction, ~0.5)</summary>
        public const float SunToLuminance = 0.5f;
        /// <summary>the sun's illuminance assumed when there is no sun Light, lux (HDRP's default directional light)</summary>
        public const float DefaultSunLux = 100000f;
        [Tooltip("Physical: sunlight on cloud from multiple-scattering octaves and a Mie phase function for the droplets " +
                 "(light glowing through towers and thin edges, silver linings when back-lit). Off: 1.0's lighting, for comparison")]
        public bool physicalLighting = true;
        [Tooltip("cloud droplet diameter, micrometres (5-50), which shapes the Mie phase function: continental cumulus about 10-20")]
        [Range(5f, 50f)] public float dropletDiameter = 16f;
        [Tooltip("multiple-scattering octaves (1 = single scattering only)")]
        [Range(1, 4)] public int scatterOctaves = 4;
        [Tooltip("each octave's extinction relative to the one before: lower carries sunlight deeper into the cloud (Wrenninge's 0.5; 0.7 keeps low-sun cores blue-grey, not orange-brown)")]
        [Range(0.3f, 0.95f)] public float octaveExtinction = 0.7f;
        [Tooltip("brightness of the physical sunlight on cloud, calibrated so the chaser view matches 1.0's exposure")]
        public float sunScale = 1.26f;
        [Tooltip("brightness of the physical sky light on cloud (what each point sees of the sky: zenith, mid sky, horizon)")]
        public float skyScale = 1f;
        [Tooltip("brightness of the light bounced up from the ground onto cloud undersides")]
        public float bounceScale = 1f;
        [Tooltip("sky light that diffuses through the cloud to points that cannot see the sky (keeps a shadowed base blue-grey, not black)")]
        [Range(0f, 0.5f)] public float skyDiffuse = 0.2f;

        [Header("Night")]
        [Tooltip("directional light for the moon (Light sun source; empty: the bearing and height below)")]
        public Light moon;
        [Tooltip("0 new, 0.25 first quarter, 0.5 full, 0.75 last quarter. With TimeOfDay it also places the moon in the sky")]
        [Range(0f, 1f)] public float moonPhase = 0.5f;
        [Tooltip("compass bearing of the moon, degrees (Fixed, or Light with no moon Light)")]
        public float moonAzimuth = 120f;
        [Tooltip("height of the moon above the horizon, degrees (Fixed, or Light with no moon Light)")]
        public float moonElevation = 30f;
        [Tooltip("how bright a moonlit night is shown, relative to 1.0's evening sun. Physically about 0.000005; 0.08 keeps the storm readable")]
        [Range(0.005f, 0.095f)] public float nightBrightness = 0.08f;
        [Tooltip("the dimmest a night is ever shown (a moonless night far from towns)")]
        [Range(0f, 0.095f)] public float nightFloor = 0.045f;
        [Tooltip("night blue shift: the eye's rods see no colour and favour blue-green")]
        [Range(0f, 1f)] public float purkinje = 0.6f;
        [Tooltip("town light on the haze and cloud bases at night: 0 none .. 1 a large town nearby")]
        [Range(0f, 1f)] public float lightPollution = 0.35f;
        [Tooltip("at night, a floor on how much sky every point sees: lifts the darkest shadows (under the base) as the eye's local adaptation does")]
        [Range(0f, 1f)] public float nightFill = 0.35f;
        [Tooltip("brighten night scenes to nightBrightness, for the standalone previews. Off (the game): night is as dark as " +
                 "moonlight is, and HDRP's exposure adapts to it")]
        public bool nightAdaptation = false;
        [Tooltip("draw 1.0's sky and fields behind the storm where the camera sees no geometry (standalone previews only: " +
                 "it covers the game's own sky)")]
        public bool drawBackdrop = false;
        [Tooltip("draw this storm as well as the active one (several storms at once, e.g. DesignedScenarioRunner). The " +
                 "farthest storm drawn supplies the backdrop; nearer ones are laid over it")]
        public bool drawAlongside;
        [Tooltip("invert the automatic vertical flip of the scene-depth lookup (only if a platform shows the storm cut by the wrong objects)")]
        public bool invertDepthFlip;
        [Tooltip("invert the automatic vertical flip of the composite (only if a platform shows the storm upside down)")]
        public bool invertCompositeFlip;
        // HDRP's camera targets, its depth buffer included, hold the bottom of the view in row 0 (it renders them with a
        // flipped projection), as the march's rows do, so neither lookup flips. Verified on D3D12 (the game views'
        // ground and markers, DesignedSkinTests); other APIs follow the same rule but are untested
        bool AutoFlip => false;
        bool DepthAutoFlip => false;

        /// <summary>The lab's standalone look: 1.0's backdrop, display curve and night adaptation (for a volume with
        /// HDRP's tonemapping off). The defaults are the game's.</summary>
        public void UseStandaloneLook() { toneMap = true; drawBackdrop = true; nightAdaptation = true; }

        /// <summary>luminance of the skin's unit of light, nits (luminanceScale, or from the sun Light)</summary>
        public float Luminance => luminanceScale > 0f ? luminanceScale : (sun ? sun.intensity : DefaultSunLux) * SunToLuminance / Mathf.PI;

        // ---------------------------------------------------------------- runtime
        public static StormSkin Active { get; private set; }
        /// <summary>every enabled skin (the custom pass draws Active and those marked drawAlongside)</summary>
        public static readonly List<StormSkin> Enabled = new List<StormSkin>();
        public StormRun Run { get; private set; }
        public FeatureDesigner Designer { get; private set; }
        public WindHistory Wind { get; private set; }
        /// <summary>the designed storm being drawn (instead of the brain), and its wind</summary>
        public DesignedStorm Designed { get; private set; }
        public KinematicWind DesignedWind { get; private set; }
        /// <summary>environment km of grid (0, 0) in designed mode</summary>
        public Vector2 DesignedOriginKm { get; private set; }
        /// <summary>the wind the passes read: the brain's history or the designed storm's kinematic field</summary>
        public IWindSource ActiveWind => Designed != null ? (IWindSource)DesignedWind : Wind;
        public SkinVolume Volume { get; private set; }
        public NearVolume Near { get; private set; }
        /// <summary>Optional local lighting adapter. Null preserves the normal cloud path.</summary>
        [System.NonSerialized] public IStormTransientLighting TransientLighting;
        public readonly SkinParams Params = new SkinParams();
        /// <summary>volume centre in grid metres</summary>
        public Vector2 Centre { get; private set; }
        public bool ReadyToRender => Volume != null && hasFrame;
        public float WallClock { get; private set; }

        HydrometeorHistory hydrometeors;
        ComputeShader volCs, marchCs, nearCs; Material compositeMat; Texture3D noise;
        /// <summary>profiling switches for the march (see _Dbg in StormSkinMarch.compute); 0 in normal use</summary>
        [System.NonSerialized] public int debugFlags;
        bool hasFrame; float lastT = float.NaN; Vector2 pendingShift;
        CommandBuffer frameCmd;

        public static readonly Vector3 DefaultSunVol = SunVol(272f, 15f);
        /// <summary>direction toward the sun in the volume frame (x east, y north, z up) from a compass bearing and a height, degrees</summary>
        public static Vector3 SunVol(float azDeg, float elevDeg)
        {
            float az = azDeg * Mathf.Deg2Rad, el = elevDeg * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(az) * Mathf.Cos(el), Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el));
        }
        /// <summary>
        /// The sun's compass bearing and height (degrees) at a local solar time (hours), latitude (degrees north) and day of
        /// the year: the usual declination and hour-angle formulas, good to about a degree, which is all the sky needs.
        /// </summary>
        public static Vector2 SolarPosition(float solarHour, float latDeg, int day)
        {
            double decl = 23.44 * Mathf.Deg2Rad * System.Math.Sin(2 * System.Math.PI * (284 + day) / 365.0);
            double H = (solarHour - 12.0) * 15.0 * Mathf.Deg2Rad, lat = latDeg * Mathf.Deg2Rad;
            double sinEl = System.Math.Sin(lat) * System.Math.Sin(decl) + System.Math.Cos(lat) * System.Math.Cos(decl) * System.Math.Cos(H);
            double az = System.Math.Atan2(-System.Math.Sin(H), System.Math.Tan(decl) * System.Math.Cos(lat) - System.Math.Sin(lat) * System.Math.Cos(H));
            return new Vector2((float)((az * Mathf.Rad2Deg + 360.0) % 360.0), (float)(System.Math.Asin(sinEl) * Mathf.Rad2Deg));
        }
        public Vector3 SunDirVol
        {
            get
            {
                switch (sunSource)
                {
                    case SunSource.TimeOfDay: { var a = SolarPosition(solarHour, latitude, dayOfYear); return SunVol(a.x, a.y); }
                    case SunSource.Fixed: return SunVol(sunAzimuth, sunElevation);
                    default: return sun ? WorldToVolDir(-sun.transform.forward) : DefaultSunVol;
                }
            }
        }
        /// <summary>
        /// The moon's compass bearing and height (degrees): it trails the sun by its phase (a full moon rises at sunset), and
        /// its declination swings opposite the sun's as it waxes to full (summer full moons ride low). Good to a few degrees.
        /// </summary>
        public static Vector2 MoonPosition(float solarHour, float latDeg, int day, float phase)
        {
            float h = solarHour - phase * 24f;
            h = ((h % 24f) + 24f) % 24f;
            double declSun = 23.44 * System.Math.Sin(2 * System.Math.PI * (284 + day) / 365.0);
            double decl = declSun * System.Math.Cos(2 * System.Math.PI * phase) * Mathf.Deg2Rad;
            double H = (h - 12.0) * 15.0 * Mathf.Deg2Rad, lat = latDeg * Mathf.Deg2Rad;
            double sinEl = System.Math.Sin(lat) * System.Math.Sin(decl) + System.Math.Cos(lat) * System.Math.Cos(decl) * System.Math.Cos(H);
            double az = System.Math.Atan2(-System.Math.Sin(H), System.Math.Tan(decl) * System.Math.Cos(lat) - System.Math.Sin(lat) * System.Math.Cos(H));
            return new Vector2((float)((az * Mathf.Rad2Deg + 360.0) % 360.0), (float)(System.Math.Asin(sinEl) * Mathf.Rad2Deg));
        }
        public Vector3 MoonDirVol
        {
            get
            {
                if (sunSource == SunSource.TimeOfDay) { var a = MoonPosition(solarHour, latitude, dayOfYear, moonPhase); return SunVol(a.x, a.y); }
                if (sunSource == SunSource.Light && moon) return WorldToVolDir(-moon.transform.forward);
                return SunVol(moonAzimuth, moonElevation);
            }
        }
        public NightSettings Night => new NightSettings
        {
            moonPhase = moonPhase, nightBrightness = nightBrightness, nightFloor = nightFloor, purkinje = purkinje,
            lightPollution = lightPollution, adapt = nightAdaptation
        };
        /// <summary>this frame's sun, moon, sky and night light (SkyLighting)</summary>
        public SkyLight Lighting => SkyLighting.Evaluate(SunDirVol, MoonDirVol, Night);
        /// <summary>the direction the light grids are computed for: the sun, or the moon at night</summary>
        public Vector3 KeyDirVol => Lighting.keyDir;
        bool lastKeyMoon;
        static readonly int LightScaleId = Shader.PropertyToID("_StormLabLight");

        // ---- steady rotation. The shaders turn features by rate x clock; with a simulated rate that changes, that angle
        // jumps (or stalls) whenever the rate changes late in a session. Each angle is integrated here over model
        // clock, and the GPU gets the rate that gives it: phase / clock (live 1.0 fixed the same jump with a steady rate)
        static readonly Slot[] SpinSlot = { Slot.Wall2, Slot.Tilt, Slot.MesoV, Slot.Tor6 };
        static readonly int[] SpinComp = { 1, 3, 2, 0 };
        readonly double[] spinPhase = new double[4];
        float spinClock = float.NaN;
        readonly SkinParams gpuParams = new SkinParams();
        /// <summary>Params as the GPU passes take them: spin rates replaced by their integrated phase / clock</summary>
        public SkinParams GpuBlock => gpuParams;
        void UpdateGpuParams(float clock)
        {
            Params.CopyTo(gpuParams);
            float dc = float.IsNaN(spinClock) ? float.NaN : clock - spinClock;
            bool restart = !hasFrame || float.IsNaN(dc) || dc < 0f || dc > 60f;
            for (int i = 0; i < SpinSlot.Length; i++)
            {
                float rate = Params[SpinSlot[i]][SpinComp[i]];
                spinPhase[i] = restart ? rate * (double)clock : spinPhase[i] + rate * (double)dc;
                var v = gpuParams[SpinSlot[i]];
                v[SpinComp[i]] = clock > 1e-3f ? (float)(spinPhase[i] / clock) : rate;
                gpuParams[SpinSlot[i]] = v;
            }
            spinClock = clock;
        }

        /// <summary>
        /// Sun and sky colour for a sun direction (volume frame). The sunlight reddens and dims with the air mass it
        /// crosses (Kasten-Young air mass, per-channel extinction fitted so 1.0's evening sun, 15 degrees up, keeps 1.0's
        /// colour (1, 0.82, 0.6) x 0.95); the sky and ambient light dim as the sun gets low. skyC.w is that sky scale (1 at
        /// 15 degrees).
        /// </summary>
        public static void SunColours(Vector3 sunVol, out Vector4 sunC, out Vector4 skyC)
        {
            float sinEl = Mathf.Clamp(sunVol.z, -1f, 1f);
            float elDeg = Mathf.Asin(sinEl) * Mathf.Rad2Deg;
            float Air(float e) { float ec = Mathf.Max(e, -0.5f); return 1f / (Mathf.Max(Mathf.Sin(ec * Mathf.Deg2Rad), 0f) + 0.50572f * Mathf.Pow(ec + 6.07995f, -1.6364f)); }
            float m = Air(elDeg), m15 = Air(15f), k = m / m15;
            float bright = Mathf.Exp(-0.06f * (m - m15)) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 1.5f, elDeg));
            sunC = new Vector4(0.95f * bright, 0.95f * 0.82f * Mathf.Pow(0.82f, k - 1f) * bright, 0.95f * 0.6f * Mathf.Pow(0.6f, k - 1f) * bright, 0f);
            float skyK = Mathf.Clamp(0.35f + 2.5f * sinEl, 0.2f, 1.3f) / (0.35f + 2.5f * Mathf.Sin(15f * Mathf.Deg2Rad));
            skyC = new Vector4(0.42f * skyK, 0.52f * skyK, 0.68f * skyK, skyK);
        }
        public static Vector3 WorldToVolDir(Vector3 w) => new Vector3(w.x, w.z, w.y);
        public static Vector3 VolToWorldDir(Vector3 v) => new Vector3(v.x, v.z, v.y);

        void OnEnable()
        {
            Active = this; if (!Enabled.Contains(this)) Enabled.Add(this);
            if (GraphicsSettings.currentRenderPipeline is UnityEngine.Rendering.HighDefinition.HDRenderPipelineAsset) StormSkinCustomPass.EnsureInstalled();
        }
        /// <summary>Make this the skin the custom pass draws (the last enabled one otherwise).</summary>
        public void Activate() { Active = this; }
        void OnDisable()
        {
            Enabled.Remove(this);
            if (Active != this) return;
            Active = null;
            // hand over to another skin still drawn alongside, so a group of storms keeps drawing as members switch off
            for (int i = Enabled.Count - 1; i >= 0; i--) if (Enabled[i] && Enabled[i].drawAlongside) { Active = Enabled[i]; break; }
        }
        void OnDestroy() { Release(); }

        void LoadAssets()
        {
            if (!volCs) volCs = Resources.Load<ComputeShader>("StormSkinVolume");
            if (!marchCs) marchCs = Resources.Load<ComputeShader>("StormSkinMarch");
            if (!nearCs) nearCs = Resources.Load<ComputeShader>("StormSkinNear");
            if (!compositeMat) { var sh = Shader.Find("Hidden/StormLab/SkinComposite"); if (sh) compositeMat = new Material(sh) { hideFlags = HideFlags.DontSave }; }
            if (!noise) noise = CloudNoise.Make();
        }

        /// <summary>Attach to a storm. The skin keeps its own wind history; frames already computed get wind from then on.</summary>
        public void Bind(StormRun run)
        {
            if (Run == run) return;
            Release();
            LoadAssets();
            Run = run;
            Designer = new FeatureDesigner(run.timeline, run.env);
            Wind = new WindHistory(run.g, volCs);
            hydrometeors = new HydrometeorHistory(run.timeline);
            Volume = new SkinVolume(tier, volCs);
            Near = nearCs ? new NearVolume(tier, nearCs) : null;
            hasFrame = false; lastT = float.NaN;
        }

        /// <summary>
        /// Draw a designed storm. wind covers the storm's track; its grid (0, 0) is at originKm in the environment's
        /// frame, and groundOrigin should be set to that point's world x, z (m).
        /// </summary>
        public void BindDesigned(DesignedStorm storm, KinematicWind wind)
        {
            if (Designed == storm) return;
            Release();
            LoadAssets();
            Designed = storm; DesignedWind = wind; DesignedOriginKm = wind.OriginKm;
            Volume = new SkinVolume(tier, volCs);
            Near = nearCs ? new NearVolume(tier, nearCs) : null;
            hasFrame = false; lastT = float.NaN;
        }

        public void Release()
        {
            Wind?.Dispose(); Wind = null;
            hydrometeors?.Dispose(); hydrometeors = null;
            DesignedWind?.Dispose(); DesignedWind = null; Designed = null;
            Volume?.Dispose(); Volume = null;
            Near?.Dispose(); Near = null;
            foreach (var c in cams.Values) c.Dispose();
            cams.Clear();
            Run = null; Designer = null; hasFrame = false;
        }

        void Update()
        {
            if (Designed != null)
            {
                float ddt = Application.isPlaying ? Time.deltaTime : 0f;
                WallClock += ddt;
                if (playing) playbackTime = Mathf.Clamp(playbackTime + ddt * timelapse, 0f, (float)Designed.lifeS);
                StepTo(playbackTime, WallClock);
                return;
            }
            if (runner != null && runner.Run != null && runner.Run != Run) Bind(runner.Run);
            if (Run == null) return;
            Wind.Poll(Run);
            float dt = Application.isPlaying ? Time.deltaTime : 0f;
            WallClock += Application.isPlaying ? Time.deltaTime : 0f;
            float pdt = playing ? dt * timelapse : 0f;
            float t = Mathf.Min(playbackTime + pdt, Run.Horizon);
            if (t < playbackTime) pdt = 0;   // waiting for the sim
            playbackTime = Mathf.Max(0, t);
            StepTo(playbackTime, WallClock);
        }

        /// <summary>
        /// Show model time t (s): evaluate the designer and do this frame's volume work. A step forward of up to a
        /// minute is advected; a jump or a scrub backwards repaints the volume.
        /// Cloud flow and rotation use t, so 1x is physical time, pause freezes motion, and timelapse scales all motion.
        /// clock is retained as the caller's presentation wall clock; it does not drive atmospheric motion.
        /// </summary>
        public bool StepTo(float t, float clock)
        {
            if (Volume == null || (Run == null && Designed == null)) return false;
            WallClock = clock;
            if (Run != null && Run.timeline.Latest < 0) return false;
            float dt = float.IsNaN(lastT) ? 0f : t - lastT;
            bool reset = !hasFrame || dt < 0f || dt > 60f;
            // volume centre: follows the storm in whole voxels, so the advected volume can be shifted exactly
            Vector2 c = Centre; Vector2 shift = Vector2.zero;
            if (reset)
            {
                EvaluateBlock(t, t, Vector2.zero);
                c = Params.suggestedCentre;
            }
            else
            {
                Vector2 want = Params.suggestedCentre; float vox = Volume.VoxelKm * 1000f;
                if ((want - c).magnitude > 4000f)
                {
                    var step = new Vector2(Mathf.Round((want.x - c.x) / vox) * vox, Mathf.Round((want.y - c.y) / vox) * vox);
                    c += step; shift = step / 1000f;
                }
            }
            Centre = c;
            // Reprojection follows the translating cloud frame. Recenter its history
            // with the volume; a time seek must discard history from the old scene.
            foreach (var state in cams.Values)
            {
                if (reset) state.valid = false;
                else state.Shift(shift);
            }
            if (!EvaluateBlock(t, t, Centre)) return false;
            Volume.Precipitation = hydrometeors;
            Volume.PrecipitationCentre = Centre;
            var wind = ActiveWind;
            bool windOk = wind.Upload(t);
            Volume.PrecipitationWind = windOk ? wind : null;
            if (frameCmd == null) frameCmd = new CommandBuffer { name = "Storm skin volume" };
            frameCmd.Clear();
            if (sunSource == SunSource.TimeOfDay && sun) sun.transform.rotation = Quaternion.LookRotation(-VolToWorldDir(SunDirVol));
            var lighting = Lighting;
            Vector3 sunVol = lighting.keyDir;
            // the key light switched between sun and moon: relight the whole storm now rather than over several frames
            bool relight = lighting.keyIsMoon != lastKeyMoon; lastKeyMoon = lighting.keyIsMoon;
            // the near-field box follows the low-level updraft (the mesocyclone's base)
            bool nearOn = nearField && Near != null && Params[Slot.Tower].z > 0f;
            bool nearFull = false;
            if (nearOn)
            {
                var up = Params[Slot.Up];
                nearFull = Near.Place(Centre + new Vector2(up.x, up.y) * 1000f, Params[Slot.Base].x) || reset;
                Params[Slot.Near] = Near.Slot(Centre);
            }
            UpdateGpuParams(t);
            // Slab budgets are normally in rendered frames. At timelapse those
            // frames can span tens of model seconds, leaving geometry/textures
            // at different times. Bound a complete refresh to two model seconds.
            int MotionSlabs(int budget) => Mathf.Min(budget, Mathf.Max(1, Mathf.FloorToInt(2f / Mathf.Max(dt, .001f))));
            Volume.Frame(frameCmd, gpuParams, noise, wind, advect && windOk, Centre, shift, dt, relaxSeconds, sunVol, reset, MotionSlabs(buildSlabs), MotionSlabs(lightSlabs));
            if (relight && !reset) Volume.Relight(frameCmd, sunVol, Params[Slot.Base].x);
            if (nearOn) Near.Frame(frameCmd, GpuParams(), noise, Volume, wind, advect && windOk, Centre, sunVol, nearFull || relight, MotionSlabs(nearSlabs), MotionSlabs(nearLightSlabs));
            else Near?.Invalidate();
            Graphics.ExecuteCommandBuffer(frameCmd);
            // the light scale the storm's lightning and rain streaks share with it: x luminance of a unit, y 1 to apply
            // the camera's exposure (StormLab/Lightning, StormLab/RainStreak)
            Shader.SetGlobalVector(LightScaleId, toneMap ? new Vector4(1, 0, 0, 0) : new Vector4(Luminance, 1, 0, 0));
            lastT = t; hasFrame = true;
            return true;
        }

        /// <summary>The parameter block at time t for a volume centred at centreGrid (m): from the brain or the designed storm.</summary>
        bool EvaluateBlock(float t, float clock, Vector2 centreGrid)
        {
            if (Designed == null) return Designer.Evaluate(t, clock, centreGrid, Params);
            var o = DesignedOriginKm;
            Designed.Evaluate(t, clock, new Vec2(o.x + centreGrid.x / 1000.0, o.y + centreGrid.y / 1000.0), Params);
            // the designed storm suggests a centre in environment metres; the skin works in grid metres
            Params.suggestedCentre -= o * 1000f;
            return true;
        }

        /// <summary>Force a full repaint at the next StepTo.</summary>
        public void ResetVolume() { hasFrame = false; lastT = float.NaN; }

        /// <summary>the parameter block as the GPU passes see it: the renderer's Grid slot and the clock filled in</summary>
        public Vector4[] GpuParams()
        {
            var sp = (Vector4[])(float.IsNaN(spinClock) ? Params.v : gpuParams.v).Clone();
            sp[(int)Slot.Grid] = new Vector4(Volume.NV, Volume.NZ, Volume.L, Volume.ZT);
            // Base.z already contains model time from EvaluateBlock, shared by every volume and march pass.
            return sp;
        }
        public bool NearActive => nearField && Near != null && Near.Valid && Params[Slot.Near].w > 0f;

        // ---------------------------------------------------------------- coordinates
        public Vector2 FrameMotion => Run != null ? Run.timeline.frameMotion : Vector2.zero;
        /// <summary>world position (m) to the volume frame (km) at the shown time</summary>
        public Vector3 WorldToVol(Vector3 w)
        {
            Vector2 grid = new Vector2(w.x, w.z) - groundOrigin - FrameMotion * lastTOr0;
            Vector2 v = (grid - Centre) / 1000f;
            return new Vector3(v.x, v.y, (w.y - groundHeight) / 1000f);
        }
        /// <summary>grid position (m) and height above ground (m) to world</summary>
        public Vector3 GridToWorld(Vector2 grid, float z) { var g = grid + groundOrigin + FrameMotion * lastTOr0; return new Vector3(g.x, groundHeight + z, g.y); }
        /// <summary>volume-frame position (km) to world</summary>
        public Vector3 VolToWorld(Vector3 v) => GridToWorld(Centre + new Vector2(v.x, v.y) * 1000f, v.z * 1000f);
        float lastTOr0 => float.IsNaN(lastT) ? 0f : lastT;
        public float ShownTime => lastTOr0;

        // ---------------------------------------------------------------- per-camera march state
        sealed class CamState
        {
            public RenderTexture cur, dist, half; public readonly RenderTexture[] hist = new RenderTexture[2]; public int h;
            public Vector3 prevPos, prevFwd, prevRight, prevUp; public bool valid, wasTransient; public int frame;
            public void Shift(Vector2 s) { prevPos -= new Vector3(s.x, s.y, 0); }
            public void Ensure(int w, int hh)
            {
                if (cur && cur.width == w && cur.height == hh) return;
                Dispose();
                cur = RT(w, hh, RenderTextureFormat.ARGBHalf); dist = RT(w, hh, RenderTextureFormat.RFloat); half = RT(w, hh, RenderTextureFormat.RFloat);
                hist[0] = RT(w, hh, RenderTextureFormat.ARGBHalf); hist[1] = RT(w, hh, RenderTextureFormat.ARGBHalf);
                valid = false; wasTransient = false;
            }
            static RenderTexture RT(int w, int h, RenderTextureFormat f)
            {
                var r = new RenderTexture(w, h, 0, f, RenderTextureReadWrite.Linear) { enableRandomWrite = true, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave, name = "storm march" };
                r.Create(); return r;
            }
            public void Dispose()
            {
                foreach (var r in new[] { cur, dist, half, hist[0], hist[1] }) if (r) { r.Release(); Object.DestroyImmediate(r); }
                cur = dist = half = hist[0] = hist[1] = null;
            }
        }
        readonly Dictionary<Camera, CamState> cams = new Dictionary<Camera, CamState>();

        /// <summary>Discard a camera's temporal history (after a cut).</summary>
        public void ResetHistory(Camera cam) { if (cams.TryGetValue(cam, out var s)) s.valid = false; }

        /// <summary>
        /// Record the ray-march and temporal resolve for one camera into cmd. depth: the camera's depth buffer (or
        /// null), not bound as a target; fullW x fullH: the camera's viewport size. Returns the resolved half-resolution
        /// storm texture (rgb light, a transmittance), and in dist this frame's distance along each ray to where the storm
        /// hides half of what is behind it, km.
        /// </summary>
        public RenderTexture RecordMarch(CommandBuffer cmd, Camera cam, RTHandle depth, int fullW, int fullH, bool backdrop, out RenderTexture dist)
        {
            dist = null;
            if (!ReadyToRender || !marchCs) return null;
            int w = Mathf.Max(8, Mathf.RoundToInt(fullW * renderScale)), h = Mathf.Max(8, Mathf.RoundToInt(fullH * renderScale));
            if (!cams.TryGetValue(cam, out var st)) { st = new CamState(); cams[cam] = st; }
            st.Ensure(w, h);
            var tr = cam.transform;
            Vector3 pos = WorldToVol(tr.position), fwd = WorldToVolDir(tr.forward), right = WorldToVolDir(tr.right), up = WorldToVolDir(tr.up);
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad), tanX = tanY * fullW / (float)fullH;
            int kM = marchCs.FindKernel("March"), kR = marchCs.FindKernel("Resolve");
            var sp = GpuParams();
            bool near = NearActive;
            if (!near) sp[(int)Slot.Near] = Vector4.zero;
            cmd.SetComputeVectorArrayParam(marchCs, "_SP", sp);
            cmd.SetComputeVectorParam(marchCs, "_CamPos", pos); cmd.SetComputeVectorParam(marchCs, "_CamFwd", fwd);
            cmd.SetComputeVectorParam(marchCs, "_CamRight", right); cmd.SetComputeVectorParam(marchCs, "_CamUp", up);
            cmd.SetComputeVectorParam(marchCs, "_CamTan", new Vector4(tanX, tanY, 0, 0));
            cmd.SetComputeVectorParam(marchCs, "_Res", new Vector4(w, h, 1f / w, 1f / h));
            var lighting = Lighting;
            cmd.SetComputeVectorParam(marchCs, "_Sun", lighting.keyDir);
            cmd.SetComputeVectorParam(marchCs, "_SunC", lighting.keyC); cmd.SetComputeVectorParam(marchCs, "_SkyC", lighting.skyC);
            cmd.SetComputeVectorParam(marchCs, "_SkyP", new Vector4(lighting.dim, lighting.townLight, lighting.keyIsMoon ? 1 : 0, 0));
            cmd.SetComputeVectorParam(marchCs, "_Night", new Vector4(lighting.gain, lighting.night, nightFill * lighting.dark, 0));
            cmd.SetComputeVectorParam(marchCs, "_PL2", new Vector4(skyScale, bounceScale, skyDiffuse, octaveExtinction));
            SkyLighting.Bands(lighting, out var bZen, out var bMid, out var bHor, out var bHorK);
            cmd.SetComputeVectorParam(marchCs, "_SkyZen", bZen); cmd.SetComputeVectorParam(marchCs, "_SkyMid", bMid);
            cmd.SetComputeVectorParam(marchCs, "_SkyHor", bHor); cmd.SetComputeVectorParam(marchCs, "_SkyHorK", bHorK);
            cmd.SetComputeVectorParam(marchCs, "_MP", new Vector4(Volume.cloudMax, Volume.rainMax, exposure, st.frame++));
            cmd.SetComputeVectorParam(marchCs, "_PL", new Vector4(physicalLighting ? 1 : 0, dropletDiameter, sunScale, scatterOctaves));
            Vector2 travel = (Centre + FrameMotion * lastTOr0 + groundOrigin) / 1000f;
            cmd.SetComputeVectorParam(marchCs, "_Opt", new Vector4(travel.x, travel.y, drawBackdrop && backdrop ? 1 : 0, toneMap ? 1 : 0));
            float n = cam.nearClipPlane, f = cam.farClipPlane;
            bool rev = SystemInfo.usesReversedZBuffer;
            float zx = rev ? -1f + f / n : 1f - f / n, zy = rev ? 1f : f / n;
            cmd.SetComputeVectorParam(marchCs, "_ZB", new Vector4(zx / f, zy / f, rev ? 1 : 0, (DepthAutoFlip ^ invertDepthFlip) ? 1 : 0));
            if (depth != null) cmd.SetComputeTextureParam(marchCs, kM, "_DepthTex", depth, 0, RenderTextureSubElement.Depth);
            else cmd.SetComputeTextureParam(marchCs, kM, "_DepthTex", NoDepth);
            cmd.SetComputeVectorParam(marchCs, "_DepthP", new Vector4(depth != null ? 1 : 0, fullW, fullH, maxSteps));
            cmd.SetComputeTextureParam(marchCs, kM, "_Dens", Volume.Dens);
            cmd.SetComputeTextureParam(marchCs, kM, "_NearTex", near ? (Texture)Near.Field : SkinVolume.Calm);
            cmd.SetComputeTextureParam(marchCs, kM, "_NearLight", near ? (Texture)Near.LightTex : SkinVolume.Calm);
            cmd.SetComputeVectorParam(marchCs, "_Dbg", new Vector4(debugFlags, 0, 0, 0));
            cmd.SetComputeVectorParam(marchCs, "_NearP", new Vector4(near ? Near.H : 0f, 0, 0, 0));
            var aw = ActiveWind;
            bool windOk = advect && aw != null && aw.Count > 0;
            cmd.SetComputeTextureParam(marchCs, kM, "_WindA", windOk ? (Texture)aw.A : SkinVolume.Calm);
            cmd.SetComputeTextureParam(marchCs, kM, "_WindB", windOk ? (Texture)aw.Bt : SkinVolume.Calm);
            cmd.SetComputeVectorParam(marchCs, "_WindP", windOk ? new Vector4(aw.Blend, aw.Lx, aw.Ly, aw.NL) : new Vector4(0, 1, 1, 1));
            cmd.SetComputeVectorParam(marchCs, "_VolC", new Vector4(Centre.x, Centre.y, Volume.rainFall, windOk ? 1 : 0));
            cmd.SetComputeTextureParam(marchCs, kM, "_Light", Volume.LightTex);
            cmd.SetComputeTextureParam(marchCs, kM, "_SkyVis", Volume.SkyVisTex);
            cmd.SetComputeTextureParam(marchCs, kM, "_Ground", Volume.GroundTex);
            cmd.SetComputeTextureParam(marchCs, kM, "_Noise", noise);
            cmd.SetComputeTextureParam(marchCs, kM, "_Out", st.cur);
            cmd.SetComputeTextureParam(marchCs, kM, "_OutDist", st.dist);
            cmd.SetComputeTextureParam(marchCs, kM, "_OutHalf", st.half);
            cmd.SetComputeIntParam(marchCs, "_TransientLightOn", 0);
            cmd.SetComputeTextureParam(marchCs, kM, "_TransientLightTex", SkinVolume.Calm);
            bool transient = TransientLighting != null && TransientLighting.RecordLighting(cmd, this, cam, marchCs, kM);
            bool fresh = transient || st.wasTransient;
            bool useCheckerboard = checkerboard && !fresh;
            cmd.SetComputeVectorParam(marchCs, "_TP", new Vector4(st.valid && !fresh ? temporalBlend : 1f, useCheckerboard ? 1 : 0, 0, 0));
            cmd.DispatchCompute(marchCs, kM, useCheckerboard ? ((w + 1) / 2 + 7) / 8 : (w + 7) / 8, (h + 7) / 8, 1);
            st.wasTransient = transient;
            // temporal resolve into the other history buffer
            var src = st.hist[st.h]; var dst = st.hist[1 - st.h];
            cmd.SetComputeTextureParam(marchCs, kR, "_Cur", st.cur);
            cmd.SetComputeTextureParam(marchCs, kR, "_CurDist", st.dist);
            cmd.SetComputeTextureParam(marchCs, kR, "_Hist", src);
            cmd.SetComputeTextureParam(marchCs, kR, "_ResolveOut", dst);
            cmd.SetComputeVectorParam(marchCs, "_PrevPos", st.prevPos); cmd.SetComputeVectorParam(marchCs, "_PrevFwd", st.prevFwd);
            cmd.SetComputeVectorParam(marchCs, "_PrevRight", st.prevRight); cmd.SetComputeVectorParam(marchCs, "_PrevUp", st.prevUp);
            cmd.DispatchCompute(marchCs, kR, (w + 7) / 8, (h + 7) / 8, 1);
            st.h = 1 - st.h; st.valid = true;
            st.prevPos = pos; st.prevFwd = fwd; st.prevRight = right; st.prevUp = up;
            dist = st.half;
            return dst;
        }

        /// <summary>RecordMarch without the distance texture (profiling, offline checks).</summary>
        public RenderTexture RecordMarch(CommandBuffer cmd, Camera cam, RTHandle depth, int fullW, int fullH, bool backdrop = true) =>
            RecordMarch(cmd, cam, depth, fullW, fullH, backdrop, out _);

        static Texture2DArray noDepth;
        /// <summary>a 1 x 1 depth stand-in for cameras without depth (the march reads a texture array)</summary>
        static Texture2DArray NoDepth
        {
            get
            {
                if (noDepth) return noDepth;
                noDepth = new Texture2DArray(1, 1, 1, TextureFormat.RFloat, false, true) { hideFlags = HideFlags.HideAndDontSave, name = "storm no depth" };
                noDepth.SetPixels(new[] { Color.clear }, 0); noDepth.Apply(false, true);
                return noDepth;
            }
        }

        MaterialPropertyBlock compositeProps;
        /// <summary>
        /// Draw the storm texture over the bound colour target (out = storm.rgb x light scale + storm.a x target) and,
        /// with writeDepth and a depth target bound, write the storm's depth where it is opaque. w x h: the viewport.
        /// The light scale is 1 with toneMap (the standalone look) and Luminance x the camera's exposure otherwise.
        /// </summary>
        public void RecordComposite(CommandBuffer cmd, Camera cam, Texture storm, Texture dist, int w, int h, bool writeDepth)
        {
            if (!compositeMat || storm == null) return;
            var mpb = compositeProps ??= new MaterialPropertyBlock(); mpb.Clear();
            mpb.SetTexture("_StormTex", storm);
            mpb.SetTexture("_StormDist", dist ? dist : Texture2D.blackTexture);
            mpb.SetVector("_StormView", new Vector4(w, h, (AutoFlip ^ invertCompositeFlip) ? 1 : 0, toneMap ? 0 : Luminance));
            float tanY = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            mpb.SetVector("_StormTan", new Vector4(tanY * w / (float)h, tanY, 0, 0));
            cmd.DrawProcedural(Matrix4x4.identity, compositeMat, 0, MeshTopology.Triangles, 3, 1, mpb);
            if (writeDepth && dist) cmd.DrawProcedural(Matrix4x4.identity, compositeMat, 1, MeshTopology.Triangles, 3, 1, mpb);
        }
    }
}
