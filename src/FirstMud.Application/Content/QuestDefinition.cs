using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven quest definition loaded from content/quests.json.
///
/// Replaces the hardcoded <c>QuestSeedRecord</c> / <c>BuildQuestSeedData()</c>
/// table in <see cref="FirstMud.Infrastructure.Neo4j.LoreSeeder"/>. Runtime
/// quest-graph traversal, live per-player state and the Neo4j schema itself
/// are unchanged — only the authoring surface moved to JSON.
///
/// <para>
/// In first-mud each quest IS the graph node. A quest's narrative branches
/// (e.g. "reported" vs. "concealed") are represented by the
/// <see cref="PossibleOutcomes"/> array; the traversal between quests lives
/// in the sibling <see cref="QuestEdgeDefinition"/> list (UNLOCKS +
/// REQUIRES_COMPLETION). Inline <see cref="Nodes"/> / <see cref="InternalEdges"/>
/// are reserved for future multi-step quests; they are optional and validated
/// when present, but the existing 13-quest corpus does not use them.
/// </para>
/// </summary>
public sealed record QuestDefinition(
    string QuestId,
    string Title,
    string Description,
    FactionId FactionId,
    ReputationTier RequiredTier,
    WorldId RequiredWorld,
    int ReputationReward,
    IReadOnlyList<string> PossibleOutcomes,
    bool IsWyrdQuest,
    string? StartingZoneId,
    IReadOnlyList<string> Prerequisites,
    IReadOnlyList<QuestRewardDefinition> Rewards,
    IReadOnlyList<QuestNodeDefinition> Nodes,
    IReadOnlyList<QuestEdgeDefinition> InternalEdges);

/// <summary>
/// Optional intra-quest step node, for quests that are themselves a small
/// state-machine (dialogue/kill/collect/visit). The existing FirstMud corpus
/// treats each quest as atomic; this record is in place so future multi-step
/// quests can author sub-nodes without another migration.
/// </summary>
public sealed record QuestNodeDefinition(
    string NodeId,
    string Type,
    string Content,
    IReadOnlyList<string> RequiredFlags);

/// <summary>
/// Directed edge in the cross-quest graph authored in content/quests.json.
///
/// <list type="bullet">
/// <item><c>Kind = "unlocks"</c> with a non-null <see cref="Outcome"/> — maps
///       to Neo4j <c>UNLOCKS {outcome}</c>.</item>
/// <item><c>Kind = "requires"</c> — maps to Neo4j
///       <c>REQUIRES_COMPLETION</c>.</item>
/// </list>
/// </summary>
public sealed record QuestEdgeDefinition(
    string Kind,
    string FromQuestId,
    string ToQuestId,
    string? Outcome);

/// <summary>Quest reward payload. Item names are cross-checked against the
/// content item catalog once it exists; today the check is forgiving (unknown
/// items are accepted with a load-time warning).</summary>
public sealed record QuestRewardDefinition(
    string Kind,
    string? ItemName,
    int Quantity);
