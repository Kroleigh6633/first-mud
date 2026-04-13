using System.IO;
using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FluentAssertions;

namespace FirstMud.Tests.Content;

/// <summary>
/// Tests for the loot-tables content pipeline. Uses temp dirs seeded with
/// minimal-but-valid consumables.json + loot-tables.json so each invariant
/// can be violated independently without touching the real repo files.
/// </summary>
public class LootTablesContentTests
{
    [Fact]
    public void Real_loot_tables_file_loads_and_exposes_expected_pools()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        provider.LootTables.EquipmentTemplates.Should().NotBeEmpty();
        provider.AllDropPools().Should().Contain(p => p.Id == "common-materials");
        provider.AllDropPools().Should().Contain(p => p.Id == "biome-mountain");
        provider.AllDropPools().Should().Contain(p => p.Id == "loot-consumables");
        provider.AllDropPools().Should().Contain(p => p.Id == "loot-tapers");

        provider.GetDropPool("biome-wyrd").Should().NotBeNull();
        provider.GetDropPool("does-not-exist").Should().BeNull();

        // Tier curves are informational; 0..10 must be present.
        provider.GetTierCurve(0).Should().NotBeNull();
        provider.GetTierCurve(10).Should().NotBeNull();
        provider.GetTierCurve(99).Should().BeNull();

        // Zone → biome mapping matches historical switch.
        provider.LootTables.BiomeByZoneName["The Thornwood"].Should().Be("forest");
        provider.LootTables.BiomeByZoneName["The Maw Borderlands"].Should().Be("wyrd");

