using FirstMud.GameServer.Services;
using FluentAssertions;

namespace FirstMud.Tests.Services;

/// <summary>
/// Regression tests for the playtest bug where procgen quests referenced
/// nonexistent item names ("healing herbs", "fungal spore", "Thornwood resin")
/// that never matched player inventory. The biome resolver must always emit a
/// REAL item name that HarvestCommandHandler can actually produce in that zone.
/// </summary>
public class DungeonMasterBiomeResolverTests
{
    // The full roster of real harvest output item names emitted by
    // HarvestCommandHandler. Anything the resolver returns must be in this set.
    private static readonly HashSet<string> RealHarvestableItems =
    [
        "Copper Ore", "Iron Ore", "Silver Ore", "Mithril Ore",
        "Stone", "Granite Block", "Gravel",
        "Sage", "Fire Moss", "Lavender", "Nightshade", "Mint",
        "Bogweed", "Marsh Gas Crystal", "Thornroot", "Sand Thistle",
        "Oak Wood", "Thornwood", "Ashwood", "Bogwood",
        "Sand", "Coral Fragment", "Sea Kelp",
        "Bog Iron", "Obsidian Shard", "Tin Nugget",
        "Dravenite Dust", "Wyrdstone",
    ];

    // Must stay in sync with DungeonMasterService.SafeQuestZoneNames.
    // Listed here so the test fails loudly if someone adds a zone to the
    // procgen pool without wiring up its biome in GetBiomeForZone.
    private static readonly string[] SafeQuestZones =
    [
        "the Caervorn Highlands", "the Thornwood", "Portmere", "the Gravenmarsh",
        "the Drowned Coast", "the Ashen Reach", "the Starting Road", "Gravenhold",
        "Coldmere", "Ironspire Ridge", "the Rootweave",
        "the Tidegate", "the Pale City", "the Sundering Scar", "Ashcross", "Veldann",
    ];

    [Fact]
    public void ResolveZoneResourceItem_ReturnsRealItemName_ForEverySafeZone()
    {
        foreach (var zone in SafeQuestZones)
        {
            var item = DungeonMasterService.ResolveZoneResourceItem(zone);
            RealHarvestableItems.Should().Contain(item,
                $"zone '{zone}' must resolve to a real harvestable item name, got '{item}'");
        }
    }

    [Fact]
    public void ResolveZoneResourceItem_NeverReturnsLegacyGenericStrings()
    {
        // These are the exact noun phrases from the old QuestTitleTemplates that
        // broke completion matching in the playtest bug report.
        var legacyBadStrings = new[]
        {
            "healing herbs", "fungal spore", "Thornwood resin",
            "beast hides", "Compact spice", "wyrd-thread",
        };

        foreach (var zone in SafeQuestZones)
        {
            var item = DungeonMasterService.ResolveZoneResourceItem(zone);
            legacyBadStrings.Should().NotContain(item,
                $"zone '{zone}' resolved to legacy generic string '{item}'");
        }
    }

    [Fact]
    public void GetBiomeForZone_UnknownZone_FallsBackToPlains()
    {
        DungeonMasterService.GetBiomeForZone("Some Unknown Zone").Should().Be("plains");
    }
}
