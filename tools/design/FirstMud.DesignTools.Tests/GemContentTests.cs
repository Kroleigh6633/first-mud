using System.IO;
using System.Linq;
using System.Text.Json;
using FirstMud.DesignTools.Shared;
using Xunit;

namespace FirstMud.DesignTools.Tests;

/// <summary>
/// Content-layer tests for the gems subsystem. Verify that:
///  1. The gem-supply playbook is well-formed and references gems that exist
///     in imbue-recipes.json, and that every biome in its biomeCoverage map
///     is actually produceable via loot-tables.json dropPools.
///  2. imbue-recipes.json is structurally valid and every recipe references a
///     gem that exists both in the gem catalog AND in the loot-tables content
///     (so a player can actually obtain the gem the recipe consumes).
///
/// These tests guard the content contract; they do NOT exercise ImbueService
/// (which still resolves imbue type from taper name and is untouched by this
/// change). Wiring imbue-recipes.json into ImbueService is a follow-up.
/// </summary>
public class GemContentTests
{
    private static string Repo() => RepoRoot.Find();

    private static JsonDocument LoadJson(string relativePath)
    {
        var fullPath = Path.Combine(Repo(), relativePath);
        Assert.True(File.Exists(fullPath), $"missing content file: {relativePath}");
        var text = File.ReadAllText(fullPath);
        return JsonDocument.Parse(text);
    }

    [Fact]
    public void ImbueRecipes_content_is_structurally_valid_and_gems_appear_in_loot_tables()
    {
        using var imbueRecipes = LoadJson("content/imbue-recipes.json");
        using var lootTables   = LoadJson("content/loot-tables.json");

        var root = imbueRecipes.RootElement;
        Assert.True(root.TryGetProperty("gems", out var gems), "imbue-recipes.json missing 'gems' array");
        Assert.True(root.TryGetProperty("recipes", out var recipes), "imbue-recipes.json missing 'recipes' array");
        Assert.True(root.TryGetProperty("compatibility", out _), "imbue-recipes.json missing 'compatibility' block");

        // Build gem id → displayName map and assert required fields exist.
        var gemIdToDisplayName = new Dictionary<string, string>();
        foreach (var gem in gems.EnumerateArray())
        {
            var id = gem.GetProperty("id").GetString();
            var displayName = gem.GetProperty("displayName").GetString();
            var tier = gem.GetProperty("tier").GetInt32();
            var alignment = gem.GetProperty("alignment").GetString();

            Assert.False(string.IsNullOrWhiteSpace(id), "gem id is empty");
            Assert.False(string.IsNullOrWhiteSpace(displayName), $"gem {id} missing displayName");
            Assert.InRange(tier, 1, 3);
            Assert.False(string.IsNullOrWhiteSpace(alignment), $"gem {id} missing alignment");

            gemIdToDisplayName[id!] = displayName!;
        }

        Assert.Equal(8, gemIdToDisplayName.Count);
        Assert.Contains("quartz",      gemIdToDisplayName.Keys);
        Assert.Contains("obsidian",    gemIdToDisplayName.Keys);
        Assert.Contains("amethyst",    gemIdToDisplayName.Keys);
        Assert.Contains("emerald",     gemIdToDisplayName.Keys);
        Assert.Contains("topaz",       gemIdToDisplayName.Keys);
        Assert.Contains("aquamarine",  gemIdToDisplayName.Keys);
        Assert.Contains("diamond",     gemIdToDisplayName.Keys);
        Assert.Contains("black-pearl", gemIdToDisplayName.Keys);

        // Every recipe must reference a known gem.
        foreach (var recipe in recipes.EnumerateArray())
        {
            var recipeId = recipe.GetProperty("id").GetString();
            var gemId = recipe.GetProperty("gemId").GetString();
            Assert.NotNull(gemId);
            Assert.True(gemIdToDisplayName.ContainsKey(gemId!),
                $"recipe {recipeId} references unknown gemId '{gemId}'");
        }

        // Every gem displayName must appear as an itemName in loot-tables dropPools
        // so the gem is actually obtainable in-game.
        var lootItemNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pool in lootTables.RootElement.GetProperty("dropPools").EnumerateArray())
        {
            foreach (var entry in pool.GetProperty("entries").EnumerateArray())
            {
                var name = entry.GetProperty("itemName").GetString();
                if (name != null) lootItemNames.Add(name);
            }
        }

