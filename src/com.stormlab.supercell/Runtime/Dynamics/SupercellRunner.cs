// Drop-in component: builds a storm from a preset or a real sounding at Start and runs it ahead of playback in the
// background. Phase 2 (designer and renderer) reads Run.timeline; the game's time slider calls Seek or just reads
// frames below Horizon.
using UnityEngine;

namespace StormLab.Supercell.Dynamics
{
    public class SupercellRunner : MonoBehaviour
    {
        public enum Source { Preset, SoundingJson }

        [Header("Environment")]
        public Source source = Source.SoundingJson;
        [Tooltip("validation/soundings/*.json (tools/fetch_sounding.py format)")]
        public TextAsset sounding;
        [Tooltip("1.0-style preset: surface T and Td (F), moisture depth above cloud base (ft), 700-500 lapse (C/km), cap (C), low-level jet, 0-6 km shear and upper wind (kt). Defaults: 1.0's Plains tornado outbreak.")]
        public float T0F = 84, TdF = 68, moistFt = 4000, lapse = 7.8f, cap = 1.0f, lljKt = 44, shearKt = 55, upperKt = 80;

        [Header("Run")]
        public RunSetup setup = new RunSetup();

        [Header("Scheduling")]
        [Tooltip("GPU milliseconds per frame the sim may use")]
        public float gpuBudgetMs = 4f;
        [Tooltip("fastest run-ahead, sim-seconds per wall-second (0 = budget only)")]
        public float maxSpeed = 10f;

        public StormRun Run { get; private set; }
        public RunAheadScheduler Scheduler { get; private set; }
        public Timeline Timeline => Run?.timeline;
        /// <summary>model time of the newest computed frame, s</summary>
        public float Horizon => Run != null ? Run.Horizon : 0f;

        public EnvInput PresetInput() => new EnvInput
        {
            T0F = T0F, TdF = Mathf.Min(TdF, T0F), moist = moistFt * C.FT, lapse = lapse, cap = cap,
            llj = lljKt * C.KT, shear = shearKt * C.KT, upper = upperKt * C.KT
        };

        void Start()
        {
            if (!SystemInfo.supportsComputeShaders) { Debug.LogError("[Supercell] compute shaders are not supported on this device"); enabled = false; return; }
            Run = source == Source.SoundingJson && sounding != null
                ? StormRun.FromSoundingJson(sounding.text, setup)
                : StormRun.FromEnvironment(Environment.Make(PresetInput()), setup);
            Scheduler = new RunAheadScheduler(Run, gpuBudgetMs, maxSpeed);
            Scheduler.Calibrate(4);
            Debug.Log($"[Supercell] {Run.g}; dt {Run.p.dt} s; {Run.plan}; step {Scheduler.stepMs:0.0} ms; VRAM {Run.model.VramBytes / 1e6:0} MB");
        }

        void Update()
        {
            if (Scheduler == null) return;
            Scheduler.gpuBudgetMs = gpuBudgetMs; Scheduler.maxSpeed = maxSpeed;
            Scheduler.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>Restore the full model state at t (nearest checkpoint, then re-run); run-ahead continues from there.</summary>
        public bool Seek(float t) => Run != null && Run.Seek(t);

        void OnDestroy() { Run?.Dispose(); Run = null; Scheduler = null; }
    }
}
