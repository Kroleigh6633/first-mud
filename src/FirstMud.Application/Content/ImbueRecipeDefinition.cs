using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven imbue-recipe definition, loaded from content/imbue-recipes.json.
///
/// Each recipe maps a specific reagent (today, a gem by id) plus a crafting
/// skill gate onto an <see cref="ImbueType"/> and a power value. Consumed by
/// <c>ImbueWithGemCommandHandler</c> to resolve what happens when a player
/// socket-imbues an item with a gem.
///
/// Disambiguation when multiple recipes share a reagent:
///   <see cref="ContentProvider.MatchImbueRecipe"/> returns the highest-power
///   recipe whose RequiredCraftingSkill ≤ the player's skill, breaking ties
///   by file order (first wins). Callers that need a specific outcome (e.g.
///   topaz-fire vs topaz-air) MUST pass an explicit RecipeId.
/// </summary>
public sealed record ImbueRecipeDefinition(
    string Id,
    string GemId,
    string RequiredReagent,
    int RequiredCraftingSkill,
    ImbueType ImbueType,
    string Effect,              // "imbue" | "upgrade" | "successBoost"
    float Power,
    float Variance,
    int SuccessBonus,
    bool UpgradesExisting,
    string Description);
