using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace FirstMud.Tests.Application;

/// <summary>
/// Pure-function tests for <see cref="AutoImbuePlanner"/>. No DI, no repos.
/// Mirrors AutoCraftPlannerTests' style — hand-built snapshots fed in, plan
/// asserted on output.
/// </summary>
public class AutoImbuePlannerTests
{
    private static EquippedSlotSnapshot Snap(EquipmentSlot slot, int max, int filled, string? name = null)
        => new(slot, Guid.NewGuid(), name ?? slot.ToString(), max, filled);

    private static ImbueRecipeDefinition GemRecipe(
        string id, string reagent, int skill, ImbueType type, float power)
        => new(
            Id: id, GemId: reagent.ToLowerInvariant(),
            RequiredReagent: reagent,
            RequiredCraftingSkill: skill, ImbueType: type,
            Effect: "imbue", Power: power, Variance: 0f, SuccessBonus: 0,
            UpgradesExisting: false, Description: "");

    [Fact]
    public void PartyCoverage_is_unweighted_mean_of_per_slot_ratios()
    {
        var snaps = new[]
        {
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.MeleeWeapon, Filled: 0, Max: 2),  // 0.00
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.Chest,        Filled: 1, Max: 2), // 0.50
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.Head,         Filled: 2, Max: 2), // 1.00
        };
        AutoImbuePlanner.PartyCoverage(snaps).Should().BeApproximately(0.5, 1e-6);
    }

    [Fact]
    public void PickWorstSlot_returns_lowest_ratio_with_open_slot()
    {
        var snaps = new[]
        {
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.MeleeWeapon, 0, 2), // 0.0
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.Chest,        1, 2), // 0.5
        };
        AutoImbuePlanner.PickWorstSlot(snaps)!.Value.Slot.Should().Be(EquipmentSlot.MeleeWeapon);
    }

    [Fact]
    public void PickWorstSlot_breaks_ties_by_larger_capacity_first()
    {
        // Both at ratio 0.0 but chest has capacity 4 vs head 2 → chest wins (more to gain).
        var snaps = new[]
        {
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.Head,  0, 2),
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.Chest, 0, 4),
        };
        AutoImbuePlanner.PickWorstSlot(snaps)!.Value.Slot.Should().Be(EquipmentSlot.Chest);
    }

    [Fact]
    public void PickWorstSlot_returns_null_when_all_slots_saturated()
    {
        var snaps = new[]
        {
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.MeleeWeapon, 2, 2),
            new AutoImbuePlanner.SlotCoverage(EquipmentSlot.Chest,        2, 2),
        };
        AutoImbuePlanner.PickWorstSlot(snaps).Should().BeNull();
    }

    [Fact]
    public void SelectImbueType_prefers_weapon_element_over_player_element()
    {
        var type = AutoImbuePlanner.SelectImbueType(
            weaponElement: MagicElement.Fire,
            playerPrimaryElement: MagicElement.Water,
            stash: new Dictionary<string, int>());
        type.Should().Be(ImbueType.Fire);
    }

    [Fact]
    public void SelectImbueType_falls_back_to_player_element_when_weapon_has_none()
    {
        var type = AutoImbuePlanner.SelectImbueType(
            weaponElement: null,
            playerPrimaryElement: MagicElement.Earth,
            stash: new Dictionary<string, int>());
        type.Should().Be(ImbueType.Earth);
    }

    [Fact]
    public void SelectImbueType_falls_back_to_reagent_histogram_when_no_element()
    {
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Aquamarine"] = 10, // Water preferred gem
            ["Topaz"] = 2,
        };
        var type = AutoImbuePlanner.SelectImbueType(
            weaponElement: null,
            playerPrimaryElement: default,
            stash: stash);
        type.Should().Be(ImbueType.Water);
    }

    [Fact]
    public void DiffReagent_reports_missing_quantity_and_farming_source()
    {
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var diff = AutoImbuePlanner.DiffReagent("Topaz", needed: 1, stash);
        diff.Should().HaveCount(1);
        diff[0].ItemName.Should().Be("Topaz");
        diff[0].Quantity.Should().Be(1);
        diff[0].Source.ZoneHint.Should().Be("Starting Road");
    }

    [Fact]
    public void PickBestGemRecipe_prefers_higher_power_in_stash()
    {
        var recipes = new[]
        {
            GemRecipe("obsidian-fire-budget", "obsidian", skill: 1, ImbueType.Fire, 0.15f),
            GemRecipe("topaz-fire",           "topaz",    skill: 4, ImbueType.Fire, 0.35f),
        };
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Topaz"] = 1,
            ["Obsidian"] = 3,
        };
        var pick = AutoImbuePlanner.PickBestGemRecipe(ImbueType.Fire, 5, recipes, stash);
        pick!.Id.Should().Be("topaz-fire");
    }

    [Fact]
    public void PickBestGemRecipe_respects_skill_gate()
    {
        var recipes = new[]
        {
            GemRecipe("topaz-fire",      "topaz", skill: 4, ImbueType.Fire, 0.35f),
            GemRecipe("diamond-high-fire","diamond",skill:8, ImbueType.Fire, 0.50f),
        };
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Topaz"] = 1,
            ["Diamond"] = 1,
        };
        // Skill=5: diamond gated out.
        var pick = AutoImbuePlanner.PickBestGemRecipe(ImbueType.Fire, 5, recipes, stash);
        pick!.Id.Should().Be("topaz-fire");
    }

    [Fact]
    public void Plan_prefers_gem_recipe_when_available()
    {
        var snaps = new[] { Snap(EquipmentSlot.MeleeWeapon, max: 2, filled: 0, "Iron Sword") };
        var recipes = new[]
        {
            GemRecipe("topaz-fire", "topaz", skill: 1, ImbueType.Fire, 0.35f),
        };
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Topaz"] = 1,
        };
        var plan = AutoImbuePlanner.Plan(
            snaps,
            equippedWeaponElement: MagicElement.Fire,
            playerPrimaryElement: MagicElement.Fire,
            playerCraftingSkill: 5,
            stash: stash,
            allImbueRecipes: recipes);

        plan.TargetItemId.Should().NotBeNull();
        plan.TargetSlot.Should().Be(EquipmentSlot.MeleeWeapon);
        plan.UsesGem.Should().BeTrue();
        plan.SelectedRecipeId.Should().Be("topaz-fire");
        plan.ImbuableNow.Should().BeTrue();
        plan.MissingReagents.Should().BeEmpty();
    }

    [Fact]
    public void Plan_returns_missing_reagent_when_stash_empty()
    {
        var snaps = new[] { Snap(EquipmentSlot.Chest, max: 2, filled: 0, "Leather Vest") };
        var recipes = new[] { GemRecipe("topaz-fire", "topaz", 1, ImbueType.Fire, 0.35f) };
        var plan = AutoImbuePlanner.Plan(
            snaps,
            equippedWeaponElement: MagicElement.Fire,
            playerPrimaryElement: MagicElement.Fire,
            playerCraftingSkill: 5,
            stash: new Dictionary<string, int>(),
            allImbueRecipes: recipes);

        plan.ImbuableNow.Should().BeFalse();
        plan.MissingReagents.Should().NotBeEmpty();
        plan.MissingReagents[0].ItemName.Should().ContainAny("topaz", "Topaz");
    }

    [Fact]
    public void Plan_returns_null_target_when_all_slots_saturated()
    {
        var snaps = new[]
        {
            Snap(EquipmentSlot.MeleeWeapon, max: 2, filled: 2, "Iron Sword"),
            Snap(EquipmentSlot.Chest,        max: 1, filled: 1, "Leather Vest"),
        };
        var plan = AutoImbuePlanner.Plan(
            snaps,
            equippedWeaponElement: MagicElement.Fire,
            playerPrimaryElement: MagicElement.Fire,
            playerCraftingSkill: 5,
            stash: new Dictionary<string, int> { ["Topaz"] = 10 },
            allImbueRecipes: Array.Empty<ImbueRecipeDefinition>());

        plan.TargetItemId.Should().BeNull();
        plan.Reason.Should().Contain("fully imbued");
    }

    [Fact]
    public void Plan_falls_back_to_taper_when_no_gem_recipe_available()
    {
        var snaps = new[] { Snap(EquipmentSlot.MeleeWeapon, max: 2, filled: 0, "Iron Sword") };
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fire Shaping Taper"] = 1,
        };
        var plan = AutoImbuePlanner.Plan(
            snaps,
            equippedWeaponElement: MagicElement.Fire,
            playerPrimaryElement: MagicElement.Fire,
            playerCraftingSkill: 1,
            stash: stash,
            allImbueRecipes: Array.Empty<ImbueRecipeDefinition>());

        plan.UsesGem.Should().BeFalse();
        plan.SelectedReagentName.Should().Contain("Fire");
        plan.ImbuableNow.Should().BeTrue();
    }

    [Fact]
    public void Plan_respects_skill_gate_when_only_gated_gem_available()
    {
        var snaps = new[] { Snap(EquipmentSlot.MeleeWeapon, max: 2, filled: 0, "Iron Sword") };
        var recipes = new[]
        {
            GemRecipe("diamond-high-fire", "diamond", skill: 8, ImbueType.Fire, 0.50f),
        };
        // Skill 3 < 8 → gem recipe filtered out, planner falls back to taper.
        var stash = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fire Shaping Taper"] = 1,
        };
        var plan = AutoImbuePlanner.Plan(
            snaps,
            equippedWeaponElement: MagicElement.Fire,
            playerPrimaryElement: MagicElement.Fire,
            playerCraftingSkill: 3,
            stash: stash,
            allImbueRecipes: recipes);

        plan.UsesGem.Should().BeFalse();
        plan.ImbuableNow.Should().BeTrue();
    }
}
