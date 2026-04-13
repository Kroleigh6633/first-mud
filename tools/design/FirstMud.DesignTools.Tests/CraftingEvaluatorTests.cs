using System.Text.Json;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Tests the crafting cell evaluator — verifies dispatch on kind="crafting",
/// deterministic outputs across runs, and that outcome percentages sum to ~100.
/// </summary>
public class CraftingEvaluatorTests
{
    private static Playbook TinyCraftingPlaybook() => new()
    {
        Id = "test-crafting-tiny",
        DisplayName = "crafting tiny",
        Kind = "crafting",
        Rolls = 256,
        Seed = 4242,
        Crafting = new CraftingHoldouts
        {
            BaseQuantity = 10,
            PlayerSeed = 42,
            DisplayRounding = 5,
            SeedSpread = 16,
        },
        Axes = new List<Axis>
        {
            new() { Name = "craftingSkill",    Values = new[] { 2, 20 } },
            new() { Name = "recipeDifficulty", Values = new[] { 1, 5 } },
        },
        ExpectedDistribution = new ExpectedDistribution
        {
            Cells = new List<ExpectedDistributionCell>
            {
                BuildExpectedCell(new[] { ("craftingSkill", 2), ("recipeDifficulty", 1) },
                    new Dictionary<string, double[]>
                    {
                        ["Success"]  = new[] { 0.0, 100.0 },
                        ["NearMiss"] = new[] { 0.0, 100.0 },
                    }),
            }
        },
    };

    private static ExpectedDistributionCell BuildExpectedCell(
        (string name, int val)[] axes,
        Dictionary<string, double[]> expected)
    {
        var obj = new Dictionary<string, object> { ["expected"] = expected };
        foreach (var (n, v) in axes) obj[n] = v;
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<ExpectedDistributionCell>(json)!;
    }

    [Fact]
    public void Crafting_evaluator_produces_deterministic_output()
    {
        var pb = TinyCraftingPlaybook();
        var evaluator = new CraftingEvaluator();

        var a = evaluator.Execute(pb);
        var b = evaluator.Execute(pb);

        Assert.Equal(a.Cells.Count, b.Cells.Count);
        Assert.Equal(4, a.Cells.Count); // 2 x 2 axis grid
        for (int i = 0; i < a.Cells.Count; i++)
        {
            foreach (var kv in a.Cells[i].OutcomePercentages)
                Assert.Equal(kv.Value, b.Cells[i].OutcomePercentages[kv.Key], 6);
            Assert.Equal(a.Cells[i].TotalRolls, b.Cells[i].TotalRolls);
        }
    }

    [Fact]
    public void Crafting_evaluator_outcome_percentages_sum_to_one_hundred()
    {
        var pb = TinyCraftingPlaybook();
        var result = new CraftingEvaluator().Execute(pb);

        foreach (var cell in result.Cells)
        {
            var sum = cell.OutcomePercentages.Values.Sum();
            Assert.InRange(sum, 99.99, 100.01);
            Assert.True(cell.TotalRolls > 0);
        }
    }
}
