using System.IO;
using FirstMud.Application.Content;
using FirstMud.Domain.Enums;
using FluentAssertions;

namespace FirstMud.Tests.Content;

/// <summary>
/// Tests that exercise ContentProvider.LoadImbueRecipes and the IContentProvider
/// imbue-recipe accessors. Wiring of imbue-recipes.json into ImbueService was
/// deferred when gems shipped; this file is the regression fence now that the
/// recipes are a live content layer.
/// </summary>
public class ImbueRecipesContentTests
{
    // ─── Happy path ──────────────────────────────────────────────────────────

    /// <summary>
    /// Regression fence: the legacy taper keyword-match code path
    /// (ImbueService.ResolveImbueType) MUST remain intact. Gem-based imbuing
    /// goes through IContentProvider, but tapers continue to resolve by name.
    /// </summary>
    [Fact]
    public void Legacy_taper_keyword_resolution_is_preserved()
    {
        FirstMud.Application.Services.ImbueService.ResolveImbueType("Fire Shaping Taper")
            .Should().Be(ImbueType.Fire);
        FirstMud.Application.Services.ImbueService.ResolveImbueType("Water Shard of the Deep")
            .Should().Be(ImbueType.Water);
        FirstMud.Application.Services.ImbueService.ResolveImbueType("Wyrd Taper of the Maw")
            .Should().Be(ImbueType.Wyrd);
        FirstMud.Application.Services.ImbueService.ResolveImbueType("Dravenite Stone")
            .Should().Be(ImbueType.Restoration);
        // Gem names must NOT resolve via the legacy path — they're handled by
        // the new gem-content flow.
        FirstMud.Application.Services.ImbueService.ResolveImbueType("Raw Emerald")
            .Should().Be(ImbueType.None);
        FirstMud.Application.Services.ImbueService.ResolveImbueType("Polished Diamond")
            .Should().Be(ImbueType.None);
    }

    [Fact]
    public void Real_imbue_recipes_file_loads_every_gem_recipe()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var all = provider.AllImbueRecipes();
        all.Should().HaveCount(14, "content/imbue-recipes.json authors 14 recipes");

        // Representative spot checks per alignment
        var emerald = provider.GetImbueRecipe("emerald-earth");
        emerald.Should().NotBeNull();
        emerald!.ImbueType.Should().Be(ImbueType.Earth);
        emerald.Power.Should().BeApproximately(0.35f, 0.0001f);
        emerald.RequiredCraftingSkill.Should().Be(4);
        emerald.UpgradesExisting.Should().BeFalse();

        var diamond = provider.GetImbueRecipe("diamond-universal");
        diamond.Should().NotBeNull();
        diamond!.UpgradesExisting.Should().BeTrue();
        diamond.Effect.Should().Be("upgrade");
        diamond.RequiredCraftingSkill.Should().Be(5);