        foreach (var (id, displayName) in gemIdToDisplayName)
        {
            Assert.True(lootItemNames.Contains(displayName),
                $"gem '{id}' (displayName '{displayName}') not found in any loot-tables dropPool — players cannot obtain it");
        }
    }

    [Fact]
    public void GemSupply_playbook_is_valid_and_its_biome_coverage_matches_loot_tables()
    {
        var playbookPath = Path.Combine(Repo(), "tools", "design", "playbooks", "economy", "gem-supply.json");
        Assert.True(File.Exists(playbookPath), "gem-supply.json missing");

        using var playbook   = JsonDocument.Parse(File.ReadAllText(playbookPath));
        using var lootTables = LoadJson("content/loot-tables.json");
        using var imbueRecipes = LoadJson("content/imbue-recipes.json");

        var root = playbook.RootElement;
        Assert.Equal("gem-supply", root.GetProperty("id").GetString());
        Assert.Equal("economy",    root.GetProperty("kind").GetString());

        // Band definitions present for tiers 1, 2, 3.
        var bands = root.GetProperty("expectedYield").GetProperty("bands");
        Assert.True(bands.TryGetProperty("1", out _));
        Assert.True(bands.TryGetProperty("2", out _));
        Assert.True(bands.TryGetProperty("3", out _));

        // Build: gem id → displayName (from imbue-recipes.json).
        var gemIdToDisplayName = imbueRecipes.RootElement
            .GetProperty("gems")
            .EnumerateArray()
            .ToDictionary(
                g => g.GetProperty("id").GetString()!,
                g => g.GetProperty("displayName").GetString()!);

        // Build: biome → set of itemNames (from loot-tables dropPools, matched
        // via biomeDropPoolMap).
        var poolById = lootTables.RootElement.GetProperty("dropPools")
            .EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("id").GetString()!,
                p => p.GetProperty("entries").EnumerateArray()
                      .Select(e => e.GetProperty("itemName").GetString()!)
                      .ToHashSet());

        var biomeToItemNames = lootTables.RootElement.GetProperty("biomeDropPoolMap")
            .EnumerateArray()
            .ToDictionary(
                m => m.GetProperty("biome").GetString()!,
                m => poolById[m.GetProperty("poolId").GetString()!]);

        // Every (biome, gemId) claim in biomeCoverage must be obtainable in that
        // biome's drop pool (via displayName match).
        var coverage = root.GetProperty("expectedYield").GetProperty("biomeCoverage");
        foreach (var biomeProp in coverage.EnumerateObject())
        {
            if (biomeProp.Name == "comment") continue;
            var biome = biomeProp.Name;
            Assert.True(biomeToItemNames.ContainsKey(biome),
                $"gem-supply playbook lists biome '{biome}' but it is not in loot-tables biomeDropPoolMap");

            var itemsInBiome = biomeToItemNames[biome];
            foreach (var gemIdElem in biomeProp.Value.EnumerateArray())
            {
                var gemId = gemIdElem.GetString()!;
                Assert.True(gemIdToDisplayName.ContainsKey(gemId),
                    $"gem-supply biomeCoverage references unknown gem '{gemId}' in biome '{biome}'");
                var displayName = gemIdToDisplayName[gemId];
                Assert.True(itemsInBiome.Contains(displayName),
                    $"gem-supply claims biome '{biome}' produces '{gemId}' ({displayName}) but that item is not in the biome's drop pool");
            }
        }
    }
}
