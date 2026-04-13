using FirstMud.Application.Services;
using FluentAssertions;

namespace FirstMud.Tests.Application;

/// <summary>
/// Unit tests for the pure helper functions on HomesteadCompanionService that drive
/// the new Forge auto-smelt duty. The full tick flow is covered by integration tests;
/// these pin the throughput curve and the per-unit ore-roll distribution so the
/// forge-throughput economy playbook stays deterministic.
/// </summary>
public class HomesteadCompanionForgeTests
{
    [Theory]
    [InlineData(1, 1, 1)]   // layer 1, tier 1 forge → 1 unit/tick
    [InlineData(2, 1, 1)]
    [InlineData(3, 1, 1)]   // layer 3 baseUnits = 1
    [InlineData(4, 1, 2)]
    [InlineData(5, 1, 2)]
    [InlineData(6, 1, 3)]
    [InlineData(3, 2, 1)]   // tier 2 adds (1)*3/5 = 0 → still 1
    [InlineData(5, 2, 3)]   // 2 + (1)*5/5 = 3
    [InlineData(5, 3, 4)]   // 2 + (2)*5/5 = 4
    [InlineData(6, 3, 5)]   // 3 + (2)*6/5 = 3+2 = 5
    public void ForgeThroughput_ScalesWithLayerAndTier(int layer, int tier, int expected)
    {
        HomesteadCompanionService.ForgeThroughput(layer, tier).Should().Be(expected);
    }

    [Fact]
    public void ForgeThroughput_AlwaysAtLeastOne()
    {
        for (var l = 1; l <= 6; l++)
            for (var t = 1; t <= 3; t++)
                HomesteadCompanionService.ForgeThroughput(l, t).Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public void RollForgeOre_ReturnsKnownOreNames()
    {
        var rng = new Random(42);
        var seen = new HashSet<string>();
        for (var i = 0; i < 5000; i++)
            seen.Add(HomesteadCompanionService.RollForgeOre(6, rng));

        seen.Should().BeSubsetOf(new[] { "Iron Ore", "Copper Nugget", "Tin", "Silver Ore", "Mithril Ore" });
        seen.Should().Contain(new[] { "Iron Ore", "Copper Nugget", "Tin", "Silver Ore" });
    }

    [Fact]
    public void RollForgeOre_Layer1_IsMostlyIron()
    {
        var rng = new Random(123);
        var iron = 0;
        const int trials = 20000;
        for (var i = 0; i < trials; i++)
            if (HomesteadCompanionService.RollForgeOre(1, rng) == "Iron Ore") iron++;

        // Layer 1 baseline is 50% Iron. ±3% tolerance.
        var rate = iron / (double)trials;
        rate.Should().BeInRange(0.47, 0.53);
    }

    [Fact]
    public void RollForgeOre_HigherLayer_ShiftsTowardRarer()
    {
        var rng1 = new Random(7);
        var rng6 = new Random(7);
        var iron1 = 0; var iron6 = 0;
        const int trials = 20000;
        for (var i = 0; i < trials; i++)
        {
            if (HomesteadCompanionService.RollForgeOre(1, rng1) == "Iron Ore") iron1++;
            if (HomesteadCompanionService.RollForgeOre(6, rng6) == "Iron Ore") iron6++;
        }
        iron6.Should().BeLessThan(iron1, "higher-layer companions should shift Iron probability into rarer ores.");
    }
}
