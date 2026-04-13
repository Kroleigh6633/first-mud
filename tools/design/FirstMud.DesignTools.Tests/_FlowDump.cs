using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner.AutoQuestFlow;
using Xunit;
using Xunit.Abstractions;

namespace FirstMud.DesignTools.Tests;

/// <summary>Diagnostic dump — prints per-quest + per-zone outcomes to the
/// test output so baseline reports can be assembled without a separate CLI.</summary>
public class _FlowDump
{
    private readonly ITestOutputHelper _out;
    public _FlowDump(ITestOutputHelper o) { _out = o; }

    [Fact]
    public void Dump_all_quest_outcomes()
    {
        var content = new ContentProvider(RepoRoot.ContentDir());
        var svc = new AutoQuestSimulationService(content);

        _out.WriteLine("=== ALL QUESTS ===");
        foreach (var r in svc.RunAll())
            _out.WriteLine($"{r.QuestId,-14} | {r.QuestType,-8} | {r.Outcome,-22} | {r.Minutes:F1}m | {r.Title}");

        _out.WriteLine("");
        _out.WriteLine("=== PER ZONE (poolSize=10) ===");
        foreach (var z in new[] {
            "aeldran-1-caervorn-highlands","aeldran-2-thornwood","aeldran-5-drowned-coast",
            "aeldran-7-starting-road","aeldran-8-gravenhold" })
        {
            var runs = svc.RunZone(z, 10, 30);
            int completed = runs.Count(r => r.Outcome == AutoQuestSimulationService.Outcome.Completed);
            _out.WriteLine($"");
            _out.WriteLine($"[{z}] {completed}/{runs.Count} completed");
            foreach (var r in runs)
                _out.WriteLine($"  {r.Outcome,-22} {r.QuestId,-14} {r.QuestType} :: {r.Title}");
        }
    }
}
