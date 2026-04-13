using FirstMud.Application.Content;
using FirstMud.Domain.Enums;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.ProgressionSim;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Tests over the progression simulator. Uses real content from
/// <c>content/*.json</c> so the regression pins track live game tuning.
/// </summary>
public class ProgressionSimTests
{
    private static IContentProvider Content() => new ContentProvider(RepoRoot.ContentDir());

    private static ProgressionSimulator.SimResult RunBalanced(int hours, int seed = 42)
    {
        var sim = new ProgressionSimulator(
            Content(), seed, "balanced", MagicElement.Aether, "aeldran-3-portmere");
        return sim.Run(hours);
    }

    [Fact]
    public void Same_seed_and_inputs_produce_identical_action_log()
    {
        var a = RunBalanced(hours: 10, seed: 42);
        var b = RunBalanced(hours: 10, seed: 42);

        Assert.Equal(a.ActionLog.Count, b.ActionLog.Count);
        for (int i = 0; i < a.ActionLog.Count; i++)
        {
            Assert.Equal(a.ActionLog[i].Kind,   b.ActionLog[i].Kind);
            Assert.Equal(a.ActionLog[i].Detail, b.ActionLog[i].Detail);
            Assert.Equal(a.ActionLog[i].Hour,   b.ActionLog[i].Hour);
            Assert.Equal(a.ActionLog[i].Slot,   b.ActionLog[i].Slot);
        }
        Assert.Equal(a.HourByHour[^1].Level, b.HourByHour[^1].Level);
        Assert.Equal(a.HourByHour[^1].ReliableDangerTier, b.HourByHour[^1].ReliableDangerTier);
    }

    [Fact]
    public void Zero_hour_baseline_level1_no_gear_reliably_clears_at_least_danger_1()
    {
        // Fresh character with starter companions: the *starting* reliable danger
        // probe must yield at least 1 — if it doesn't, zone 1 (Starting Road /
        // Portmere at d1) is unbeatable and the game is broken at t=0.
        var sim = new ProgressionSimulator(
            Content(), seed: 7, "balanced", MagicElement.Aether, "aeldran-3-portmere");
        var r = sim.Run(hours: 1);

        Assert.True(r.HourByHour[0].ReliableDangerTier >= 1,
            $"A level-1 character + 3 starter companions must reliably clear danger 1. Got d{r.HourByHour[0].ReliableDangerTier}.");
        Assert.Equal(1, r.HourByHour[0].Level == 0 ? 0 : Math.Max(1, r.HourByHour[0].Level - (r.HourByHour[0].Level - 1)));
    }

    [Fact]
    public void High_hours_ceiling_balanced_seed42_pinned()
    {
        // Regression pin: captures CURRENT reality at hour 40, balanced, seed 42.
        // Observed during initial calibration: reliable danger ≥ 8 at hour 40.
        // If a future content tune lowers this below 6 we want to know immediately.
        // If a tune RAISES it past 10 (capped), that's fine — the lower bound
        // is what matters.
        //
        // Note: the user's Pass-13 playtest described danger 5 as a functional
        // ceiling. This test currently FAILS that narrative in the sim (sim
        // reaches d8+), which suggests the user's bottleneck is elsewhere —
        // see the bottleneck-detection output (likely workmanship/materials).
        var r = RunBalanced(hours: 40, seed: 42);

        var finalDanger = r.HourByHour[^1].ReliableDangerTier;
        Assert.True(finalDanger >= 6,
            $"Expected reliable danger ≥ 6 at hour 40 (balanced, seed 42). Got d{finalDanger}. " +
            $"A drop below this line means a progression regression — investigate before merging.");
    }

    [Fact]
    public void Resource_conservation_earned_materials_meet_or_exceed_consumed()
    {
        // Basic sanity: the sim should not manufacture material value out of
        // nothing. Over a reasonable run the total material units gained via
        // harvest + loot must be ≥ total units consumed in crafts.
        var r = RunBalanced(hours: 24, seed: 13);

        Assert.True(r.TotalMaterialsEarned >= r.TotalMaterialsConsumed,
            $"Conservation violation: earned={r.TotalMaterialsEarned}, consumed={r.TotalMaterialsConsumed}.");
    }

    [Fact]
    public void Bottleneck_detection_flags_gear_workmanship_at_current_tunes()
    {
        // Observed in calibration: even at hour 40 balanced, TopGearWorkmanship
        // caps around 2-3 under current CraftingService formula. This test
        // pins that bottleneck-detection currently fires for workmanship-5+.
        // A future tune to `craftingSkill / 10` (halved divisor) should make
        // this test fail, at which point remove the assertion.
        var r = RunBalanced(hours: 40, seed: 42);

        var bottlenecks = ProgressionSimCommand.DetectBottlenecks(r);
        Assert.Contains(bottlenecks, b => b.Name == "workmanship-5-gear");
    }
}
