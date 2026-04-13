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

    // ─── Monsters ────────────────────────────────────────────────────────────

    /// <summary>All monster definitions, in file order.</summary>
    IReadOnlyList<MonsterDefinition> AllMonsters();

    /// <summary>Lookup a monster by its stable id. Returns null if unknown.</summary>
    MonsterDefinition? GetMonster(string id);

    /// <summary>All monsters whose biome matches (ordinal equality, lowercase).</summary>
    IReadOnlyList<MonsterDefinition> MonstersByBiome(string biome);

    /// <summary>All monsters in a given biome + tier (0-3).</summary>
    IReadOnlyList<MonsterDefinition> MonstersByBiomeAndTier(string biome, int tier);

    // ─── Loot Tables ─────────────────────────────────────────────────────────

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

    // ─── Zones ───────────────────────────────────────────────────────────────

    /// <summary>All zone definitions, in file order.</summary>
    IReadOnlyList<ZoneDefinition> AllZones();

    /// <summary>
    /// Returns the zone definition with the given <c>zoneId</c>, or null.
    /// </summary>
    ZoneDefinition? GetZone(string zoneId);

    /// <summary>
    /// Biome string for the zone identified by name (matches <see cref="Domain.Entities.Zone.Name"/>),
    /// or null if the name is not present in zones.json. Replaces the switch
    /// that previously lived in BiomeService.GetBiome(Zone).
    /// </summary>
    string? GetBiomeForZone(string zoneName);

    /// <summary>
    /// Hand-crafted grid position for a seeded zone identified by
    /// (world, zoneNumber), or null if no such zone is authored. Replaces the
    /// KnownPositions dictionary in ZoneGridLayout.
    /// </summary>
    (int X, int Y)? GetZoneLayoutPosition(WorldId world, int zoneNumber);

    // ─── Factions ────────────────────────────────────────────────────────────

    /// <summary>All faction definitions, in file order.</summary>
    IReadOnlyList<FactionDefinition> AllFactions();

    /// <summary>Lookup a faction by its enum id. Returns null if no definition exists.</summary>
    FactionDefinition? GetFaction(FactionId id);

    // ─── Quests ──────────────────────────────────────────────────────────────

    /// <summary>All quest definitions authored in content/quests.json,
    /// in file order.</summary>
    IReadOnlyList<QuestDefinition> AllQuests();

    /// <summary>
    /// Returns the quest definition with the given <c>questId</c>, or null
    /// if no such quest is authored.
    /// </summary>
    QuestDefinition? GetQuest(string id);

    /// <summary>All cross-quest edges (unlocks + requires) authored in
    /// content/quests.json.</summary>
    IReadOnlyList<QuestEdgeDefinition> AllQuestEdges();

    // ─── Combat Curves ───────────────────────────────────────────────────────

    /// <summary>
    /// Tunable combat scaling coefficients (monster danger-level multipliers,
    /// boss bonuses). Loaded from <c>content/combat-curves.json</c>. Consumed
    /// by both the live game's <c>MonsterFactory</c> and the design-time
    /// <c>encounter-sim</c> tool — one source of truth.
    /// </summary>
    CombatCurvesDefinition CombatCurves { get; }

    // ─── NPCs ────────────────────────────────────────────────────────────────

    /// <summary>All NPC definitions, in file order.</summary>
    IReadOnlyList<NpcDefinition> AllNpcs();

    /// <summary>Lookup an NPC by its stable id. Returns null if unknown.</summary>
    NpcDefinition? GetNpc(string id);

    // ─── Maintenance ─────────────────────────────────────────────────────────

    /// <summary>Force a reload from disk — supports hot-reload in dev.</summary>
    void Reload();
}
