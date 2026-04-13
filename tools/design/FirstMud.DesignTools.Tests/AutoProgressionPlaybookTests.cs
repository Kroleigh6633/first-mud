using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Smoke-tests for the scaffolded auto-progression benchmark playbook.
/// Verifies the file loads and that the engine produces the expected
/// cell shape. Sub-modes are still stubbed — we don't pin win-rate numbers.
/// </summary>
public class AutoProgressionPlaybookTests
{
    private static string PlaybookPath() => Path.Combine(
        RepoRoot.Find(), "tools", "design", "playbooks", "auto-progression-endgame.json");

    [Fact]
    public void Playbook_file_loads_with_expected_fields()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        Assert.Equal("auto-progression-endgame", pb.Id);
        Assert.NotEmpty(pb.Axes);
        Assert.Contains(pb.Axes, a => a.Name == "dangerLevel");
        Assert.NotEmpty(pb.ExpectedViability.Cells);
        Assert.True(pb.ToleranceBands.ContainsKey("punishing"));
    }

    [Fact]
    public void Engine_executes_playbook_and_returns_expected_shape()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        // Keep this fast — override rolls for CI.
        pb.Rolls = 20;

        var engine = new PlaybookEngine(new ContentProvider(RepoRoot.ContentDir()));
        var result = engine.Execute(pb);

        Assert.Equal("auto-progression-endgame", result.PlaybookId);
        Assert.Equal(pb.Axes[0].Values.Length, result.Cells.Count);
        foreach (var cell in result.Cells)
        {
            Assert.True(cell.Summary.Wins >= 0);
            Assert.False(string.IsNullOrEmpty(cell.ActualBand));
        }
    }
}
