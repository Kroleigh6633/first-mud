namespace FirstMud.Application.Content;

/// <summary>
/// Central registry for data-driven game content loaded from JSON files in
/// the repo-root <c>content/</c> folder.
///
/// Pilot scope: consumables only. The provider is designed to expand —
/// adding a new content cluster means a new property and a new loader
/// call, not a new service.
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

    /// <summary>All monster definitions, in file order.</summary>
    IReadOnlyList<MonsterDefinition> AllMonsters();

    /// <summary>Lookup a monster by its stable id. Returns null if unknown.</summary>
    MonsterDefinition? GetMonster(string id);

    /// <summary>All monsters whose biome matches (ordinal equality, lowercase).</summary>
    IReadOnlyList<MonsterDefinition> MonstersByBiome(string biome);

    /// <summary>All monsters in a given biome + tier (0-3).</summary>
    IReadOnlyList<MonsterDefinition> MonstersByBiomeAndTier(string biome, int tier);

    /// <summary>Force a reload from disk — supports hot-reload in dev.</summary>
    void Reload();
}
