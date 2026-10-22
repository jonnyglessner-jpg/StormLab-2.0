using System;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

// Editor-only, repeatable compilation check. Does not modify simulation or rendering behavior.
[InitializeOnLoad]
public static class StormLabCompileCheck
{
    const string Active = "StormLab.CompileCheck.Active";
    const string Count = "StormLab.CompileCheck.Assemblies";
    const string Errors = "StormLab.CompileCheck.Errors";
    const string Warnings = "StormLab.CompileCheck.Warnings";
    const string Folder = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\outputs\compile-test";

    static StormLabCompileCheck()
    {
        CompilationPipeline.assemblyCompilationFinished += OnAssembly;
        CompilationPipeline.compilationFinished += OnFinished;
    }

    [MenuItem("Supercell Lab/Export Repair Diagnostics")]
    public static void ExportDiagnostics()
    {
        string folder = @"C:\Users\Jonny Glessner\Documents\Codex\2026-10-22\le\outputs\stormlab-repair";
        Directory.CreateDirectory(folder);
        using (var stream = new FileStream(Application.consoleLogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        using (var reader = new StreamReader(stream))
            File.WriteAllText(Path.Combine(folder, "unity-editor.log"), reader.ReadToEnd());
        var messages = new System.Text.StringBuilder();
        foreach (var name in new[] { "StormSkinMarch", "StormSkinNear", "StormSkinVolume", "SupercellDynamics" })
        {
            var shader = Resources.Load<ComputeShader>(name);
            if (!shader) continue;
            foreach (var m in ShaderUtil.GetComputeShaderMessages(shader))
                messages.AppendLine($"{name}: {m.severity}: {m.message} ({m.file}:{m.line})");
        }
        File.WriteAllText(Path.Combine(folder, "shader-messages.txt"), messages.ToString());
        Debug.Log("[Repair] Diagnostics exported to " + folder);
    }

    [MenuItem("Supercell Lab/Compile Scripts (clean)")]
    public static void Compile()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            throw new InvalidOperationException("Exit Play mode and wait for compilation before running this check.");
        Directory.CreateDirectory(Folder);
        SessionState.SetBool(Active, true);
        SessionState.SetInt(Count, 0); SessionState.SetInt(Errors, 0); SessionState.SetInt(Warnings, 0);
        File.WriteAllText(Path.Combine(Folder, "compilation-details.txt"), "Unity " + Application.unityVersion + "\n");
        File.WriteAllText(Path.Combine(Folder, "compilation.txt"), "RUNNING: clean Unity script compilation\n");
        CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
    }

    static void OnAssembly(string assembly, CompilerMessage[] messages)
    {
        if (!SessionState.GetBool(Active, false)) return;
        SessionState.SetInt(Count, SessionState.GetInt(Count, 0) + 1);
        File.AppendAllText(Path.Combine(Folder, "compilation-details.txt"), assembly + "\n");
        foreach (var m in messages)
        {
            string key = m.type == CompilerMessageType.Error ? Errors : Warnings;
            SessionState.SetInt(key, SessionState.GetInt(key, 0) + 1);
            File.AppendAllText(Path.Combine(Folder, "compilation-details.txt"), $"{m.type}: {m.file}:{m.line}: {m.message}\n");
        }
    }

    static void OnFinished(object context)
    {
        if (!SessionState.GetBool(Active, false)) return;
        SessionState.SetBool(Active, false);
        int n = SessionState.GetInt(Count, 0), errors = SessionState.GetInt(Errors, 0), warnings = SessionState.GetInt(Warnings, 0);
        bool failed = errors > 0 || EditorUtility.scriptCompilationFailed;
        string result = $"{(failed ? "FAIL" : "PASS")}: Unity script compilation check\nUnity {Application.unityVersion}\nAssembly completion callbacks: {n}\nCompiler errors: {errors}\nCompiler warnings: {warnings}\n";
        if (n == 0) result += "Unity emitted no per-assembly callbacks for this request; unchanged cached assemblies may be reused.\n";
        File.WriteAllText(Path.Combine(Folder, "compilation.txt"), result);
        Debug.Log("[CompileCheck] " + result);
    }
}
