using System;
using System.IO;
using System.Threading.Tasks;
using StormLab.Supercell.Dynamics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Regression: a completed readback must survive several editor frames before Pump consumes it.
public static class CheckpointLifetimeChecks
{
    [MenuItem("Supercell Lab/Run Checkpoint Lifetime Check")]
    public static async void Run()
    {
        const string folder = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\outputs\stormlab-repair";
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "checkpoint-lifetime.txt");
        File.WriteAllText(path, "RUNNING\n");
        try
        {
            var wk = new WeismanKlempProfile(.014, 30);
            wk.ToEnvironmentGrid(out var sd, out var wd);
            var motion = wk.BunkersRightMover();
            using (var run = StormRun.FromProfile(wk, sd, wd, new Vector2((float)motion.x, (float)motion.y),
                new RunSetup { tier = SimTier.Test, runLength = 1800 }))
            {
                AsyncGPUReadback.WaitAllRequests();
                for (int i = 0; i < 8; i++) await NextEditorFrame();
                run.Pump();
                if (run.checkpoints.All.Count != 1 || run.timeline.Latest != 0 || run.PendingReadbacks != 0)
                    throw new Exception("Delayed collection lost the initial checkpoint or timeline frame.");
                var ck = run.checkpoints.All[0];
                for (int f = 0; f < 10; f++)
                    if (ck.fields[f] == null || ck.fields[f].Length != (run.model.FieldBuffer(f).count + 1) / 2)
                        throw new Exception("Incomplete checkpoint field " + SupercellModel.FieldNames[f]);
                if (ck.acc.Length != 2 * run.g.Plane || !run.Seek(0)) throw new Exception("Checkpoint restore failed.");
                run.Pump(true);
                File.WriteAllText(path, "PASS: delayed Pump after eight editor updates retains all ten fields, surface accumulations and timeline frame; checkpoint restore succeeds.\n");
                Debug.Log("[Repair] Checkpoint lifetime regression PASS");
            }
        }
        catch (Exception e) { File.WriteAllText(path, "FAIL: " + e); Debug.LogException(e); }
    }

    static Task NextEditorFrame()
    {
        var completion = new TaskCompletionSource<bool>();
        void Tick() { EditorApplication.update -= Tick; completion.SetResult(true); }
        EditorApplication.update += Tick;
        return completion.Task;
    }
}
