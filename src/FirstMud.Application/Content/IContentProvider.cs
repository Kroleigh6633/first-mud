using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Central registry for data-driven game content loaded from JSON files in
/// the repo-root <c>content/</c> folder.
///
/// Adding a new content cluster means a new property/method and a new loader
/// call, not a new service.
/// </summary>
public interface IContentProvider
{
    // ─── Consumables ─────────────────────────────────────────────────────────

    /// <summary>All consumable definitions, in file order.</summary>
    IReadOnlyList<ConsumableDefinition> Consumables { get; }

    /// <summary>
    /// Returns the first consumable definition whose <c>MatchToken</c> is
    /// contained in the given item name (case-insensitive), or null.
    /// Preserves the ordering semantics of the original switch statement.
    /// </summary>
    ConsumableDefinition? ResolveConsumable(string itemName);

    /// <summary>
    /// Consumables in a given priority group sorted by priorityRank ascending.
    /// Used by auto-farm to build its preference lists.
    /// </summary>
    IReadOnlyList<ConsumableDefinition> ConsumablesByGroup(string priorityGroup);

    // ─── Buildings ───────────────────────────────────────────────────────────

    /// <summary>All building definitions, in file order.</summary>
    IReadOnlyList<BuildingDefinition> AllBuildings();

    /// <summary>
    /// Returns the building definition for the given type, or null if the
    /// type is not defined. Under normal operation every <see cref="BuildingType"/>
    /// enum value has a definition — a null return indicates a malformed
    /// buildings.json that slipped past startup validation.
    /// </summary>
    BuildingDefinition? GetBuilding(BuildingType type);

    /// <summary>
    /// Resident capacity of a housing building at the given tier (1-indexed).
    /// Returns 0 for non-housing types. Tiers outside the defined range clamp
    /// to the first tier (preserving legacy <c>GetHutCapacity</c> fallback).
    /// </summary>
    int GetHutCapacity(BuildingType type, int tier);

    // ─── Recipes ─────────────────────────────────────────────────────────────

    /// <summary>All recipe definitions, in file order.</summary>
    IReadOnlyList<RecipeDefinition> AllRecipes();

    /// <summary>
    /// Returns the recipe definition with the given <c>recipeId</c>, or null.
    /// </summary>
    RecipeDefinition? GetRecipe(string recipeId);

    // ─── Maintenance ─────────────────────────────────────────────────────────

    /// <summary>Force a reload from disk — supports hot-reload in dev.</summary>
    void Reload();
}
