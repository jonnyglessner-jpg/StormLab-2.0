// One simulated storm: the GPU model plus its timeline recorder, checkpoints and seek (DESIGN.md 2.2, TIMELINE.md).
// Same environment + RunSetup (tier, seed, initiation, outflow boundary) = same storm, bit for bit on one GPU.
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace StormLab.Supercell.Dynamics
{
    public sealed class StormRun : IDisposable
    {
        public readonly SimGrid g;
        public readonly SimParams p;
        public readonly BaseState b;
        public readonly RunSetup setup;
        /// <summary>environment diagnostics of the input (CAPE, CIN, SRH, Bunkers motion ...)</summary>
        public readonly Environment env;
        public readonly Initiation.Plan plan;
        public readonly SupercellModel model;
        public readonly Timeline timeline;
        public readonly CheckpointStore checkpoints = new CheckpointStore();
        readonly FeatureExtractor fx;

        public readonly int stepsPerFrame, framesPerCheckpoint;
        /// <summary>record timeline frames (off for raw step-time measurements)</summary>
        public bool recording = true;
        /// <summary>take checkpoints</summary>
        public bool checkpointing = true;

        struct PendingFrame { public int index; public long step; public AsyncGPUReadbackRequest c2, c3, sum; }
        struct PendingCheckpoint { public Checkpoint ck; public AsyncGPUReadbackRequest[] f; public AsyncGPUReadbackRequest acc; }
        readonly Queue<PendingFrame> pendingFrames = new Queue<PendingFrame>();
        readonly Queue<PendingCheckpoint> pendingCks = new Queue<PendingCheckpoint>();

        public const float FrameInterval = 10f, CheckpointInterval = 900f;

        // ------------------------------------------------------------ construction
        StormRun(Environment E, BaseState baseState, RunSetup s, SimGrid grid, SimParams prms)
        {
            env = E; b = baseState; setup = s; g = grid; p = prms;
            stepsPerFrame = Mathf.Max(1, Mathf.RoundToInt(FrameInterval / p.dt));
            if (Mathf.Abs(stepsPerFrame * p.dt - FrameInterval) > 1e-3f)
                throw new ArgumentException($"dt {p.dt} s must divide the {FrameInterval} s frame interval");
            framesPerCheckpoint = Mathf.RoundToInt(CheckpointInterval / FrameInterval);
            model = new SupercellModel(g, b, p);
            plan = Initiation.FromEnvironment(E, s.runLength);
            model.SetNudge(Initiation.MakeNudge(g, s, plan));
            Bubble? bubble = null;
            if (s.bubbleEnabled)
                bubble = new Bubble { x = g.Lx * 0.45f, y = g.Ly * 0.5f, z = s.bubbleZ, rh = s.bubbleRadius, rz = s.bubbleDepth, dth = s.bubbleDth, moist = true };
            model.Initialise(s, bubble);
            var shear = new Vector2((float)(WindProfile.At(E.w.u, 6000) - E.w.u[0]), (float)(WindProfile.At(E.w.v, 6000) - E.w.v[0]));
            var motion = new Vector2(b.cu, b.cv);
            int capacity = Mathf.CeilToInt(s.runLength / FrameInterval) + 2;
            timeline = new Timeline(g, model.NCX, model.NCY, model.NL, model.F * g.dx, motion, capacity, FrameInterval);
            fx = new FeatureExtractor(g, model.NCX, model.NCY, model.NL, model.F, motion, shear);
            OnFrameBoundary();
        }

        static SimGrid GridFor(RunSetup s, SimGrid gridOverride) => gridOverride ?? SimGrid.ForTier(s.tier);

        /// <summary>A storm from a 1.0-style preset environment, in the Bunkers right-mover frame.</summary>
        public static StormRun FromEnvironment(Environment E, RunSetup s, SimGrid gridOverride = null, SimParams paramOverride = null)
        {
            var g = GridFor(s, gridOverride);
            return new StormRun(E, BaseState.FromEnvironment(g, E), s, g, paramOverride ?? SimParams.ForGrid(g));
        }

        /// <summary>A storm from a real sounding (data/raw/soundings/*.json), in its Bunkers right-mover frame.</summary>
        public static StormRun FromSoundingJson(string json, RunSetup s, SimGrid gridOverride = null, SimParams paramOverride = null)
        {
            var E = SoundingFile.ToEnvironment(SoundingFile.Parse(json));
            return FromEnvironment(E, s, gridOverride, paramOverride);
        }

        /// <summary>A storm from any analytic profile (Weisman-Klemp, tests), with CIN/CAPE from its 100 m sampling and a given frame motion.</summary>
        public static StormRun FromProfile(IProfile prof, Sounding sDiag, Winds wDiag, Vector2 motion, RunSetup s, SimGrid gridOverride = null, SimParams paramOverride = null)
        {
            var g = GridFor(s, gridOverride);
            var E = Environment.FromProfiles(sDiag, wDiag);
            return new StormRun(E, BaseState.FromProfile(g, prof, motion.x, motion.y), s, g, paramOverride ?? SimParams.ForGrid(g));
        }

        // ------------------------------------------------------------ stepping
        public long Step => model.steps;
        /// <summary>model time, s (exact: steps x dt)</summary>
        public float Time => (float)(model.steps * (double)p.dt);
        public bool Finished => Time >= setup.runLength - 1e-3f;
        /// <summary>model time of the newest finished timeline frame</summary>
        public float Horizon => timeline.Horizon;
        public int StepsToNextFrame => stepsPerFrame - (int)(model.steps % stepsPerFrame);
        public int PendingReadbacks => pendingFrames.Count + pendingCks.Count;

        /// <summary>true while a step has been partly queued segment by segment</summary>
        public bool MidStep => model.SegPhase != 0;

        /// <summary>Queue n time steps on the GPU, recording a frame at every 10 s boundary and a checkpoint every 15 min.
        /// A partly run step is finished first and counts as one of the n.</summary>
        public void Advance(int n)
        {
            if (n > 0 && MidStep) { while (!AdvanceSegment()) { } n--; }
            while (n > 0 && !Finished)
            {
                int m = Math.Min(n, StepsToNextFrame);
                model.Step(m); n -= m;
                if (model.steps % stepsPerFrame == 0) OnFrameBoundary();
            }
        }

        /// <summary>Queue one segment of a step (SupercellModel.Segments per step). Returns true when it completed a step.</summary>
        public bool AdvanceSegment()
        {
            if (!MidStep && Finished) return false;
            bool done = model.StepSegment();
            if (done && model.steps % stepsPerFrame == 0) OnFrameBoundary();
            return done;
        }
        /// <summary>true if completing the current step will land on a timeline frame (so diagnostics follow it)</summary>
        public bool NextStepRecords => (model.steps + 1) % stepsPerFrame == 0;

        /// <summary>Advance to model time t (s), queued.</summary>
        public void AdvanceTo(float t) => Advance((int)Math.Max(0, Math.Round(t / p.dt) - model.steps));

        void OnFrameBoundary()
        {
            int index = (int)(model.steps / stepsPerFrame);
            // checkpoint first: the live state is quantised in place, so the frame and everything after it see exactly
            // the state a seek will restore
            if (checkpointing && index % framesPerCheckpoint == 0)
            {
                var req = model.QuantiseAndCapture();
                if (!checkpoints.Has(model.steps))
                {
                    var ck = new Checkpoint { t = Time, step = model.steps, frameIndex = index };
                    pendingCks.Enqueue(new PendingCheckpoint { ck = ck, f = req, acc = AsyncGPUReadback.Request(model.Accum) });
                }
            }
            if (recording)
            {
                model.RunDiagnostics();
                pendingFrames.Enqueue(new PendingFrame
                {
                    index = index, step = model.steps,
                    c2 = AsyncGPUReadback.Request(model.c2), c3 = AsyncGPUReadback.Request(model.c3), sum = AsyncGPUReadback.Request(model.sum)
                });
            }
        }

        /// <summary>Collect finished readbacks into timeline frames and checkpoints (in order). wait = block until all are done.</summary>
        public int Pump(bool wait = false)
        {
            if (wait && PendingReadbacks > 0) AsyncGPUReadback.WaitAllRequests();
            int made = 0;
            while (pendingCks.Count > 0)
            {
                var pc = pendingCks.Peek();
                bool done = pc.acc.done; foreach (var r in pc.f) done &= r.done;
                if (!done) break;
                pendingCks.Dequeue();
                if (pc.acc.hasError) { Debug.LogError("[Dynamics] checkpoint readback failed"); continue; }
                pc.ck.fields = new uint[10][];
                for (int f = 0; f < 10; f++) pc.ck.fields[f] = pc.f[f].GetData<uint>().ToArray();
                pc.ck.acc = pc.acc.GetData<float>().ToArray();
                checkpoints.Add(pc.ck);
            }
            while (pendingFrames.Count > 0)
            {
                var pf = pendingFrames.Peek();
                if (!(pf.c2.done && pf.c3.done && pf.sum.done)) break;
                pendingFrames.Dequeue();
                if (pf.c2.hasError || pf.c3.hasError || pf.sum.hasError) { Debug.LogError($"[Dynamics] frame {pf.index} readback failed"); continue; }
                var fr = new TimelineFrame { index = pf.index, step = pf.step, t = pf.index * FrameInterval };
                fx.Extract(fr, pf.c2.GetData<float>().ToArray(), pf.c3.GetData<float>().ToArray(), pf.sum.GetData<float>().ToArray(), timeline);
                timeline.Store(fr);
                made++;
            }
            return made;
        }

        // ------------------------------------------------------------ seek
        /// <summary>
        /// Put the full model state at model time t: restore the nearest checkpoint at or before t and re-run to t
        /// (queued on the GPU; frames re-recorded on the way are identical to the stored ones and overwrite them).
        /// Frames beyond t that were already computed stay in the timeline; run-ahead continues from t.
        /// Returns false if no checkpoint is available.
        /// </summary>
        public bool Seek(float t)
        {
            Pump(true);
            long target = (long)Math.Round(Mathf.Clamp(t, 0, setup.runLength) / p.dt);
            var ck = checkpoints.LatestAtOrBefore(target);
            if (ck == null) return false;
            for (int f = 0; f < 10; f++) model.RestoreField(f, ck.fields[f]);
            model.Accum.SetData(ck.acc);
            model.SetTime(ck.t, ck.step);
            Advance((int)(target - ck.step));
            return true;
        }

        /// <summary>Re-run from the checkpoint at or before t and return the frames re-recorded up to t, without storing them (determinism test).</summary>
        public List<TimelineFrame> Replay(float fromT, float toT)
        {
            var got = new List<TimelineFrame>();
            Action<TimelineFrame> grab = f => got.Add(f);
            timeline.FrameAdded += grab;
            try { Seek(fromT); AdvanceTo(toT); Pump(true); }
            finally { timeline.FrameAdded -= grab; }
            return got;
        }

        public long MemoryBytes => timeline.Bytes + checkpoints.Bytes;

        public void Dispose()
        {
            AsyncGPUReadback.WaitAllRequests();
            model.Dispose();
        }
    }
}
