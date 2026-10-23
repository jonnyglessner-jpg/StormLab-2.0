// Runs every designed-storm check (the same list DesignedValidation runs in Unity) and exits 1 on any failure.
using System;
using StormLab.Supercell.EditorTools;

static class DesignedChecksMain
{
    static int Main()
    {
        int fail = 0;
        foreach (var (name, run) in DesignedChecks.All)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew(); string d;
            try { d = run(); } catch (Exception e) { d = "FAIL exception: " + e; }
            bool ok = !d.StartsWith("FAIL", StringComparison.Ordinal);
            if (!ok) fail++;
            Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name}: {(ok ? d : d.Substring(5))} ({sw.Elapsed.TotalSeconds:0.0} s)");
        }
        Console.WriteLine(fail == 0 ? $"PASS: all {DesignedChecks.All.Length} designed checks" : $"FAIL: {fail} of {DesignedChecks.All.Length}");
        return fail == 0 ? 0 : 1;
    }
}
