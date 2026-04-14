using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FirstMud.Tests.Application;

/// <summary>
/// Pure-function tests for <see cref="AutoCraftPlanner"/>. No DI, no repos.
/// Drives the recipe-selection + ingredient-diff logic against hand-built
/// <see cref="RecipeDefinition"/> fixtures.
/// </summary>
public class AutoCraftPlannerTests
{
    private static RecipeDefinition Recipe(
        string id, string result, ItemCategory cat, int skill, int wmMin, int wmMax,
        params (string name, int qty)[] ingredients)
        => new(
            RecipeId: id,
            Name: result,
            ResultItemName: result,
            ResultCategory: cat,
            RequiredCraftingSkill: skill,
            RequiredWorld: WorldId.Aeldran,
            RequiredTaperType: null,
            BaseWorkmanshipMin: wmMin,
            BaseWorkmanshipMax: wmMax,
            IsDiscoverable: false,
            Ingredients: ingredients
                .Select(i => new RecipeIngredientDefinition(ItemCategory.Component, i.name, i.qty))
                .ToList());

    [Fact]
    public void ScoreRecipe_prefers_larger_workmanship_gain()
    {
        var cheap = Recipe("A", "Iron Sword",  ItemCategory.Weapon, 1, 2, 4, ("Iron Ore", 3), ("Wood", 1));
        var hefty = Recipe("B", "Mithril Blade", ItemCategory.Weapon, 1, 6, 9, ("Iron Ore", 3), ("Wood", 1));

        var sCheap = AutoCraftPlanner.ScoreRecipe(cheap, currentSlotWorkmanship: 0, playerCraftingSkill: 1);
        var sHefty = AutoCraftPlanner.ScoreRecipe(hefty, currentSlotWorkmanship: 0, playerCraftingSkill: 1);

        sHefty.Should().BeGreaterThan(sCheap);
    }

    [Fact]
    public void ScoreRecipe_penalises_ingredient_cost()
    {
        // Same expected Wm, but one uses more units — cheaper should score higher.
        var cheap = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 3, 5, ("Iron Ore", 2));
        var bulky = Recipe("B", "Iron Dagger", ItemCategory.Weapon, 1, 3, 5, ("Iron Ore", 8));

        var sCheap = AutoCraftPlanner.ScoreRecipe(cheap, 0, 1);
        var sBulky = AutoCraftPlanner.ScoreRecipe(bulky, 0, 1);

