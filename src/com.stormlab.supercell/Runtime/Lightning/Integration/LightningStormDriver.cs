using StormLab.Supercell.Dynamics;
using StormLab.Supercell.Rendering;
using UnityEngine;

namespace StormLab.Supercell.Lightning
{
    /// <summary>Drives a LightningSystem's flash rate and flash types from the storm the skin shows: the designed storm's
    /// life story or the brain's timeline. Optionally runs the lightning on the skin's model clock.</summary>
    [ExecuteAlways, DefaultExecutionOrder(115)]
    public sealed class LightningStormDriver : MonoBehaviour
    {
        public enum Source { Auto, Designed, Brain }

        public LightningSystem lightning;
        public StormSkin skin;
        [Tooltip("Auto uses the designed storm when the skin has one, otherwise the brain run it is bound to.")]
        public Source source = Source.Auto;
        [Tooltip("Region for the brain's flash-type split (designed storms use their own).")]
        public ClimoRegion brainRegion = ClimoRegion.Plains;
        public StormLightningTuning tuning = new StormLightningTuning();
        [Tooltip("Sample the lightning at the skin's playback time each frame (the activity is in model seconds).")]
        public bool followSkinClock = true;
#pragma warning disable 0414   // shown in the Inspector, never read by code
        [Tooltip("Live readout in Play: the storm-driven activity now. Not an input.")]
        [SerializeField] string activityNow = "";
#pragma warning restore 0414

        /// <summary>The activity at the last sampled time, for readouts; null when no storm is bound.</summary>
        public LightningActivity? Current => lightning ? lightning.CurrentActivity : null;
        /// <summary>Which source is bound now (Auto resolved), or null.</summary>
        public Source? Bound { get; private set; }

        LightningSystem boundLightning;
        DesignedStorm boundStorm;
        StormRun boundRun;
        BrainLightning brain;
        System.Func<double, LightningActivity> activity;
        bool dirty = true;
        float lastPlayback = float.NaN, windowModel, windowReal;
        // Real seconds the 1x speed test averages over.
        const float SpeedWindowS = .1f;

        void OnEnable() { dirty = true; Bind(); }
        void OnValidate() { dirty = true; }

        void LateUpdate()
        {
            Bind();
            // Binding also works in edit mode (for review tools); the clock only follows the skin in Play.
            if (!Application.isPlaying || !followSkinClock || !lightning || !skin || activity == null) return;
            lightning.advanceClock = false;
            // Thunder plays only at real-time speed, so pass the speed the skin's clock actually moved at. Some hosts (the
            // Storm Test Bench) drive playbackTime themselves with skin.playing off. playbackTime is a float, so one frame's
            // step is off by up to two units in the last place: late in a long storm at a high frame rate that is more than
            // 10 % of the step. A step that cannot be 1x even so (a seek, another speed) counts at once and clears the
            // average; the others are judged on their average over SpeedWindowS, which rounding barely moves.
            float now = Mathf.Max(0, skin.playbackTime), dt = Time.deltaTime, step = now - lastPlayback;
            float slack = .1f * dt + 2.4e-7f * Mathf.Max(1, now);
            if (float.IsNaN(step) || dt <= 0) { lightning.timeScale = 0; windowModel = windowReal = 0; }
            else if (Mathf.Abs(step - dt) > slack) { lightning.timeScale = Mathf.Max(0, step / dt); windowModel = windowReal = 0; }
            else
            {
                float keep = Mathf.Exp(-dt / SpeedWindowS);
                windowModel = windowModel * keep + step; windowReal = windowReal * keep + dt;
                float speed = windowModel / windowReal;
                lightning.timeScale = Mathf.Abs(speed - 1) < .1f ? 1 : Mathf.Max(0, speed);
            }
            lastPlayback = now;
            lightning.SampleAt(now);
            var a = lightning.CurrentActivity;
            activityNow = a.HasValue ? $"{Bound}: {a.Value} at {now / 60:0.0} min" : "";
        }

        /// <summary>Call after changing the source, region or tuning from code (the inspector does this itself).</summary>
        public void Rebind() { dirty = true; Bind(); }

        /// <summary>Rebinds when the skin's storm, the source or the tuning changes. Changing them changes the flash history,
        /// so the lightning restarts its presentation.</summary>
        public void Bind()
        {
            if (!lightning) lightning = GetComponent<LightningSystem>();
            Source want = source;
            DesignedStorm storm = skin ? skin.Designed : null;
            StormRun run = skin ? skin.Run : null;
            if (want == Source.Auto) want = storm != null ? Source.Designed : Source.Brain;
            if (want == Source.Designed) run = null; else storm = null;
            if (!isActiveAndEnabled) { storm = null; run = null; }
            if (!dirty && lightning == boundLightning && storm == boundStorm && run == boundRun) return;
            Unbind();
            boundLightning = lightning; boundStorm = storm; boundRun = run;
            if (storm != null) { var s = storm; var k = tuning; activity = t => DesignedLightning.At(s, t, k); Bound = Source.Designed; }
            else if (run != null) { brain = new BrainLightning(run, brainRegion, tuning); activity = brain.At; Bound = Source.Brain; }
            if (lightning && activity != null) { lightning.ActivityAtTime = activity; lightning.ResetPlayback(); }
            dirty = false;
        }

        void Unbind()
        {
            if (boundLightning && activity != null && boundLightning.ActivityAtTime == activity)
            {
                boundLightning.ActivityAtTime = null;
                boundLightning.ResetPlayback();
            }
            brain?.Dispose(); brain = null;
            activity = null; Bound = null;
            boundLightning = null; boundStorm = null; boundRun = null;
        }

        void OnDisable() { Unbind(); dirty = true; }
    }
}
