using System.Text.Json;
using FirstMud.DesignTools.Shared;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Sanity tests for the analytical economy playbooks (forge-throughput, leather-flow).
/// These playbooks are design specs — JSON files encoding the expected bands.
/// The tests verify they exist, parse, and their internal numbers stay consistent
/// with the constants baked into production code.
/// </summary>
public class EconomyPlaybookTests
{
    private static string PlaybookPath(string id)
        => Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks", id + ".json");

    [Fact]
    public void ForgeThroughput_Playbook_Parses()
    {
        var path = PlaybookPath("forge-throughput");
        Assert.True(File.Exists(path), $"forge-throughput playbook must exist at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("forge-throughput", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("economy-analytical", doc.RootElement.GetProperty("kind").GetString());

        var cells = doc.RootElement.GetProperty("expectedCells");
        Assert.True(cells.GetArrayLength() >= 4);
    }

    [Fact]
    public void ForgeThroughput_ExpectedCells_MatchServiceFormula()
    {
        // Cross-check the playbook's expectedCells against the live throughput
        // formula in HomesteadCompanionService. If either side drifts, this test flips.
        var path = PlaybookPath("forge-throughput");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));

        foreach (var cell in doc.RootElement.GetProperty("expectedCells").EnumerateArray())
        {
            var layer = cell.GetProperty("companionLayer").GetInt32();
            var tier  = cell.GetProperty("forgeTier").GetInt32();
            var expectedTick = cell.GetProperty("unitsPerTick").GetInt32();

            var actual = FirstMud.Application.Services.HomesteadCompanionService.ForgeThroughput(layer, tier);
            Assert.Equal(expectedTick, actual);
        }
    }

    [Fact]
    public void LeatherFlow_Playbook_Parses()
    {
        var path = PlaybookPath("leather-flow");
        Assert.True(File.Exists(path), $"leather-flow playbook must exist at {path}");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("leather-flow", doc.RootElement.GetProperty("id").GetString());
        Assert.Equal("economy-analytical", doc.RootElement.GetProperty("kind").GetString());

        var pre = doc.RootElement.GetProperty("preTuneSnapshot");
        var post = doc.RootElement.GetProperty("postTuneSnapshot");
        Assert.Equal("chronic_deficit", pre.GetProperty("band").GetString());
        Assert.Equal("healthy_surplus", post.GetProperty("band").GetString());
    }

    [Fact]
    public void LeatherFlow_PostTune_AppliedToContent()
    {
        // Verifies the content tune advertised by the playbook is actually present in loot-tables.json.
        var lootPath = Path.Combine(RepoRoot.ContentDir(), "loot-tables.json");
        var json = File.ReadAllText(lootPath);

        // Post-tune: Leather appears in biome-plains and biome-forest pools.
        // Rough textual check — both pools exist above these entries in the file.
        Assert.Contains("biome-plains", json);
        Assert.Contains("biome-forest", json);

        using var doc = JsonDocument.Parse(json);
        var pools = doc.RootElement.GetProperty("dropPools");

        bool plainsHasLeather = false, forestHasLeather = false;
        foreach (var pool in pools.EnumerateArray())
        {
            var pid = pool.GetProperty("id").GetString();
            foreach (var entry in pool.GetProperty("entries").EnumerateArray())
            {
                if (entry.GetProperty("itemName").GetString() == "Leather")
                {
                    if (pid == "biome-plains") plainsHasLeather = true;
                    if (pid == "biome-forest") forestHasLeather = true;
                }
            }
        }
        Assert.True(plainsHasLeather, "Post-tune: biome-plains pool must contain 'Leather'.");
        Assert.True(forestHasLeather, "Post-tune: biome-forest pool must contain 'Leather'.");
    }
}