        // Biome → imbue matches historical switch.
        provider.LootTables.ImbueByBiome["desert"].Should().Be(ImbueType.Fire);
        provider.LootTables.ImbueByBiome["wyrd"].Should().Be(ImbueType.Wyrd);
    }

    [Fact]
    public void Unknown_pool_reference_in_biomeDropPoolMap_throws()
    {
        var dir = MakeTempContent(lootTables: """
        {
          "equipmentTemplates": [
            { "name": "X", "description": "", "category": "Weapon", "slot": "MeleeWeapon", "minWorkmanship": 1, "maxWorkmanship": 1 }
          ],
          "dropPools": [
            { "id": "p1", "entries": [
              { "itemName": "a", "description": "", "category": "Component", "minWorkmanship": 1, "maxWorkmanship": 1, "weight": 1 }
            ]}
          ],
          "biomeZones": [],
          "biomeImbueTypes": [],
          "biomeDropPoolMap": [ { "biome": "plains", "poolId": "ghost-pool" } ],
          "independentRolls": [],
          "dropChance": { "base": 40, "perDangerLevel": 4, "autoFarmMultiplier": 0.6 },
          "biomeMaterialConfig": { "commonMaterialChancePercent": 30, "dangerSuppressCommonAt": 8, "dangerTier2MinAt": 8, "baseWeight": 10, "rarityWeightPerTier": 2 },
          "tierWorkmanshipCurves": [],
          "preImbue": { "dangerThreshold": 8, "chancePercent": 10, "imbueStrength": 0.2 }
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*ghost-pool*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Unknown_monsterDrop_pool_reference_throws()
    {
        var dir = MakeTempContent(lootTables: """
        {
          "equipmentTemplates": [
            { "name": "X", "description": "", "category": "Weapon", "slot": "MeleeWeapon", "minWorkmanship": 1, "maxWorkmanship": 1 }
          ],
          "dropPools": [
            { "id": "p1", "entries": [
              { "itemName": "a", "description": "", "category": "Component", "minWorkmanship": 1, "maxWorkmanship": 1, "weight": 1 }
            ]}
          ],
          "biomeZones": [], "biomeImbueTypes": [], "biomeDropPoolMap": [], "independentRolls": [],
          "dropChance": { "base": 40, "perDangerLevel": 4, "autoFarmMultiplier": 0.6 },
          "biomeMaterialConfig": { "commonMaterialChancePercent": 30, "dangerSuppressCommonAt": 8, "dangerTier2MinAt": 8, "baseWeight": 10, "rarityWeightPerTier": 2 },
          "tierWorkmanshipCurves": [],
          "preImbue": { "dangerThreshold": 8, "chancePercent": 10, "imbueStrength": 0.2 },
          "monsterDrops": [ { "monsterId": "boar", "pools": ["not-a-pool"], "rollsMin": 1, "rollsMax": 1 } ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*not-a-pool*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Negative_or_zero_weight_throws()
    {
        var dir = MakeTempContent(lootTables: """
        {
          "equipmentTemplates": [
            { "name": "X", "description": "", "category": "Weapon", "slot": "MeleeWeapon", "minWorkmanship": 1, "maxWorkmanship": 1 }
          ],
          "dropPools": [
            { "id": "p1", "entries": [
              { "itemName": "a", "description": "", "category": "Component", "minWorkmanship": 1, "maxWorkmanship": 1, "weight": 0 }
            ]}
          ],
          "biomeZones": [], "biomeImbueTypes": [], "biomeDropPoolMap": [], "independentRolls": [],
          "dropChance": { "base": 40, "perDangerLevel": 4, "autoFarmMultiplier": 0.6 },
          "biomeMaterialConfig": { "commonMaterialChancePercent": 30, "dangerSuppressCommonAt": 8, "dangerTier2MinAt": 8, "baseWeight": 10, "rarityWeightPerTier": 2 },
          "tierWorkmanshipCurves": [],
          "preImbue": { "dangerThreshold": 8, "chancePercent": 10, "imbueStrength": 0.2 }
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*weight*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Tier_curve_with_max_less_than_min_throws()
    {
        var dir = MakeTempContent(lootTables: """
        {
          "equipmentTemplates": [
            { "name": "X", "description": "", "category": "Weapon", "slot": "MeleeWeapon", "minWorkmanship": 1, "maxWorkmanship": 1 }
          ],
          "dropPools": [
            { "id": "p1", "entries": [
              { "itemName": "a", "description": "", "category": "Component", "minWorkmanship": 1, "maxWorkmanship": 1, "weight": 1 }
            ]}
          ],
          "biomeZones": [], "biomeImbueTypes": [], "biomeDropPoolMap": [], "independentRolls": [],
          "dropChance": { "base": 40, "perDangerLevel": 4, "autoFarmMultiplier": 0.6 },
          "biomeMaterialConfig": { "commonMaterialChancePercent": 30, "dangerSuppressCommonAt": 8, "dangerTier2MinAt": 8, "baseWeight": 10, "rarityWeightPerTier": 2 },
          "tierWorkmanshipCurves": [ { "tier": 3, "minWorkmanship": 5, "maxWorkmanship": 2, "dangerBonus": 0 } ],
          "preImbue": { "dangerThreshold": 8, "chancePercent": 10, "imbueStrength": 0.2 }
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*tier*3*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Duplicate_pool_ids_throw()
    {
        var dir = MakeTempContent(lootTables: """
        {
          "equipmentTemplates": [
            { "name": "X", "description": "", "category": "Weapon", "slot": "MeleeWeapon", "minWorkmanship": 1, "maxWorkmanship": 1 }
          ],
          "dropPools": [
            { "id": "p1", "entries": [
              { "itemName": "a", "description": "", "category": "Component", "minWorkmanship": 1, "maxWorkmanship": 1, "weight": 1 }
            ]},
            { "id": "p1", "entries": [
              { "itemName": "b", "description": "", "category": "Component", "minWorkmanship": 1, "maxWorkmanship": 1, "weight": 1 }
            ]}
          ],
          "biomeZones": [], "biomeImbueTypes": [], "biomeDropPoolMap": [], "independentRolls": [],
          "dropChance": { "base": 40, "perDangerLevel": 4, "autoFarmMultiplier": 0.6 },
          "biomeMaterialConfig": { "commonMaterialChancePercent": 30, "dangerSuppressCommonAt": 8, "dangerTier2MinAt": 8, "baseWeight": 10, "rarityWeightPerTier": 2 },
          "tierWorkmanshipCurves": [],
          "preImbue": { "dangerThreshold": 8, "chancePercent": 10, "imbueStrength": 0.2 }
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*duplicate drop pool*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Equipment_template_count_matches_migrated_legacy_list()
    {
        // Legacy LootService.Templates had 37 entries (9 slots of gear + 7 material
        // stand-ins). This fence catches accidental drops during future edits.
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        provider.LootTables.EquipmentTemplates.Should().HaveCount(37);
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static DirectoryInfo MakeTempContent(string lootTables)
    {
        var dir = Directory.CreateTempSubdirectory("fm-loot-content-test-");
        File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
        {
          "consumables": [
            { "id": "stub", "matchToken": "stub", "effectType": "Heal", "amount": 1 }
          ]
        }
        """);
        // ContentProvider loads buildings/recipes/monsters before loot-tables.
        // Copy the shipped files so loot-tables validation under test is the
        // first thing to fail.
        ContentProviderTests.WriteValidBuildings(dir.FullName);
        ContentProviderTests.WriteValidRecipes(dir.FullName);
        ContentProviderTests.WriteValidMonsters(dir.FullName);
        File.WriteAllText(Path.Combine(dir.FullName, "loot-tables.json"), lootTables);
        return dir;
    }
}
