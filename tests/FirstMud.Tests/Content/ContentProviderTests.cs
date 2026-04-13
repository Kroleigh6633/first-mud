using System.IO;
using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FluentAssertions;

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

    private static void WriteValidBuildings(string dir)
    {
        // Mirror content/buildings.json's structural requirements: every
        // BuildingType enum value must be present. Easiest path: copy the
        // shipped file from the repo root. Callers that don't need buildings
        // validation coverage just need this to pass load.
        var realRoot = ContentRootResolver.Resolve();
        File.Copy(Path.Combine(realRoot, "buildings.json"), Path.Combine(dir, "buildings.json"));
    }
}