        sCheap.Should().BeGreaterThan(sBulky);
    }

    [Fact]
    public void ScoreRecipe_penalises_skill_gap()
    {
        var easy = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 3, 5, ("Iron Ore", 2));
        var hard = Recipe("B", "Iron Dagger", ItemCategory.Weapon, 5, 3, 5, ("Iron Ore", 2));

        var sEasy = AutoCraftPlanner.ScoreRecipe(easy, 0, playerCraftingSkill: 1);
        var sHard = AutoCraftPlanner.ScoreRecipe(hard, 0, playerCraftingSkill: 1);

        sEasy.Should().BeGreaterThan(sHard);
    }

    [Fact]
    public void DiffIngredients_flags_missing_quantities_and_sources_them()
    {
        var recipe = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 2, 5,
            ("Iron Ore", 3), ("Wood", 1));

        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Iron Ore"] = 1,
            // Wood missing entirely
        };

        var diff = AutoCraftPlanner.DiffIngredients(recipe, stash);

        diff.Should().HaveCount(2);
        diff.Should().ContainSingle(e => e.ItemName == "Iron Ore" && e.Quantity == 2);
        diff.Should().ContainSingle(e => e.ItemName == "Wood" && e.Quantity == 1);
        // Iron Ore + Wood both live in the common-materials pool → loot kind.
        diff.All(e => e.Source.Kind == "loot").Should().BeTrue();
    }

    [Fact]
    public void DiffIngredients_returns_empty_when_all_present()
    {
        var recipe = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 2, 5, ("Iron Ore", 3));
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Iron Ore"] = 10,
        };
        var diff = AutoCraftPlanner.DiffIngredients(recipe, stash);
        diff.Should().BeEmpty();
    }

    [Fact]
    public void ResolveSource_routes_biome_materials_to_harvest()
    {
        var beastHide = AutoCraftPlanner.ResolveSource("Beast Hide");
        beastHide.Kind.Should().Be("harvest");
        beastHide.Biome.Should().Be("biome-forest");
        beastHide.ZoneHint.Should().Be("The Thornwood");

        var mithril = AutoCraftPlanner.ResolveSource("Mithril Ore");
        mithril.Kind.Should().Be("harvest");
        mithril.Biome.Should().Be("biome-mountain");
    }

    [Fact]
    public void ResolveSlotForItem_maps_known_names()
    {
        AutoCraftPlanner.ResolveSlotForItem("Iron Sword", ItemCategory.Weapon)
            .Should().Be(EquipmentSlot.MeleeWeapon);
        AutoCraftPlanner.ResolveSlotForItem("Leather Cap", ItemCategory.Armor)
            .Should().Be(EquipmentSlot.Head);
        AutoCraftPlanner.ResolveSlotForItem("Thornwood Bow", ItemCategory.Weapon)
            .Should().Be(EquipmentSlot.RangedWeapon);
    }

    [Fact]
    public void Plan_picks_craftable_now_over_higher_scored_but_missing()
    {
        var craftable = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 2, 4, ("Iron Ore", 2));
        var unreachable = Recipe("B", "Mithril Blade", ItemCategory.Weapon, 1, 7, 9, ("Mithril Ore", 4));

        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Iron Ore"] = 5,
        };
        var equipped = new Dictionary<EquipmentSlot, int>();

        var plan = AutoCraftPlanner.Plan(
            new[] { craftable, unreachable },
            WorldId.Aeldran,
            playerCraftingSkill: 1,
            currentEquippedWorkmanship: equipped,
            stash: stash);

        plan.TargetRecipe.Should().NotBeNull();
        plan.TargetRecipe!.RecipeId.Should().Be("A");
        plan.CraftableNow.Should().BeTrue();
        plan.TargetSlot.Should().Be(EquipmentSlot.MeleeWeapon);
    }

    [Fact]
    public void Plan_falls_back_to_highest_scored_farmable_when_nothing_craftable_now()
    {
        var cheapFarmable = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 2, 4, ("Iron Ore", 2));
        var biggerFarmable = Recipe("B", "Mithril Blade", ItemCategory.Weapon, 1, 7, 9, ("Mithril Ore", 2));

        var plan = AutoCraftPlanner.Plan(
            new[] { cheapFarmable, biggerFarmable },
            WorldId.Aeldran,
            playerCraftingSkill: 1,
            currentEquippedWorkmanship: new Dictionary<EquipmentSlot, int>(),
            stash: new Dictionary<string, int>());

        plan.TargetRecipe.Should().NotBeNull();
        plan.CraftableNow.Should().BeFalse();
        plan.MissingIngredients.Should().NotBeEmpty();
        // Mithril Blade has greater Wm gain → wins the score tiebreak.
        plan.TargetRecipe!.RecipeId.Should().Be("B");
        plan.MissingIngredients[0].Source.Biome.Should().Be("biome-mountain");
    }

    [Fact]
    public void Plan_filters_out_downgrades()
    {
        var downgrade = Recipe("A", "Iron Sword", ItemCategory.Weapon, 1, 2, 3, ("Iron Ore", 2));
        var equipped = new Dictionary<EquipmentSlot, int>
        {
            [EquipmentSlot.MeleeWeapon] = 8, // already better than midpoint 2.5
        };
        var plan = AutoCraftPlanner.Plan(
            new[] { downgrade },
            WorldId.Aeldran,
            playerCraftingSkill: 1,
            currentEquippedWorkmanship: equipped,
            stash: new Dictionary<string, int> { ["Iron Ore"] = 10 });

        plan.TargetRecipe.Should().BeNull();
        plan.Reason.Should().Contain("No upgrade-worthy");
    }

    [Fact]
    public void Plan_filters_non_equipment_recipes()
    {
        var consumable = Recipe("A", "Healing Draught", ItemCategory.Consumable, 1, 3, 7, ("Herbs", 3));
        var plan = AutoCraftPlanner.Plan(
            new[] { consumable },
            WorldId.Aeldran,
            playerCraftingSkill: 1,
            currentEquippedWorkmanship: new Dictionary<EquipmentSlot, int>(),
            stash: new Dictionary<string, int> { ["Herbs"] = 10 });

        plan.TargetRecipe.Should().BeNull();
    }
}
