using System.IO;
using System.Linq;
using FirstMud.DesignTools.Shared;
using FirstMud.DesignTools.Tools.PlaybookRunner;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Herb tiering economy playbook tests. Validates the static shape of
/// tools/design/playbooks/herb-supply.json and the content-side consistency
/// of the tier-1/2/3 herb item + seed names referenced by the playbook.
///
/// This is intentionally a "loads + matches" regression test rather than a
/// simulation: the PlaybookRunner does not yet execute kind="economy" cells.
/// When the economy simulator is built, this file provides the canonical
/// expected bands.
/// </summary>
public class HerbSupplyPlaybookTests
{
    private static string PlaybookPath() =>
        Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", "herb-supply.json");

    [Fact]
    public void Playbook_Loads_And_Has_Required_Shape()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());

        Assert.Equal("herb-supply", pb.Id);
        Assert.Equal("economy", pb.Kind);
        Assert.Contains(pb.Axes, a => a.Name == "biome");
        Assert.Contains(pb.Axes, a => a.Name == "playerHours");
        Assert.NotEmpty(pb.ExpectedDistribution.Cells);
    }

    [Fact]
    public void Playbook_Contains_Cells_For_All_Seven_Biomes_Plus_Greenhouse()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        // Seven biomes (1..7) + one synthetic "greenhouse" cell (biome=0).
        var biomeIds = pb.ExpectedDistribution.Cells
            .Select(c => c.Axis("biome"))
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .OrderBy(v => v)
            .ToArray();
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5, 6, 7 }, biomeIds);
    }

    [Fact]
    public void Tier3_Drop_Rate_Is_Zero_Except_Greenhouse()
    {
        var pb = PlaybookRunnerCommand.LoadPlaybook(PlaybookPath());
        foreach (var cell in pb.ExpectedDistribution.Cells)
        {
            var biome = cell.Axis("biome");
            var expected = cell.Expected();
            if (!expected.TryGetValue("tier3PerHour", out var band)) continue;

            if (biome == 0)
            {
                // Greenhouse cultivation row: tier-3 is expected > 0.
                Assert.True(band[1] > 0, "Greenhouse row must allow tier-3 yield.");
            }
            else
            {
                // All biome rows: tier-3 herbs must NOT drop directly — they
                // come from Greenhouse cultivation only.
                Assert.Equal(0, band[0]);
                Assert.Equal(0, band[1]);
            }
        }
    }

    [Fact]
    public void All_Thirteen_Herb_Names_Appear_In_LootTables_Or_Are_Tier3()
    {
        // Tier-1 + tier-2 herbs should appear in content/loot-tables.json
        // (they drop from zones). Tier-3 herbs must NOT appear in loot-tables
        // (they come only from Greenhouse cultivation).
        var lootPath = Path.Combine(RepoRoot.ContentDir(), "loot-tables.json");
        var lootJson = File.ReadAllText(lootPath);

        string[] tier1 = { "Sage", "Mint", "Thornroot", "Lavender" };
        string[] tier2 = { "Fire Moss", "Sea Kelp", "Nightshade", "Sand Thistle", "Bogweed" };
        string[] tier3 = { "Moonbloom", "Starflower", "Wyrd Blossom", "Dragon's Breath" };
        string[] seeds = { "Moonbloom Seed", "Starflower Seed", "Wyrd Blossom Seed", "Dragon's Breath Seed" };

        foreach (var t1 in tier1)
            Assert.Contains($"\"{t1}\"", lootJson);
        foreach (var t2 in tier2)
            Assert.Contains($"\"{t2}\"", lootJson);
        foreach (var seed in seeds)
            Assert.Contains($"\"{seed}\"", lootJson);

        // Tier-3 herb names should not appear as direct drop entries (only
        // as seed names, which include the word as a prefix — so we check for
        // the exact quoted itemName that would be a drop entry).
        foreach (var t3 in tier3)
        {
            // Drop pool entries look like:  "itemName": "Moonbloom",
            // Seed entries use "Moonbloom Seed", which contains the herb name
            // but as a different quoted string. Assert the bare herb name is
            // not present as an itemName value.
            Assert.DoesNotContain($"\"itemName\": \"{t3}\"", lootJson);
        }
    }
}
