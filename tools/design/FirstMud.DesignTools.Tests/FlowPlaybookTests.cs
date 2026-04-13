using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using FirstMud.DesignTools.Tools.PlaybookRunner.AutoQuestFlow;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Covers the flow cell-evaluator and the AutoQuestSimulationService.
/// These tests exercise the data path: every authored quest is either
/// completed by the sim or categorised as a specific stall reason.
/// </summary>
public class FlowPlaybookTests
{
    private static IContentProvider Content() => new ContentProvider(RepoRoot.ContentDir());

    [Fact]
    public void Auto_quest_sim_classifies_every_authored_quest()
    {
        var svc = new AutoQuestSimulationService(Content());
        var runs = svc.RunAll();

        Assert.NotEmpty(runs);
        foreach (var r in runs)
        {
            Assert.False(string.IsNullOrWhiteSpace(r.QuestId));
            // Every run resolves to a known outcome bucket — no "undefined" case.
            Assert.True(Enum.IsDefined(typeof(AutoQuestSimulationService.Outcome), r.Outcome));
        }
    }

    [Fact]
    public void Flow_playbook_runs_and_shipped_playbook_is_loadable()
    {
        // The shipped auto-quest-completion.json should deserialize cleanly and
        // the engine should dispatch it to the flow evaluator without throwing.
        var path = Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "auto-quest-completion.json");
        Assert.True(File.Exists(path), $"shipped flow playbook missing: {path}");

        var pb = PlaybookRunnerCommand.LoadPlaybook(path);
        Assert.Equal("flow", pb.Kind);
        Assert.NotEmpty(pb.Axes);

        var engine = new PlaybookEngine(Content());
        var result = engine.Execute(pb);

        Assert.NotEmpty(result.Cells);
        // Every flow cell records a completion rate in [0,1].
        foreach (var c in result.Cells)
        {
            Assert.InRange(c.Summary.WinRate, 0.0, 1.0);
        }
    }
}
