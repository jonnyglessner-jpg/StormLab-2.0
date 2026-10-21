// Stand-in for the GPU-backed StormRun: only the fields BrainLightning reads (same names and types as the real class).
namespace StormLab.Supercell.Dynamics
{
    public sealed class StormRun
    {
        public readonly SimGrid g; public readonly BaseState b; public readonly Timeline timeline;
        public StormRun(SimGrid g, BaseState b, Timeline timeline) { this.g = g; this.b = b; this.timeline = timeline; }
    }
}
namespace StormLab.Supercell.Dynamics
{
    // Constants copied from SupercellModel.cs (the GPU model itself is not built here).
    public static class SupercellModel { public const int NC = 20, NF3 = 10, NF2 = 20; }
}
