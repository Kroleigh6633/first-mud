using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Smoke-tests for the auto-craft-progress playbook. Verifies the file loads
/// with the expected (playerLevel × gearTier) cell grid and that the engine
/// produces a summary for every cell. Band values are baseline expectations;
/// the creative agent revisits once live auto-craft telemetry lands.
/// </summary>
public class AutoCraftPlaybookTests
{
    private static string PlaybookPath() => Path.Combine(
        RepoRoot.Find(), "tools", "design", "playbooks", "auto-craft-progress.json");

    [Fact]
    public void Playbook_file_loads_with_expected_axes()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        Assert.Equal("auto-craft-progress", pb.Id);
        Assert.Contains(pb.Axes, a => a.Name == "playerLevel");
        Assert.Contains(pb.Axes, a => a.Name == "gearTier");
        Assert.Contains(pb.Axes, a => a.Name == "dangerLevel");
        Assert.NotEmpty(pb.ExpectedViability.Cells);
        Assert.True(pb.ToleranceBands.ContainsKey("balanced"));
    }

    [Fact]
    public void Engine_executes_all_cells()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        pb.Rolls = 20; // keep CI fast

        var engine = new PlaybookEngine(new ContentProvider(RepoRoot.ContentDir()));
        var result = engine.Execute(pb);

        // 3 * 3 * 1 = 9 cells
        Assert.Equal(9, result.Cells.Count);
        foreach (var cell in result.Cells)
        {
            Assert.True(cell.Summary.Wins >= 0);
            Assert.False(string.IsNullOrEmpty(cell.ActualBand));
        }
    }
}
