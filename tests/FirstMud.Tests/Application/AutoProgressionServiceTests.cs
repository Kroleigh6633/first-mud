using FirstMud.Application.Services;
using FluentAssertions;
using Xunit;

namespace FirstMud.Tests.Application;

/// <summary>
/// Unit tests for the AutoProgressionService decision logic. These hit the
/// pure scoring function (no DI, no repos) so they're fast and stable.
/// </summary>
public class AutoProgressionServiceTests
{
    [Fact]
    public void ScoreDeficits_applies_axis_weights()
    {
        var deficits = new Dictionary<ProgressionAxis, double>
        {
            [ProgressionAxis.Gear]      = 0.5,
            [ProgressionAxis.Imbue]     = 0.5,
            [ProgressionAxis.Companion] = 0.5,
            [ProgressionAxis.Player]    = 0.5,
        };
        var scores = AutoProgressionService.ScoreDeficits(deficits);

        scores[ProgressionAxis.Gear].Should().BeApproximately(0.625, 1e-6);      // 0.5 * 1.25
        scores[ProgressionAxis.Imbue].Should().BeApproximately(0.550, 1e-6);     // 0.5 * 1.10
        scores[ProgressionAxis.Companion].Should().BeApproximately(0.500, 1e-6); // 0.5 * 1.00
        scores[ProgressionAxis.Player].Should().BeApproximately(0.450, 1e-6);    // 0.5 * 0.90
    }

    [Fact]
    public void ScoreDeficits_picks_gear_as_top_when_all_tied()
    {
        var deficits = new Dictionary<ProgressionAxis, double>
        {
            [ProgressionAxis.Gear]      = 0.5,
            [ProgressionAxis.Imbue]     = 0.5,
            [ProgressionAxis.Companion] = 0.5,
            [ProgressionAxis.Player]    = 0.5,
        };
        var scores = AutoProgressionService.ScoreDeficits(deficits);
        var top = scores.OrderByDescending(kv => kv.Value).First().Key;
        top.Should().Be(ProgressionAxis.Gear, "gear has the highest weight at 1.25");
    }

    [Fact]
    public void ScoreDeficits_picks_imbue_when_gear_satisfied()
    {
        var deficits = new Dictionary<ProgressionAxis, double>
        {
            [ProgressionAxis.Gear]      = 0.0,
            [ProgressionAxis.Imbue]     = 0.5,
            [ProgressionAxis.Companion] = 0.5,
            [ProgressionAxis.Player]    = 0.5,
        };
        var scores = AutoProgressionService.ScoreDeficits(deficits);
        var top = scores.OrderByDescending(kv => kv.Value).First().Key;
        top.Should().Be(ProgressionAxis.Imbue);
    }

    [Fact]
    public void ScoreDeficits_picks_larger_raw_deficit_across_weight_boundary()
    {
        // Player deficit of 1.0 (weighted 0.9) must beat gear deficit of 0.5 (weighted 0.625).
        var deficits = new Dictionary<ProgressionAxis, double>
        {
            [ProgressionAxis.Gear]      = 0.5,
            [ProgressionAxis.Imbue]     = 0.0,
            [ProgressionAxis.Companion] = 0.0,
            [ProgressionAxis.Player]    = 1.0,
        };
        var scores = AutoProgressionService.ScoreDeficits(deficits);
        var top = scores.OrderByDescending(kv => kv.Value).First().Key;
        top.Should().Be(ProgressionAxis.Player);
    }

    [Theory]
    [InlineData(ProgressionAxis.Gear,      ProgressionMode.Craft,   false)]
    [InlineData(ProgressionAxis.Imbue,     ProgressionMode.Imbue,   false)]
    [InlineData(ProgressionAxis.Companion, ProgressionMode.Capture, true)]
    [InlineData(ProgressionAxis.Player,    ProgressionMode.Farm,    true)]
    [InlineData(ProgressionAxis.None,      ProgressionMode.Farm,    true)]
    public void MapAxisToSubMode_returns_expected_mode_and_dispatch_flag(
        ProgressionAxis axis, ProgressionMode expectedMode, bool expectedDispatched)
    {
        var (mode, dispatched, _) = AutoProgressionService.MapAxisToSubMode(axis);
        mode.Should().Be(expectedMode);
        dispatched.Should().Be(expectedDispatched);
    }

    [Fact]
    public void MapAxisToSubMode_stubbed_modes_include_reason_pointer()
    {
        var (_, _, reasonGear)  = AutoProgressionService.MapAxisToSubMode(ProgressionAxis.Gear);
        var (_, _, reasonImbue) = AutoProgressionService.MapAxisToSubMode(ProgressionAxis.Imbue);
        reasonGear.Should().Contain("gap.md");
        reasonImbue.Should().Contain("gap.md");
    }

    [Fact]
    public void SessionStore_start_is_idempotent_and_replaces_prior()
    {
        var store = new AutoProgressionSessionStore();
        var pid = Guid.NewGuid();
        var a = store.Start(pid);
        var b = store.Start(pid);
        store.IsActive(pid).Should().BeTrue();
        b.Should().NotBeSameAs(a, "restart should give a fresh session");
        store.End(pid);
        store.IsActive(pid).Should().BeFalse();
    }
}
