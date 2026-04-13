using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// A single ingredient line on a recipe: category + canonical ingredient
/// name + base (unseeded) quantity. Matches the shape of
/// <see cref="RecipeIngredient"/> but lives in the data-driven content layer.
/// </summary>
public sealed record RecipeIngredientDefinition(
    ItemCategory Category,
    string Name,
    int BaseQuantity);

/// <summary>
/// Data-driven recipe definition, loaded from content/recipes.json.
///
/// Replaces the hardcoded <c>AllRecipeDefinitions</c> tuple array at the top
/// of <see cref="GameServer.Services.StartupSeeder"/>. The runtime
/// <see cref="Recipe"/> entity is still constructed from these at seed time;
/// runtime logic (outcome rolls, stacking, taper handling) stays in
/// <see cref="Services.CraftingService"/>.
/// </summary>
public sealed record RecipeDefinition(
    string RecipeId,
    string Name,
    string ResultItemName,
    ItemCategory ResultCategory,
    int RequiredCraftingSkill,
    WorldId RequiredWorld,
    TaperType? RequiredTaperType,
    int BaseWorkmanshipMin,
    int BaseWorkmanshipMax,
    bool IsDiscoverable,
    IReadOnlyList<RecipeIngredientDefinition> Ingredients);
