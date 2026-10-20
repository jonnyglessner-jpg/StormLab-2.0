// Run-ahead scheduler (DESIGN.md 2.2 "Speed and the time slider"): the sim steps in the background, spread over
// frames, capped at a GPU time budget per frame and at a maximum speed (sim-seconds per wall-second). Playback reads
// the timeline; it never waits on the sim unless it scrubs past the computed horizon.
//
// A step is queued in SupercellModel.Segments pieces, so a step that costs more than one frame's budget is spread
// over several frames. A frame never queues more than max(budget, the largest single piece) of estimated GPU time.
using System;
using UnityEngine;
using UnityEngine.Profiling;

namespace StormLab.Supercell.Dynamics
{
    public sealed class RunAheadScheduler
    {
        public readonly StormRun run;
        /// <summary>GPU milliseconds the sim may use per rendered frame</summary>
        public float gpuBudgetMs;
        /// <summary>fastest the sim may run, sim-seconds per wall-second (0 = as fast as the budget allows)</summary>
        public float maxSpeed;
        /// <summary>estimated GPU time of each step segment and of the diagnostics after a frame-boundary step, ms</summary>
        public readonly float[] segMs = new float[SupercellModel.Segments];
        public float diagMs { get; private set; } = 1f;
        public float stepMs { get { float s = 0; foreach (var m in segMs) s += m; return s; } }
        /// <summary>segments queued and their estimated GPU time in the last Tick</summary>
        public int lastSegments { get; private set; }
        public float lastFrameMs { get; private set; }
        /// <summary>stop queuing while this many frame/checkpoint readbacks are outstanding</summary>
        public int maxPendingReadbacks = 12;

        double speedCredit; float carryMs;
        readonly Recorder[] segRec = new Recorder[SupercellModel.Segments];

        public RunAheadScheduler(StormRun run, float gpuBudgetMs = 4f, float maxSpeed = 10f)
        {
            this.run = run; this.gpuBudgetMs = gpuBudgetMs; this.maxSpeed = maxSpeed;
            for (int i = 0; i < segMs.Length; i++)
            {
                segMs[i] = 1.5f;
                var r = Recorder.Get(SupercellModel.SegmentSampler(i));
                if (r != null && r.isValid) { r.enabled = true; segRec[i] = r; }
            }
        }

        /// <summary>
        /// Measure each segment's cost by running whole steps segment by segment with a GPU sync after each (the steps
        /// are real progress). Returns ms per step.
        /// </summary>
        public float Calibrate(int steps = 4)
        {
            run.Advance(1); run.model.Sync();
            var sum = new double[segMs.Length]; var sw = new System.Diagnostics.Stopwatch();
            for (int s = 0; s < steps; s++)
            {
                if (run.Finished) break;
                for (int i = 0; i < segMs.Length; i++)
                {
                    sw.Restart(); run.AdvanceSegment(); run.model.Sync(); sum[i] += sw.Elapsed.TotalMilliseconds;
                }
            }
            for (int i = 0; i < segMs.Length; i++) segMs[i] = Mathf.Max(0.02f, (float)(sum[i] / Math.Max(1, steps)));
            sw.Restart(); run.model.RunDiagnostics(); run.model.Sync(); diagMs = Mathf.Max(0.05f, (float)sw.Elapsed.TotalMilliseconds);
            run.Pump(true);
            return stepMs;
        }

        float MaxPiece { get { float m = diagMs; foreach (var s in segMs) m = Mathf.Max(m, s); return m; } }

        /// <summary>Call once per rendered frame with the unscaled frame time. Returns the number of segments queued.</summary>
        public int Tick(float wallDt)
        {
            lastSegments = 0; lastFrameMs = 0;
            run.Pump();
            // refine segment costs from the GPU profiler where the platform supports it (editor and development players)
            for (int i = 0; i < segMs.Length; i++)
            {
                var r = segRec[i];
                if (r == null || !r.isValid || r.gpuSampleBlockCount <= 0) continue;
                float ms = r.gpuElapsedNanoseconds / 1e6f / r.gpuSampleBlockCount;
                if (ms > 0.005f) segMs[i] = Mathf.Lerp(segMs[i], ms, 0.2f);
            }
            float dt = run.p.dt;
            if (maxSpeed > 0) speedCredit = Math.Min(speedCredit + maxSpeed * wallDt / dt, Math.Max(1.0, maxSpeed * 0.25 / dt));
            float avail = gpuBudgetMs + carryMs;
            while (true)
            {
                if (!run.MidStep)
                {
                    if (run.Finished || run.PendingReadbacks >= maxPendingReadbacks) break;
                    if (maxSpeed > 0 && speedCredit < 1) break;
                }
                int ph = run.model.SegPhase;
                float cost = segMs[ph] + (ph == SupercellModel.Segments - 1 && run.NextStepRecords ? diagMs : 0f);
                if (cost > avail) break;
                if (ph == 0 && maxSpeed > 0) speedCredit -= 1;
                run.AdvanceSegment();
                avail -= cost; lastFrameMs += cost; lastSegments++;
            }
            // carry unused budget only as far as needed to afford the largest single piece
            carryMs = Mathf.Clamp(avail, 0f, Mathf.Max(0f, MaxPiece - gpuBudgetMs));
            return lastSegments;
        }

        /// <summary>sim-seconds per wall-second the budget allows at a given frame rate</summary>
        public float SpeedAt(float fps) => gpuBudgetMs * fps / Mathf.Max(stepMs + diagMs / run.stepsPerFrame, 0.01f) * run.p.dt;
    }
}
