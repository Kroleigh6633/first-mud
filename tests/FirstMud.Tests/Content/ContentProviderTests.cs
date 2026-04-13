using System.IO;
using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace FirstMud.Tests.Content;

public class ContentProviderTests
{
    /// <summary>
    /// Loads the real content/consumables.json shipped with the repo and
    /// asserts every effect that existed in the old hardcoded switch
    /// (UseConsumableCommandHandler.ResolveEffect) still resolves identically.
    /// This is the regression fence for the data-migration.
    /// </summary>
    [Fact]
    public void Real_consumables_file_reproduces_legacy_switch_behavior()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        AssertHeal(provider, "Minor Healing Draught", 30);
        AssertHeal(provider, "Healing Potion", 60);
        AssertHeal(provider, "Greater Healing Elixir", 100);

        AssertWeave(provider, "Weave Tincture", 20);
        AssertWeave(provider, "Weave Elixir", 50);

        AssertBuff(provider, "Fortitude Brew", "MaxHpBonus", 0.10f);
        AssertBuff(provider, "Speed Draught", "SpeedBonus", 0.20f);
        AssertBuff(provider, "Strength Tonic", "StrikeDamageBonus", 0.15f);

        // Unknown items still return null, just like the old switch.
        provider.ResolveConsumable("Moldy Bread").Should().BeNull();
        provider.ResolveConsumable("").Should().BeNull();
    }

    [Fact]
    public void Priority_groups_are_sorted_low_to_high_potency()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var healing = provider.ConsumablesByGroup("Healing");
        healing.Select(c => c.Id).Should().ContainInOrder(
            "minor-healing-draught", "healing-potion", "greater-healing-elixir");

        var weave = provider.ConsumablesByGroup("Weave");
        weave.Select(c => c.Id).Should().ContainInOrder(
            "weave-tincture", "weave-elixir");
    }

    [Fact]
    public void Reload_repopulates_from_disk()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        var countBefore = provider.Consumables.Count;
        provider.Reload();
        provider.Consumables.Should().HaveCount(countBefore);
    }

    [Fact]
    public void Missing_consumables_file_throws_on_load()
    {
        var emptyDir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            var act = () => new ContentProvider(emptyDir.FullName);
            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            emptyDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_effect_type_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
            {
              "consumables": [
                { "id": "bogus", "matchToken": "bogus", "effectType": "Nuke", "amount": 0 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid effectType*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Buff_without_buffKey_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
            {
              "consumables": [
                { "id": "bad-buff", "matchToken": "bad buff", "effectType": "Buff", "amount": 0 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid buffKey*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_ids_throw_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
            {
              "consumables": [
                { "id": "dup", "matchToken": "a", "effectType": "Heal", "amount": 1 },
                { "id": "dup", "matchToken": "b", "effectType": "Heal", "amount": 2 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ─── Monsters ─────────────────────────────────────────────────────────

    [Fact]
    public void Real_monsters_file_loads_and_exposes_known_ids()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        provider.AllMonsters().Should().NotBeEmpty("monsters.json ships with content");

        var goat = provider.GetMonster("mountain-goat");
        goat.Should().NotBeNull();
        goat!.Name.Should().Be("Mountain Goat");
        goat.Biome.Should().Be("mountain");
        goat.Tier.Should().Be(0);
        goat.Hp.Should().Be(22);
        goat.Speed.Should().Be(7);
        goat.Level.Should().Be(1);
        goat.Abilities.Should().HaveCount(2);
        goat.Abilities[0].Name.Should().Be("Headbutt");

        provider.MonstersByBiomeAndTier("mountain", 0).Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Unknown_monster_id_returns_null()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        provider.GetMonster("no-such-beastie").Should().BeNull();
        provider.GetMonster("").Should().BeNull();
    }

    [Fact]
    public void Invalid_monster_element_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "oops", "name": "Oops", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Cosmic",
                  "abilities": ["bite"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid element*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_monster_tier_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "too-hot", "name": "Too Hot", "biome": "plains", "tier": 9,
                  "hp": 10, "speed": 5, "level": 1, "element": "Fire",
                  "abilities": ["bite"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*tier*outside range*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_monster_ids_throw_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "dup", "name": "A", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Fire", "abilities": ["bite"] },
                { "id": "dup", "name": "B", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Fire", "abilities": ["bite"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate monster id*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Monster_referencing_unknown_ability_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "ghost-ref", "name": "Ghost", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Aether",
                  "abilities": ["no-such-ability"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*unknown ability*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void WriteStubConsumables(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "consumables.json"), """
        {
          "consumables": [
            { "id": "x", "matchToken": "x", "effectType": "Heal", "amount": 1 }
          ]
        }
        """);
        // Monsters tests exercise the ContentProvider end-to-end, which
        // requires the full content fan-out (buildings/recipes/loot) to be
        // present for their own validators. Copy shipped content to unblock
        // the monster-specific assertion under test.
        WriteValidBuildings(dir);
        WriteValidRecipes(dir);
        WriteValidLootTables(dir);
        WriteValidZones(dir);
        WriteValidQuests(dir);
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static void AssertHeal(ContentProvider p, string itemName, int expected)
    {
        var c = p.ResolveConsumable(itemName);
        c.Should().NotBeNull($"'{itemName}' should resolve");
        c!.EffectType.Should().Be("Heal");
        c.Amount.Should().Be(expected);
    }

    private static void AssertWeave(ContentProvider p, string itemName, int expected)
    {
        var c = p.ResolveConsumable(itemName);
        c.Should().NotBeNull();
        c!.EffectType.Should().Be("RestoreWeave");
        c.Amount.Should().Be(expected);
    }

    private static void AssertBuff(ContentProvider p, string itemName, string key, float value)
    {
        var c = p.ResolveConsumable(itemName);
        c.Should().NotBeNull();
        c!.EffectType.Should().Be("Buff");
        c.BuffKey.Should().Be(key);
        c.BuffValue.Should().BeApproximately(value, 0.0001f);
    }

    // ─── Building definitions ─────────────────────────────────────────────

    /// <summary>
    /// Loads the real content/buildings.json and asserts every numeric value
    /// that used to live in the hardcoded dictionaries at the top of
    /// BuildingService is preserved exactly. This is the regression fence
    /// for the data-migration — any drift here changes gameplay.
    /// </summary>
    [Fact]
    public void Real_buildings_file_reproduces_legacy_dictionaries()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        // Every enum value must be present
        foreach (var type in Enum.GetValues<BuildingType>())
            provider.GetBuilding(type).Should().NotBeNull($"{type} must have a definition");

        // Construction costs — spot check a representative sample of the 15 entries
        AssertCost(provider, BuildingType.Forge, ("Wood", 10), ("Stone", 5), ("Iron Ore", 5));
        AssertCost(provider, BuildingType.Fletcher, ("Wood", 10), ("Sinew", 3));
        AssertCost(provider, BuildingType.EnchantingTower, ("Stone", 10), ("Dravenite Dust", 3), ("Wood", 5));
        AssertCost(provider, BuildingType.Hut, ("Wood", 5), ("Stone", 3));
        AssertCost(provider, BuildingType.Greenhouse, ("Wood", 8), ("Stone", 5), ("Sand", 3));
        AssertCost(provider, BuildingType.Woodworker, ("Wood", 10));

        // Worker capacities — all 14 production entries
        provider.GetBuilding(BuildingType.Forge)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.Tannery)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.Farm)!.WorkerCapacity.Should().Be(3);
        provider.GetBuilding(BuildingType.Mine)!.WorkerCapacity.Should().Be(3);
        provider.GetBuilding(BuildingType.Woodworker)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.AlchemistHut)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.Stoneworker)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.EnchantingTower)!.WorkerCapacity.Should().Be(1);
        provider.GetBuilding(BuildingType.MarketStall)!.WorkerCapacity.Should().Be(1);
        provider.GetBuilding(BuildingType.Library)!.WorkerCapacity.Should().Be(1);
        provider.GetBuilding(BuildingType.Barracks)!.WorkerCapacity.Should().Be(5);
        provider.GetBuilding(BuildingType.Warehouse)!.WorkerCapacity.Should().Be(1);
        provider.GetBuilding(BuildingType.Fletcher)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.Greenhouse)!.WorkerCapacity.Should().Be(2);
        provider.GetBuilding(BuildingType.Hut)!.WorkerCapacity.Should().Be(0);

        // Duties — all 14 production entries; Hut must be null
        provider.GetBuilding(BuildingType.Forge)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.Fletcher)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.Tannery)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.EnchantingTower)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.AlchemistHut)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.Stoneworker)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.Woodworker)!.Duty.Should().Be(HomesteadDuty.Harvester);
        provider.GetBuilding(BuildingType.MarketStall)!.Duty.Should().Be(HomesteadDuty.Crafter);
        provider.GetBuilding(BuildingType.Farm)!.Duty.Should().Be(HomesteadDuty.Harvester);
        provider.GetBuilding(BuildingType.Mine)!.Duty.Should().Be(HomesteadDuty.Harvester);
        provider.GetBuilding(BuildingType.Barracks)!.Duty.Should().Be(HomesteadDuty.Guard);
        provider.GetBuilding(BuildingType.Library)!.Duty.Should().Be(HomesteadDuty.Salvager);
        provider.GetBuilding(BuildingType.Warehouse)!.Duty.Should().Be(HomesteadDuty.Guard);
        provider.GetBuilding(BuildingType.Greenhouse)!.Duty.Should().Be(HomesteadDuty.Harvester);
        provider.GetBuilding(BuildingType.Hut)!.Duty.Should().BeNull();

        // Housing flag
        provider.GetBuilding(BuildingType.Hut)!.IsHousing.Should().BeTrue();
        provider.GetBuilding(BuildingType.Forge)!.IsHousing.Should().BeFalse();

        // Hut tier capacities — exact values from the legacy switch
        provider.GetHutCapacity(BuildingType.Hut, 1).Should().Be(3);
        provider.GetHutCapacity(BuildingType.Hut, 2).Should().Be(5);
        provider.GetHutCapacity(BuildingType.Hut, 3).Should().Be(8);

        // Legacy fallback: unknown tier returns tier-1 value
        provider.GetHutCapacity(BuildingType.Hut, 0).Should().Be(3);
        provider.GetHutCapacity(BuildingType.Hut, 99).Should().Be(3);

        // Non-housing buildings have zero hut capacity regardless of tier
        provider.GetHutCapacity(BuildingType.Forge, 1).Should().Be(0);

        // AllBuildings returns every type
        provider.AllBuildings().Should().HaveCount(Enum.GetValues<BuildingType>().Length);
    }

    [Fact]
    public void Missing_buildings_file_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            // No buildings.json

            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<FileNotFoundException>()
               .Which.FileName.Should().EndWith("buildings.json");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_building_type_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "buildings.json"), """
            {
              "buildings": [
                {
                  "type": "Forge",
                  "constructionCost": [{ "material": "Wood", "quantity": 1 }],
                  "duty": "Crafter",
                  "workerCapacity": 1,
                  "isHousing": false
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*missing definition for BuildingType*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_building_type_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "buildings.json"), """
            {
              "buildings": [
                {
                  "type": "CastleOfDoom",
                  "constructionCost": [{ "material": "Wood", "quantity": 1 }],
                  "duty": "Crafter",
                  "workerCapacity": 1,
                  "isHousing": false
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*unknown building type*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_duty_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "buildings.json"), """
            {
              "buildings": [
                {
                  "type": "Forge",
                  "constructionCost": [{ "material": "Wood", "quantity": 1 }],
                  "duty": "Overlord",
                  "workerCapacity": 1,
                  "isHousing": false
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid duty*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Housing_building_without_hutCapacityByTier_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "buildings.json"), """
            {
              "buildings": [
                {
                  "type": "Hut",
                  "constructionCost": [{ "material": "Wood", "quantity": 1 }],
                  "duty": null,
                  "workerCapacity": 0,
                  "isHousing": true
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*hutCapacityByTier*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_tier_capacity_value_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "buildings.json"), """
            {
              "buildings": [
                {
                  "type": "Hut",
                  "constructionCost": [{ "material": "Wood", "quantity": 1 }],
                  "duty": null,
                  "workerCapacity": 0,
                  "isHousing": true,
                  "hutCapacityByTier": [3, 0, 8]
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*hutCapacityByTier*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Production_building_without_duty_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "buildings.json"), """
            {
              "buildings": [
                {
                  "type": "Forge",
                  "constructionCost": [{ "material": "Wood", "quantity": 1 }],
                  "duty": null,
                  "workerCapacity": 1,
                  "isHousing": false
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*production building*must specify a duty*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void AssertCost(
        ContentProvider p, BuildingType type,
        params (string Material, int Quantity)[] expected)
    {
        var def = p.GetBuilding(type);
        def.Should().NotBeNull();
        def!.ConstructionCost.Select(c => (c.Material, c.Quantity))
            .Should().ContainInOrder(expected);
        def.ConstructionCost.Should().HaveCount(expected.Length);
    }

    private static void WriteValidConsumables(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "consumables.json"), """
        {
          "consumables": [
            { "id": "h1", "matchToken": "h1", "effectType": "Heal", "amount": 1 }
          ]
        }
        """);
    }

    // ─── Recipe definitions ───────────────────────────────────────────────

    /// <summary>
    /// Happy path: the real content/recipes.json loads, covers every recipe
    /// that used to live in the hardcoded StartupSeeder.AllRecipeDefinitions
    /// tuple array, and round-trips a representative entry exactly.
    /// </summary>
    [Fact]
    public void Real_recipes_file_reproduces_legacy_seed_definitions()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        // Spot-check the canonical starter and a master-tier entry to lock
        // in every field the old tuple array carried.
        var ironSword = provider.GetRecipe("IRON_SWORD_001");
        ironSword.Should().NotBeNull();
        ironSword!.Name.Should().Be("Iron Sword");
        ironSword.ResultItemName.Should().Be("Iron Sword");
        ironSword.ResultCategory.Should().Be(ItemCategory.Weapon);
        ironSword.RequiredCraftingSkill.Should().Be(1);
        ironSword.RequiredWorld.Should().Be(WorldId.Aeldran);
        ironSword.RequiredTaperType.Should().BeNull();
        ironSword.BaseWorkmanshipMin.Should().Be(2);
        ironSword.BaseWorkmanshipMax.Should().Be(6);
        ironSword.IsDiscoverable.Should().BeFalse();
        ironSword.Ingredients.Should().HaveCount(2);
        ironSword.Ingredients[0].Category.Should().Be(ItemCategory.Component);
        ironSword.Ingredients[0].Name.Should().Be("Iron Ore");
        ironSword.Ingredients[0].BaseQuantity.Should().Be(3);
        ironSword.Ingredients[1].Name.Should().Be("Wood");
        ironSword.Ingredients[1].BaseQuantity.Should().Be(1);

        var masters = provider.GetRecipe("MASTERS_TONIC_001");
        masters.Should().NotBeNull();
        masters!.RequiredCraftingSkill.Should().Be(100);
        masters.ResultCategory.Should().Be(ItemCategory.Consumable);
        masters.Ingredients.Should().HaveCount(3);

        // Every recipe the old tuple array held must now come from JSON.
        provider.AllRecipes().Should().HaveCountGreaterOrEqualTo(57);

        // Unknown id returns null
        provider.GetRecipe("NONEXISTENT_999").Should().BeNull();
        provider.GetRecipe("").Should().BeNull();
    }

    [Fact]
    public void Missing_recipes_file_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            WriteValidBuildings(dir.FullName);
            // No recipes.json
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<FileNotFoundException>()
               .Which.FileName.Should().EndWith("recipes.json");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_recipe_id_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            WriteValidBuildings(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "recipes.json"), """
            {
              "recipes": [
                { "recipeId": "DUPE_001", "name": "A", "resultItemName": "A", "resultCategory": "Weapon",
                  "requiredCraftingSkill": 1, "requiredWorld": "Aeldran", "requiredTaperType": null,
                  "baseWorkmanshipMin": 1, "baseWorkmanshipMax": 2, "isDiscoverable": false,
                  "ingredients": [{ "category": "Component", "name": "Wood", "baseQuantity": 1 }] },
                { "recipeId": "DUPE_001", "name": "B", "resultItemName": "B", "resultCategory": "Weapon",
                  "requiredCraftingSkill": 1, "requiredWorld": "Aeldran", "requiredTaperType": null,
                  "baseWorkmanshipMin": 1, "baseWorkmanshipMax": 2, "isDiscoverable": false,
                  "ingredients": [{ "category": "Component", "name": "Wood", "baseQuantity": 1 }] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*duplicate recipeId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_result_category_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            WriteValidBuildings(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "recipes.json"), """
            {
              "recipes": [
                { "recipeId": "X", "name": "X", "resultItemName": "X", "resultCategory": "NotACategory",
                  "requiredCraftingSkill": 1, "requiredWorld": "Aeldran", "requiredTaperType": null,
                  "baseWorkmanshipMin": 1, "baseWorkmanshipMax": 2, "isDiscoverable": false,
                  "ingredients": [{ "category": "Component", "name": "Wood", "baseQuantity": 1 }] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*invalid resultCategory*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Empty_ingredient_list_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            WriteValidBuildings(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "recipes.json"), """
            {
              "recipes": [
                { "recipeId": "X", "name": "X", "resultItemName": "X", "resultCategory": "Weapon",
                  "requiredCraftingSkill": 1, "requiredWorld": "Aeldran", "requiredTaperType": null,
                  "baseWorkmanshipMin": 1, "baseWorkmanshipMax": 2, "isDiscoverable": false,
                  "ingredients": [] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*no ingredients*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Workmanship_min_greater_than_max_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            WriteValidBuildings(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "recipes.json"), """
            {
              "recipes": [
                { "recipeId": "X", "name": "X", "resultItemName": "X", "resultCategory": "Weapon",
                  "requiredCraftingSkill": 1, "requiredWorld": "Aeldran", "requiredTaperType": null,
                  "baseWorkmanshipMin": 7, "baseWorkmanshipMax": 3, "isDiscoverable": false,
                  "ingredients": [{ "category": "Component", "name": "Wood", "baseQuantity": 1 }] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*baseWorkmanshipMin must not exceed baseWorkmanshipMax*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_required_world_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteValidConsumables(dir.FullName);
            WriteValidBuildings(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "recipes.json"), """
            {
              "recipes": [
                { "recipeId": "X", "name": "X", "resultItemName": "X", "resultCategory": "Weapon",
                  "requiredCraftingSkill": 1, "requiredWorld": "Atlantis", "requiredTaperType": null,
                  "baseWorkmanshipMin": 1, "baseWorkmanshipMax": 2, "isDiscoverable": false,
                  "ingredients": [{ "category": "Component", "name": "Wood", "baseQuantity": 1 }] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*invalid requiredWorld*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    internal static void WriteValidBuildings(string dir)
    {
        // Mirror content/buildings.json's structural requirements: every
        // BuildingType enum value must be present. Easiest path: copy the
        // shipped file from the repo root. Callers that don't need buildings
        // validation coverage just need this to pass load.
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "buildings.json"), Path.Combine(dir, "buildings.json"));
    }

    internal static void WriteValidRecipes(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "recipes.json"), Path.Combine(dir, "recipes.json"));
    }

    internal static void WriteValidLootTables(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "loot-tables.json"), Path.Combine(dir, "loot-tables.json"));
    }

    internal static void WriteValidMonsters(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "monsters.json"), Path.Combine(dir, "monsters.json"));
    }

    internal static void WriteValidZones(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "zones.json"), Path.Combine(dir, "zones.json"));
    }

    internal static void WriteValidFactions(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "factions.json"), Path.Combine(dir, "factions.json"));
    }

    internal static void WriteValidQuests(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "quests.json"), Path.Combine(dir, "quests.json"));
    }

    internal static void WriteValidNpcs(string dir)
    {
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "npcs.json"), Path.Combine(dir, "npcs.json"));
    }

    /// <summary>
    /// Seed a temp content dir with all upstream-required content files so a
    /// negative test targeting a specific file can reach that file's validator.
    /// </summary>
    internal static void WriteAllPrerequisitesExcept(string dir, params string[] excluded)
    {
        var excludedSet = new HashSet<string>(excluded, StringComparer.OrdinalIgnoreCase);
        if (!excludedSet.Contains("consumables")) WriteValidConsumables(dir);
        if (!excludedSet.Contains("buildings")) WriteValidBuildings(dir);
        if (!excludedSet.Contains("recipes")) WriteValidRecipes(dir);
        if (!excludedSet.Contains("monsters")) WriteValidMonsters(dir);
        if (!excludedSet.Contains("loot-tables")) WriteValidLootTables(dir);
        if (!excludedSet.Contains("zones")) WriteValidZones(dir);
        if (!excludedSet.Contains("factions")) WriteValidFactions(dir);
        if (!excludedSet.Contains("quests")) WriteValidQuests(dir);
        if (!excludedSet.Contains("npcs")) WriteValidNpcs(dir);
    }

    // ─── Zone definitions ─────────────────────────────────────────────────

    /// <summary>
    /// Happy path: the real content/zones.json loads, covers every zone that
    /// used to live in the hardcoded SeedAeldranZonesAsync block + the
    /// ZoneGridLayout.KnownPositions dictionary + the BiomeService.GetBiome
    /// switch, and round-trips representative entries exactly.
    /// </summary>
    [Fact]
    public void Real_zones_file_reproduces_legacy_seed_definitions()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        provider.AllZones().Should().HaveCount(9);

        var starting = provider.AllZones().Single(z => z.IsStartingZone);
        starting.Name.Should().Be("Starting Road");
        starting.Layout.X.Should().Be(20);
        starting.Layout.Y.Should().Be(10);

        // Biome mapping preserves every entry in the former BiomeService switch.
        provider.GetBiomeForZone("Caervorn Highlands").Should().Be("mountain");
        provider.GetBiomeForZone("The Thornwood").Should().Be("forest");
        provider.GetBiomeForZone("Portmere (Compact)").Should().Be("plains");
        provider.GetBiomeForZone("Gravenmarsh").Should().Be("swamp");
        provider.GetBiomeForZone("The Drowned Coast").Should().Be("water");
        provider.GetBiomeForZone("The Ashen Reach").Should().Be("desert");
        provider.GetBiomeForZone("Starting Road").Should().Be("plains");
        provider.GetBiomeForZone("Gravenhold").Should().Be("mountain");
        provider.GetBiomeForZone("The Maw Borderlands").Should().Be("wyrd");
        provider.GetBiomeForZone("Not A Zone").Should().BeNull();

        // Layout positions preserve every entry in the former KnownPositions map.
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 1).Should().Be((8, 3));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 2).Should().Be((13, 5));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 3).Should().Be((24, 13));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 4).Should().Be((28, 9));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 5).Should().Be((32, 15));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 6).Should().Be((34, 4));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 7).Should().Be((20, 10));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 8).Should().Be((26, 7));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 9).Should().Be((36, 18));
        provider.GetZoneLayoutPosition(WorldId.Aeldran, 999).Should().BeNull();

        // Danger ranges preserved
        provider.AllZones().Single(z => z.Name == "The Ashen Reach").DangerLevel.Should().Be(8);
        provider.AllZones().Single(z => z.Name == "Portmere (Compact)").DangerLevel.Should().Be(1);

        // Lookup by zoneId returns the same record
        var first = provider.AllZones()[0];
        provider.GetZone(first.ZoneId).Should().BeSameAs(first);
        provider.GetZone("no-such-zone").Should().BeNull();
        provider.GetZone("").Should().BeNull();
    }

    [Fact]
    public void Missing_zones_file_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "zones");
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<FileNotFoundException>()
               .Which.FileName.Should().EndWith("zones.json");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_zone_id_throws_on_load()
    {
        var dir = MakeContentDirWith("""
        {
          "zones": [
            { "zoneId": "dup", "world": "Aeldran", "zoneNumber": 1, "name": "A",
              "description": "", "asciiSymbol": ".", "biome": "plains",
              "dangerLevel": 1, "layout": { "x": 1, "y": 1 } },
            { "zoneId": "dup", "world": "Aeldran", "zoneNumber": 2, "name": "B",
              "description": "", "asciiSymbol": ".", "biome": "plains",
              "dangerLevel": 1, "layout": { "x": 2, "y": 2 } }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*duplicate zoneId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_biome_throws_on_load()
    {
        var dir = MakeContentDirWith("""
        {
          "zones": [
            { "zoneId": "z1", "world": "Aeldran", "zoneNumber": 1, "name": "A",
              "description": "", "asciiSymbol": ".", "biome": "lava",
              "dangerLevel": 1, "layout": { "x": 1, "y": 1 } }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*invalid biome*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_danger_range_throws_on_load()
    {
        var dir = MakeContentDirWith("""
        {
          "zones": [
            { "zoneId": "z1", "world": "Aeldran", "zoneNumber": 1, "name": "A",
              "description": "", "asciiSymbol": ".", "biome": "plains",
              "dangerLevel": 99, "layout": { "x": 1, "y": 1 } }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*dangerLevel*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_world_enum_throws_on_load()
    {
        var dir = MakeContentDirWith("""
        {
          "zones": [
            { "zoneId": "z1", "world": "Atlantis", "zoneNumber": 1, "name": "A",
              "description": "", "asciiSymbol": ".", "biome": "plains",
              "dangerLevel": 1, "layout": { "x": 1, "y": 1 } }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*invalid world*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_zoneId_returns_null()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        provider.GetZone("nope").Should().BeNull();
    }

    [Fact]
    public void Unknown_monster_spawn_throws_when_monsters_file_present()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "zones");
            // Real monsters.json does NOT contain a "dragon" — zones.json below
            // references "dragon" which must fail the cross-ref check.
            File.WriteAllText(Path.Combine(dir.FullName, "zones.json"), """
            {
              "zones": [
                { "zoneId": "z1", "world": "Aeldran", "zoneNumber": 1, "name": "A",
                  "description": "", "asciiSymbol": ".", "biome": "plains",
                  "dangerLevel": 1, "layout": { "x": 1, "y": 1 },
                  "monsterSpawns": [ { "monsterId": "dragon", "weight": 1 } ] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*unknown monsterId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static DirectoryInfo MakeContentDirWith(string zonesJson)
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        WriteAllPrerequisitesExcept(dir.FullName, "zones");
        File.WriteAllText(Path.Combine(dir.FullName, "zones.json"), zonesJson);
        return dir;
    }

    // ─── Factions ────────────────────────────────────────────────────────────

    [Fact]
    public void Real_factions_file_loads_with_all_enum_values_covered()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var all = provider.AllFactions();
        all.Should().HaveCount(Enum.GetValues<FactionId>().Length);

        foreach (FactionId id in Enum.GetValues<FactionId>())
        {
            provider.GetFaction(id).Should().NotBeNull($"FactionId.{id} must be authored in factions.json");
        }

        var caervorn = provider.GetFaction(FactionId.HouseCaervorn)!;
        caervorn.DisplayName.Should().Be("House Caervorn");
        caervorn.HostileTo.Should().Contain(FactionId.ThornwoodCovens);
        caervorn.Waypoint.Should().NotBeNull();
        caervorn.Waypoint!.X.Should().Be(8);
        caervorn.Waypoint.Y.Should().Be(3);
    }

    [Fact]
    public void Missing_factions_file_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "factions");
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ─── Quest definitions ────────────────────────────────────────────────

    /// <summary>
    /// Happy path: the real content/quests.json loads, covers every quest
    /// that used to live in the hardcoded LoreSeeder.BuildQuestSeedData
    /// table, and round-trips representative entries exactly.
    /// </summary>
    [Fact]
    public void Real_quests_file_reproduces_legacy_seed_definitions()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        provider.AllQuests().Should().HaveCount(13);

        var rider001 = provider.GetQuest("RIDER_001");
        rider001.Should().NotBeNull();
        rider001!.Title.Should().Be("A Delivery Gone Wrong");
        rider001.FactionId.Should().Be(FactionId.HouseCaervorn);
        rider001.RequiredTier.Should().Be(ReputationTier.Unknown);
        rider001.RequiredWorld.Should().Be(WorldId.Aeldran);
        rider001.ReputationReward.Should().Be(150);
        rider001.PossibleOutcomes.Should().ContainInOrder("reported", "concealed");
        rider001.IsWyrdQuest.Should().BeFalse();

        var ashen = provider.GetQuest("ASHEN_001");
        ashen!.IsWyrdQuest.Should().BeTrue();
        ashen.ReputationReward.Should().Be(0);

        // 8 unlocks + 8 requires in the legacy seeder
        provider.AllQuestEdges().Should().HaveCount(16);
        provider.AllQuestEdges()
            .Count(e => e.Kind == "unlocks").Should().Be(8);
        provider.AllQuestEdges()
            .Count(e => e.Kind == "requires").Should().Be(8);

        provider.AllQuestEdges()
            .Single(e => e.Kind == "unlocks" && e.FromQuestId == "RIDER_001" && e.ToQuestId == "RIDER_002a")
            .Outcome.Should().Be("reported");

        provider.GetQuest("NO_SUCH_QUEST").Should().BeNull();
        provider.GetQuest("").Should().BeNull();
    }

    [Fact]
    public void Missing_quests_file_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "quests");
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<FileNotFoundException>()
               .Which.FileName.Should().EndWith("quests.json");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_faction_enum_value_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "factions");
            // Author only one faction; the other FactionId enum values are missing.
            File.WriteAllText(Path.Combine(dir.FullName, "factions.json"), """
            {
              "factions": [
                {
                  "id": "HouseCaervorn", "displayName": "House Caervorn",
                  "description": "Highland kingdom.", "hqZoneId": "aeldran-1-caervorn-highlands",
                  "hostileTo": [], "startingReputation": 0,
                  "waypointZoneIds": ["aeldran-1-caervorn-highlands"]
                }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*FactionId.ThornwoodCovens has no entry*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_quest_id_throws_on_load()
    {
        var dir = MakeContentDirWithQuests("""
        {
          "quests": [
            { "questId": "DUPE", "title": "A", "description": "x",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false },
            { "questId": "DUPE", "title": "B", "description": "y",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false }
          ],
          "edges": []
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*duplicate questId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_faction_id_on_quest_throws_on_load()
    {
        var dir = MakeContentDirWithQuests("""
        {
          "quests": [
            { "questId": "Q1", "title": "T", "description": "d",
              "factionId": "NoSuchFaction", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false }
          ],
          "edges": []
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*invalid factionId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_starting_zone_on_quest_throws_on_load()
    {
        var dir = MakeContentDirWithQuests("""
        {
          "quests": [
            { "questId": "Q1", "title": "T", "description": "d",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false,
              "startingZoneId": "not-a-real-zone" }
          ],
          "edges": []
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*not a known zoneId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Dangling_edge_to_unknown_quest_throws_on_load()
    {
        var dir = MakeContentDirWithQuests("""
        {
          "quests": [
            { "questId": "Q1", "title": "T", "description": "d",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false }
          ],
          "edges": [
            { "kind": "requires", "from": "Q1", "to": "NOPE" }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*unknown 'to' questId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unlocks_edge_with_undeclared_outcome_throws_on_load()
    {
        var dir = MakeContentDirWithQuests("""
        {
          "quests": [
            { "questId": "Q1", "title": "T", "description": "d",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false },
            { "questId": "Q2", "title": "T2", "description": "d",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false }
          ],
          "edges": [
            { "kind": "unlocks", "from": "Q1", "to": "Q2", "outcome": "ghost_outcome" }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
                .WithMessage("*not in that quest's possibleOutcomes*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_hqZoneId_against_zones_file_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "factions", "zones");
            // Seed a zones.json so the cross-ref check activates. One zoneId only,
            // which none of the factions reference.
            File.WriteAllText(Path.Combine(dir.FullName, "zones.json"), """
            { "zones": [ { "zoneId": "some-other-zone", "world": "Aeldran", "zoneNumber": 1, "name": "X", "description": "", "asciiSymbol": ".", "biome": "plains", "dangerLevel": 1, "layout": { "x": 1, "y": 1 } } ] }
            """);
            // Copy real factions.json — its hqZoneIds won't match the fake zone set.
            WriteValidFactions(dir.FullName);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*hqZoneId*is not a known zoneId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Orphan_node_in_quest_throws_on_load()
    {
        var dir = MakeContentDirWithQuests("""
        {
          "quests": [
            { "questId": "Q1", "title": "T", "description": "d",
              "factionId": "HouseCaervorn", "requiredTier": "Unknown",
              "requiredWorld": "Aeldran", "reputationReward": 0,
              "possibleOutcomes": ["completed"], "isWyrdQuest": false,
              "nodes": [
                { "nodeId": "start",   "type": "dialogue", "content": "hi" },
                { "nodeId": "orphan",  "type": "dialogue", "content": "lost" }
              ],
              "internalEdges": [
                { "from": "start", "to": "start" }
              ] }
          ],
          "edges": []
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>().WithMessage("*orphan/unreachable node*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_hostileTo_ref_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "factions");
            var allIds = Enum.GetNames<FactionId>();
            var entries = string.Join(",\n", allIds.Select((id, i) =>
            {
                var hostile = i == 0 ? "\"NotAFaction\"" : "";
                return $$"""
                { "id": "{{id}}", "displayName": "{{id}}", "description": "x",
                  "hqZoneId": "aeldran-1-caervorn-highlands", "hostileTo": [{{hostile}}],
                  "startingReputation": 0, "waypointZoneIds": ["aeldran-1-caervorn-highlands"] }
                """;
            }));
            File.WriteAllText(Path.Combine(dir.FullName, "factions.json"),
                $$"""{ "factions": [ {{entries}} ] }""");
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*hostileTo entry 'NotAFaction'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Self_hostile_entry_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "factions");
            var allIds = Enum.GetNames<FactionId>();
            var entries = string.Join(",\n", allIds.Select((id, i) =>
            {
                var hostile = i == 0 ? $"\"{id}\"" : "";
                return $$"""
                { "id": "{{id}}", "displayName": "{{id}}", "description": "x",
                  "hqZoneId": "aeldran-1-caervorn-highlands", "hostileTo": [{{hostile}}],
                  "startingReputation": 0, "waypointZoneIds": ["aeldran-1-caervorn-highlands"] }
                """;
            }));
            File.WriteAllText(Path.Combine(dir.FullName, "factions.json"),
                $$"""{ "factions": [ {{entries}} ] }""");
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*cannot be hostile to itself*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Empty_waypointZoneIds_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "factions");
            var allIds = Enum.GetNames<FactionId>();
            var entries = string.Join(",\n", allIds.Select(id => $$"""
                { "id": "{{id}}", "displayName": "{{id}}", "description": "x",
                  "hqZoneId": "aeldran-1-caervorn-highlands", "hostileTo": [],
                  "startingReputation": 0, "waypointZoneIds": [] }
                """));
            File.WriteAllText(Path.Combine(dir.FullName, "factions.json"),
                $$"""{ "factions": [ {{entries}} ] }""");
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*at least one waypointZoneId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static DirectoryInfo MakeContentDirWithQuests(string questsJson)
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        WriteAllPrerequisitesExcept(dir.FullName);
        File.WriteAllText(Path.Combine(dir.FullName, "quests.json"), questsJson);
        return dir;
    }

    /// <summary>
    /// Tightening assertion: every quest's factionId must resolve against the
    /// authoritative factions.json (not just the FactionId enum). This guards
    /// against a quest referencing an enum value that has been retired from
    /// content/factions.json.
    /// </summary>
    [Fact]
    public void Every_quest_factionId_is_present_in_factions_json()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        var authoredFactions = provider.AllFactions().Select(f => f.Id).ToHashSet();
        foreach (var quest in provider.AllQuests())
        {
            authoredFactions.Should().Contain(quest.FactionId,
                $"quest '{quest.QuestId}' references FactionId.{quest.FactionId} which must be authored in factions.json");
        }
    }

    // ─── World Events ─────────────────────────────────────────────────────

    /// <summary>
    /// Tightening assertion: every world-event's referenced npcId must resolve
    /// against the authoritative npcs.json. Guards against events drifting out
    /// of sync with the NPC catalog after rename/retire.
    /// </summary>
    [Fact]
    public void Every_world_event_npcId_is_present_in_npcs_json()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        var authoredNpcIds = provider.AllNpcs().Select(n => n.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var evt in provider.AllEvents())
        {
            foreach (var effect in evt.Effects.Concat(evt.OnExpire))
            {
                if (!string.IsNullOrWhiteSpace(effect.NpcId))
                {
                    authoredNpcIds.Should().Contain(effect.NpcId,
                        $"event '{evt.Id}' effect type '{effect.Type}' references npcId '{effect.NpcId}' which must be authored in npcs.json");
                }
            }
        }
    }

    /// <summary>
    /// Happy path: the shipped world-events.json loads, 12 events are seeded
    /// from world-events.md, and lookup by id works.
    /// </summary>
    [Fact]
    public void Real_world_events_file_loads_all_authored_events()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var events = provider.AllEvents();
        events.Should().HaveCount(12);
        events.Select(e => e.Id).Should().OnlyHaveUniqueItems();

        var redMarket = provider.GetEvent("red-market");
        redMarket.Should().NotBeNull();
        redMarket!.Family.Should().Be("economic");
        redMarket.Trigger.Kind.Should().Be("complex");
        redMarket.Duration.Days.Should().Be(6);
        redMarket.Effects.Should().NotBeEmpty();

        provider.GetEvent("ashen-silence-breaks")!.OneTime.Should().BeTrue();
        provider.GetEvent("ashen-silence-breaks")!.Duration.Permanent.Should().BeTrue();

        provider.GetEvent("no-such-event").Should().BeNull();
        provider.GetEvent("").Should().BeNull();
    }

    [Fact]
    public void Unknown_zone_ref_in_event_effect_throws_on_load()
    {
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "bad-zone-evt",
              "displayName": "Bad",
              "family": "economic",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "unconditional" },
              "duration": { "days": 1 },
              "effects": [ { "type": "zoneAmbient", "zoneId": "no-such-zone", "text": "x" } ]
            }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*unknown zoneId 'no-such-zone'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_faction_tag_in_event_throws_on_load()
    {
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "bad-faction-evt",
              "displayName": "Bad",
              "family": "political",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "unconditional" },
              "duration": { "days": 1 },
              "effects": [ { "type": "setFlag", "flag": "x" } ],
              "factionTags": ["NotAFaction"]
            }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*factionTag 'NotAFaction'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_npc_ref_in_event_throws_on_load()
    {
        // After the NPC catalog merged into content/npcs.json, world-events
        // validation now cross-checks npcId against the authored catalog so
        // typos and rename-drift fail loudly.
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "permissive-npc",
              "displayName": "x",
              "family": "political",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "unconditional" },
              "duration": { "days": 1 },
              "effects": [ { "type": "npcDialogueLine", "npcId": "not-in-any-registry", "text": "x" } ]
            }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*unknown npcId 'not-in-any-registry'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Unknown_quest_ref_in_event_trigger_throws_on_load()
    {
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "bad-quest-evt",
              "displayName": "Bad",
              "family": "political",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "questCompleted", "questId": "NO_SUCH_QUEST" },
              "duration": { "days": 1 },
              "effects": [ { "type": "setFlag", "flag": "x" } ]
            }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*NO_SUCH_QUEST*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_event_id_throws_on_load()
    {
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "dupe",
              "displayName": "A",
              "family": "economic",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "unconditional" },
              "duration": { "days": 1 },
              "effects": [ { "type": "setFlag", "flag": "x" } ]
            },
            {
              "id": "dupe",
              "displayName": "B",
              "family": "economic",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "unconditional" },
              "duration": { "days": 1 },
              "effects": [ { "type": "setFlag", "flag": "x" } ]
            }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate event id 'dupe'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Missing_onExpire_for_transient_event_emits_warning()
    {
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "missing-cleanup",
              "displayName": "Missing Cleanup",
              "family": "economic",
              "priority": 1,
              "description": "transient price shift with no restore",
              "trigger": { "kind": "unconditional" },
              "duration": { "days": 3 },
              "effects": [ { "type": "shopPriceShift", "zoneId": "aeldran-7-starting-road", "item": "grain", "multiplier": 1.5 } ]
            }
          ]
        }
        """);
        try
        {
            var logger = new CapturingLogger<ContentProvider>();
            var provider = new ContentProvider(dir.FullName, logger);
            provider.GetEvent("missing-cleanup").Should().NotBeNull();
            logger.Warnings.Should().Contain(m => m.Contains("missing-cleanup") && m.Contains("onExpire"));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_trigger_shape_throws_on_load()
    {
        // repThreshold requires factionId + (minTier OR min); here we provide
        // a bogus factionId.
        var dir = MakeContentDirWithEvents("""
        {
          "events": [
            {
              "id": "bad-trigger",
              "displayName": "Bad Trigger",
              "family": "political",
              "priority": 1,
              "description": "x",
              "trigger": { "kind": "repThreshold", "factionId": "NotAFaction", "min": 0 },
              "duration": { "days": 1 },
              "effects": [ { "type": "setFlag", "flag": "x" } ]
            }
          ]
        }
        """);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*repThreshold*NotAFaction*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static DirectoryInfo MakeContentDirWithEvents(string eventsJson)
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-evt-test-");
        WriteAllPrerequisitesExcept(dir.FullName);
        File.WriteAllText(Path.Combine(dir.FullName, "world-events.json"), eventsJson);
        return dir;
    }

    /// <summary>Records LogWarning calls in memory for assertion.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = new();
        IDisposable ILogger.BeginScope<TState>(TState state) => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // ─── Combat Curves ────────────────────────────────────────────────────

    [Fact]
    public void Real_combat_curves_file_loads_with_expected_coefficients()
    {
        // content/combat-curves.json pins the current scaling values.
        // Live game's MonsterFactory + encounter-sim must agree on these.
        // Pass #7 (2026-04-13, creative pass playbook sweep) re-tuned from
        // the initial TPK-fix (hp 0.2 / power 0.15 / party 0.12) which
        // over-corrected once gear/imbue proxies were stacked in playbook
        // cells. hp/power restored partway, party-scaling cut hard. See
        // docs/design/sim-reports/playbook-pass-1.md.
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var ms = provider.CombatCurves.MonsterScaling;
        ms.HpPerDanger.Should().Be(0.40);
        ms.PowerPerDanger.Should().Be(0.28);
        ms.SpeedPerDanger.Should().Be(0.7);
        ms.BossHpMultiplier.Should().Be(1.6);
        ms.BossSpeedBonus.Should().Be(4);

        var ps = provider.CombatCurves.PartyScaling;
        ps.ScalingPerTier.Should().Be(0.03);
    }

    [Fact]
    public void Out_of_range_combat_curve_coefficient_is_rejected()
    {
        // Author error guard: hpPerDanger of 99 would 100x monster HP at
        // danger 1 — that's almost certainly a typo, not intent.
        var realRoot = ContentRootResolver.Resolve();
        var dir = Directory.CreateTempSubdirectory("fm-content-curves-test-");
        try
        {
            // Mirror every required content file from the real root, then
            // overwrite combat-curves.json with the bogus payload.
            foreach (var file in Directory.EnumerateFiles(realRoot, "*.json"))
                File.Copy(file, Path.Combine(dir.FullName, Path.GetFileName(file)));

            File.WriteAllText(Path.Combine(dir.FullName, "combat-curves.json"), """
            {
              "monsterScaling": {
                "hpPerDanger": 99,
                "powerPerDanger": 0.3,
                "speedPerDanger": 1.0,
                "bossHpMultiplier": 2.0,
                "bossSpeedBonus": 5
              }
            }
            """);

            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*hpPerDanger*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ─── NPCs ────────────────────────────────────────────────────────────────

    [Fact]
    public void Real_npcs_file_loads_and_round_trips_canonical_entries()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var all = provider.AllNpcs();
        all.Should().NotBeEmpty();
        all.Select(n => n.Id).Should().OnlyHaveUniqueItems();

        var maerwyn = provider.GetNpc("auld-maerwyn");
        maerwyn.Should().NotBeNull();
        maerwyn!.DisplayName.Should().Be("Auld Maerwyn");
        maerwyn.FactionId.Should().Be(FirstMud.Domain.Enums.FactionId.ThornwoodCovens);
        maerwyn.HomeZoneId.Should().Be("aeldran-2-thornwood");
        maerwyn.Role.Should().Be(NpcRole.Questgiver);
        maerwyn.VoiceTells.Should().HaveCountGreaterOrEqualTo(3);
        maerwyn.VoiceTells.Should().HaveCountLessOrEqualTo(5);

        // Unaffiliated NPCs retain a null factionId.
        var senna = provider.GetNpc("senna-orrick");
        senna.Should().NotBeNull();
        senna!.FactionId.Should().BeNull();

        provider.GetNpc("no-such-npc").Should().BeNull();
        provider.GetNpc("").Should().BeNull();
    }

    [Fact]
    public void Npc_missing_id_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "npcs");
            File.WriteAllText(Path.Combine(dir.FullName, "npcs.json"), """
            {
              "npcs": [
                { "id": "", "displayName": "Nameless", "homeZoneId": "aeldran-1-caervorn-highlands",
                  "role": "civilian", "shortDescription": "x",
                  "voiceTells": ["a","b","c"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*npc missing id*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Npc_unknown_factionId_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "npcs");
            File.WriteAllText(Path.Combine(dir.FullName, "npcs.json"), """
            {
              "npcs": [
                { "id": "ghost", "displayName": "Ghost", "factionId": "NotAFaction",
                  "homeZoneId": "aeldran-1-caervorn-highlands",
                  "role": "civilian", "shortDescription": "x",
                  "voiceTells": ["a","b","c"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid factionId 'NotAFaction'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Npc_unknown_homeZoneId_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "npcs");
            File.WriteAllText(Path.Combine(dir.FullName, "npcs.json"), """
            {
              "npcs": [
                { "id": "ghost", "displayName": "Ghost",
                  "homeZoneId": "no-such-zone",
                  "role": "civilian", "shortDescription": "x",
                  "voiceTells": ["a","b","c"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*homeZoneId 'no-such-zone' is not a known zoneId*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Npc_unknown_role_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "npcs");
            File.WriteAllText(Path.Combine(dir.FullName, "npcs.json"), """
            {
              "npcs": [
                { "id": "ghost", "displayName": "Ghost",
                  "homeZoneId": "aeldran-1-caervorn-highlands",
                  "role": "wizard", "shortDescription": "x",
                  "voiceTells": ["a","b","c"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid role 'wizard'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Npc_duplicate_id_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "npcs");
            File.WriteAllText(Path.Combine(dir.FullName, "npcs.json"), """
            {
              "npcs": [
                { "id": "twin", "displayName": "Twin A",
                  "homeZoneId": "aeldran-1-caervorn-highlands",
                  "role": "civilian", "shortDescription": "x",
                  "voiceTells": ["a","b","c"] },
                { "id": "twin", "displayName": "Twin B",
                  "homeZoneId": "aeldran-1-caervorn-highlands",
                  "role": "civilian", "shortDescription": "y",
                  "voiceTells": ["d","e","f"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate npc id 'twin'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Npc_too_few_voiceTells_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteAllPrerequisitesExcept(dir.FullName, "npcs");
            File.WriteAllText(Path.Combine(dir.FullName, "npcs.json"), """
            {
              "npcs": [
                { "id": "thin", "displayName": "Thin",
                  "homeZoneId": "aeldran-1-caervorn-highlands",
                  "role": "civilian", "shortDescription": "x",
                  "voiceTells": ["only-one"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*between 3 and 5 voiceTells*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ─── requires block validation ────────────────────────────────────────

    [Fact]
    public void Quest_requires_priorQuests_unknown_id_throws_on_load()
    {
        // A quest whose requires.priorQuests references a non-existent questId
        // must be rejected at load-time. This guards against typos silently
        // making a quest permanently un-acceptable.
        var questsJson =
            """
            {
              "quests": [
                { "questId": "Q_TEST", "title": "T", "description": "D",
                  "factionId": "HouseCaervorn", "requiredTier": "Unknown",
                  "requiredWorld": "Aeldran", "reputationReward": 10,
                  "possibleOutcomes": ["completed"], "isWyrdQuest": false,
                  "startingZoneId": "aeldran-1-caervorn-highlands",
                  "requires": { "priorQuests": ["DOES_NOT_EXIST"] } }
              ],
              "edges": []
            }
            """;
        var dir = MakeContentDirWithQuests(questsJson);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*requires.priorQuests references unknown questId 'DOES_NOT_EXIST'*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Quest_requires_items_empty_name_throws_on_load()
    {
        var questsJson =
            """
            {
              "quests": [
                { "questId": "Q_TEST", "title": "T", "description": "D",
                  "factionId": "HouseCaervorn", "requiredTier": "Unknown",
                  "requiredWorld": "Aeldran", "reputationReward": 10,
                  "possibleOutcomes": ["completed"], "isWyrdQuest": false,
                  "startingZoneId": "aeldran-1-caervorn-highlands",
                  "requires": { "items": [{ "name": "", "quantity": 1 }] } }
              ],
              "edges": []
            }
            """;
        var dir = MakeContentDirWithQuests(questsJson);
        try
        {
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*requires.items has an entry with empty name*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }
}
