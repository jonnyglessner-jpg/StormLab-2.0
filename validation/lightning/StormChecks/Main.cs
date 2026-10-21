// Storm-driven lightning checks without Unity: the designed-storm activity review (the same file as the editor menu)
// and the brain adapter on hand-built timeline frames. Compiled against UnityStub.cs, not Unity's API, so Unity's own
// compile is still the final word. Run: dotnet run --project validation/lightning/StormChecks/StormChecks.csproj
using System.IO;

static class StormChecksMain
{
    static void Main()
    {
        string repo = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory()));
        while (!File.Exists(Path.Combine(repo, "CLAUDE.md")) && Path.GetDirectoryName(repo) != null) repo = Path.GetDirectoryName(repo);
        UnityEngine.Application.dataPath = Path.Combine(repo, "tests", "SupercellLabTest", "Assets");
        StormLab.Supercell.Lightning.EditorTools.LightningActivityReview.Run();
        BrainTest.Run();
    }
}
