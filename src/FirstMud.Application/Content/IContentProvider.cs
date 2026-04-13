namespace FirstMud.Application.Content;

/// <summary>
/// Central registry for data-driven game content loaded from JSON files in
/// the repo-root <c>content/</c> folder.
///
/// Current scope: consumables + loot tables. The provider is designed to
/// expand — adding a new content cluster means a new property and a new
/// loader call, not a new service.
/// </summary>
public interface IContentProvider
{
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

    /// <summary>Complete loot-table data block.</summary>
    LootTablesDefinition LootTables { get; }

    /// <summary>Drop pool lookup by id, or null if unknown.</summary>
    DropPoolDefinition? GetDropPool(string id);

    /// <summary>All drop pools, in file order.</summary>
    IReadOnlyList<DropPoolDefinition> AllDropPools();

    /// <summary>
    /// Returns the monster-drop mapping for <paramref name="monsterId"/>, or
    /// null if none. Current first-mud loot is biome-keyed — this will return
    /// null until monster ids stabilise, but the contract exists so callers
    /// can migrate in-place.
    /// </summary>
    MonsterDropDefinition? GetMonsterDrop(string monsterId);

    /// <summary>Tier/workmanship curve row, or null if tier out of range.</summary>
    TierCurveDefinition? GetTierCurve(int tier);

    /// <summary>Force a reload from disk — supports hot-reload in dev.</summary>
    void Reload();
}