        var blackPearl = provider.GetImbueRecipe("black-pearl-master-wyrd");
        blackPearl.Should().NotBeNull();
        blackPearl!.Variance.Should().BeApproximately(0.30f, 0.0001f);
        blackPearl.ImbueType.Should().Be(ImbueType.Wyrd);
    }

    [Fact]
    public void MatchImbueRecipe_resolves_gem_name_and_respects_skill_gate()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        // At skill 1 only obsidian recipes fire for "Raw Obsidian"; obsidian-fire
        // and obsidian-earth share power so file order breaks the tie (fire first).
        var lowSkill = provider.MatchImbueRecipe("Raw Obsidian", craftingSkill: 1);
        lowSkill.Should().NotBeNull();
        lowSkill!.GemId.Should().Be("obsidian");

        // Skill 5 unlocks diamond-universal (RequiredSkill 5) but not
        // diamond-high-* (RequiredSkill 8). Diamond-universal has Power 0.0 so
        // it wins by being the only candidate at this skill.
        var midSkill = provider.MatchImbueRecipe("Flawless Diamond", craftingSkill: 5);
        midSkill.Should().NotBeNull();
        midSkill!.Id.Should().Be("diamond-universal");

        // Skill 8 unlocks the high-tier variants. Their Power is 0.50 >
        // diamond-universal's 0.0, so MatchImbueRecipe returns one of them
        // (file order picks diamond-high-fire first).
        var highSkill = provider.MatchImbueRecipe("Flawless Diamond", craftingSkill: 8);
        highSkill.Should().NotBeNull();
        highSkill!.Power.Should().BeApproximately(0.50f, 0.0001f);
        highSkill.Id.Should().StartWith("diamond-high-");

        // Below any recipe's skill gate → null
        provider.MatchImbueRecipe("Polished Amethyst", craftingSkill: 1).Should().BeNull();

        // Unknown reagent name → null
        provider.MatchImbueRecipe("Dragonbone Fragment", craftingSkill: 10).Should().BeNull();
        provider.MatchImbueRecipe("", craftingSkill: 10).Should().BeNull();
    }

    // ─── Negative / validation ───────────────────────────────────────────────

    [Fact]
    public void Missing_imbue_recipes_file_is_tolerated()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            // No imbue-recipes.json written — provider should still load and
            // simply expose an empty recipe list.
            var provider = new ContentProvider(dir.FullName);
            provider.AllImbueRecipes().Should().BeEmpty();
            provider.GetImbueRecipe("anything").Should().BeNull();
            provider.MatchImbueRecipe("anything", 10).Should().BeNull();
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Recipe_referencing_unknown_gem_id_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "imbue-recipes.json"), """
            {
              "gems":    [ { "id": "quartz", "tier": 1, "alignment": "neutral" } ],
              "recipes": [ { "id": "ghost-recipe", "gemId": "unicornium", "imbueType": "Fire", "effect": "imbue", "power": 0.1, "requiredCraftingSkill": 1 } ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*unknown gemId 'unicornium'*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Recipe_with_invalid_skill_requirement_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "imbue-recipes.json"), """
            {
              "gems":    [ { "id": "quartz" } ],
              "recipes": [ { "id": "bad-skill", "gemId": "quartz", "imbueType": "Fire", "effect": "imbue", "power": 0.1, "requiredCraftingSkill": 99 } ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*requiredCraftingSkill*out of range*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Duplicate_recipe_ids_throw()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "imbue-recipes.json"), """
            {
              "gems":    [ { "id": "quartz" } ],
              "recipes": [
                { "id": "dup", "gemId": "quartz", "imbueType": "Fire", "effect": "imbue", "power": 0.1, "requiredCraftingSkill": 1 },
                { "id": "dup", "gemId": "quartz", "imbueType": "Water", "effect": "imbue", "power": 0.1, "requiredCraftingSkill": 1 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate imbue recipe id 'dup'*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Recipe_with_power_out_of_range_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "imbue-recipes.json"), """
            {
              "gems":    [ { "id": "quartz" } ],
              "recipes": [ { "id": "nuke", "gemId": "quartz", "imbueType": "Fire", "effect": "imbue", "power": 42.0, "requiredCraftingSkill": 1 } ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*power*out of range*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Invalid_effect_is_rejected()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "imbue-recipes.json"), """
            {
              "gems":    [ { "id": "quartz" } ],
              "recipes": [ { "id": "bogus-effect", "gemId": "quartz", "imbueType": "Fire", "effect": "teleport", "power": 0.1, "requiredCraftingSkill": 1 } ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid effect 'teleport'*");
        }
        finally { dir.Delete(recursive: true); }
    }

    [Fact]
    public void Reload_repopulates_imbue_recipes_from_disk()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        var countBefore = provider.AllImbueRecipes().Count;
        provider.Reload();
        provider.AllImbueRecipes().Should().HaveCount(countBefore);
    }

    [Fact]
    public void Every_real_recipe_has_matching_gem_definition()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        foreach (var recipe in provider.AllImbueRecipes())
        {
            recipe.GemId.Should().NotBeNullOrWhiteSpace(
                $"recipe '{recipe.Id}' must reference a gem");
            recipe.RequiredReagent.Should().NotBeNullOrWhiteSpace(
                $"recipe '{recipe.Id}' must have a requiredReagent (defaults to gemId)");
            recipe.RequiredCraftingSkill.Should().BeInRange(1, 10,
                $"recipe '{recipe.Id}' skill gate must be within tier range");
            recipe.Power.Should().BeInRange(0f, 1f,
                $"recipe '{recipe.Id}' power must be a normalized 0..1 value");
        }
    }

    [Fact]
    public void MatchImbueRecipe_is_case_insensitive_on_reagent_name()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        var mixedCase = provider.MatchImbueRecipe("rAw EmErAlD", craftingSkill: 10);
        mixedCase.Should().NotBeNull();
        mixedCase!.GemId.Should().Be("emerald");
    }

    [Fact]
    public void GetImbueRecipe_null_and_empty_inputs_return_null()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        provider.GetImbueRecipe("").Should().BeNull();
        provider.GetImbueRecipe("no-such-recipe").Should().BeNull();
    }

    [Fact]
    public void Recipe_with_invalid_imbue_type_throws()
    {
        var dir = Directory.CreateTempSubdirectory("fm-imbue-recipes-test-");
        try
        {
            ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "imbue-recipes.json"), """
            {
              "gems":    [ { "id": "quartz" } ],
              "recipes": [ { "id": "bogus-type", "gemId": "quartz", "imbueType": "Plasma", "effect": "imbue", "power": 0.1, "requiredCraftingSkill": 1 } ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid imbueType 'Plasma'*");
        }
        finally { dir.Delete(recursive: true); }
    }
}
