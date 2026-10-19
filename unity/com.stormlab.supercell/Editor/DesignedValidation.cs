// Validation of the designed storm (Runtime/Design): runs DesignedChecks and writes Validation/DesignedReport.md next
// to the test project's Assets folder. Batch:
//   Unity.exe -batchmode -projectPath unity/SupercellLabTest -executeMethod StormLab.Supercell.EditorTools.DesignedValidation.RunFromCommandLine -logFile <file>
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace StormLab.Supercell.EditorTools
{
    public static class DesignedValidation
    {
        static string OutDir => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Validation"));

        [MenuItem("Supercell Lab/Run Designed Storm Validation")]
        public static void RunMenu() => Run();

        public static void RunFromCommandLine()
        {
            bool ok;
            try { ok = Run(); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static bool Run()
        {
            Directory.CreateDirectory(OutDir);
            var rep = new StringBuilder();
            rep.AppendLine("# Designed storm validation").AppendLine();
            rep.AppendLine($"Run {DateTime.Now:yyyy-MM-dd HH:mm}, Unity {Application.unityVersion}.").AppendLine();
            rep.AppendLine("| Test | Result | Detail |").AppendLine("| --- | --- | --- |");
            int pass = 0, fail = 0;
            foreach (var (name, run) in DesignedChecks.All)
            {
                var sw = System.Diagnostics.Stopwatch.StartNew(); string d;
                try { d = run(); } catch (Exception e) { Debug.LogException(e); d = "FAIL exception: " + e.Message; }
                bool ok = !d.StartsWith("FAIL", StringComparison.Ordinal);
                if (ok) pass++; else fail++;
                if (!ok) d = d.Substring(5);
                Debug.Log($"[Designed] {(ok ? "PASS" : "FAIL")} {name}: {d} ({sw.Elapsed.TotalSeconds:0.0} s)");
                rep.AppendLine($"| {name} | {(ok ? "PASS" : "**FAIL**")} | {d.Replace("|", "/")} |");
            }
            rep.AppendLine().AppendLine($"{pass} passed, {fail} failed.");
            File.WriteAllText(Path.Combine(OutDir, "DesignedReport.md"), rep.ToString());
            Debug.Log($"[Designed] overall {(fail == 0 ? "PASS" : "FAIL")}: {pass} passed, {fail} failed");
            return fail == 0;
        }
    }
}
