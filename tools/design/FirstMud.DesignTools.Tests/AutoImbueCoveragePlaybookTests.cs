using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Smoke-tests for the auto-imbue-coverage playbook. Verifies the file loads
/// with the expected (playerLevel × imbueCoveragePct × dangerLevel) axes and
/// that the tolerance bands declare the standard 5-band scheme. Band values
/// are a creative-agent baseline; revisit once live auto-imbue telemetry lands.
/// </summary>
public class AutoImbueCoveragePlaybookTests
{
    private static string PlaybookPath() => Path.Combine(
        RepoRoot.Find(), "tools", "design", "playbooks", "auto-imbue-coverage.json");

    [Fact]
    public void Playbook_file_loads_with_expected_axes()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        Assert.Equal("auto-imbue-coverage", pb.Id);
        Assert.Contains(pb.Axes, a => a.Name == "playerLevel");
        Assert.Contains(pb.Axes, a => a.Name == "imbueCoveragePct");
        Assert.Contains(pb.Axes, a => a.Name == "dangerLevel");
        Assert.NotEmpty(pb.ExpectedViability.Cells);
    }

    [Fact]
    public void Playbook_declares_standard_tolerance_bands()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        Assert.True(pb.ToleranceBands.ContainsKey("trivial"));
        Assert.True(pb.ToleranceBands.ContainsKey("easy"));
        Assert.True(pb.ToleranceBands.ContainsKey("balanced"));
        Assert.True(pb.ToleranceBands.ContainsKey("hard"));
        Assert.True(pb.ToleranceBands.ContainsKey("punishing"));
    }
}
