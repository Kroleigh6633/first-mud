using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using Neo4j.Driver;

namespace FirstMud.Infrastructure.Neo4j;

/// <summary>
/// Neo4j implementation of IQuestGraphRepository.
///
/// Node labels / structure:
///   (:Quest  {questId, title, description, factionId, requiredTier, requiredWorld,
///             reputationReward, possibleOutcomes, isWyrdQuest})
///   (:Player {playerId})
///
/// Relationships:
///   (:Quest)-[:UNLOCKS         {outcome}]->(:Quest)
///   (:Quest)-[:REQUIRES_COMPLETION {questId}]->(:Quest)
///   (:Player)-[:COMPLETED      {outcome, completedAt}]->(:Quest)
///   (:Player)-[:IN_PROGRESS    {takenByAi}]->(:Quest)
/// </summary>
public sealed class QuestGraphRepository : IQuestGraphRepository
{
    private readonly Neo4jDriverWrapper _driver;

    public QuestGraphRepository(Neo4jDriverWrapper driver)
    {
        _driver = driver;
    }

    // -------------------------------------------------------------------------
    // GetQuestAsync
    // -------------------------------------------------------------------------

    public async Task<QuestNode?> GetQuestAsync(string questId, CancellationToken cancellationToken = default)
    {
        return await _driver.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                """
                MATCH (q:Quest {questId: $questId})
                OPTIONAL MATCH (ai:Player)-[ip:IN_PROGRESS {takenByAi: true}]->(q)
                RETURN q, ai IS NOT NULL AS isTaken
                """,
                new { questId });

            if (!await cursor.FetchAsync())
                return null;

            var record = cursor.Current;
            return MapQuestNode(record["q"].As<INode>(), record["isTaken"].As<bool>());
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // GetAvailableQuestsAsync
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<QuestNode>> GetAvailableQuestsAsync(
        Guid playerId,
        FactionId? factionId = null,
        CancellationToken cancellationToken = default)
    {
        var playerIdStr = playerId.ToString();

        return await _driver.ExecuteReadAsync(async tx =>
        {
            // A quest is available when:
            //   1. The player has NOT completed it.
            //   2. The quest is either a root quest (no prerequisites) OR all its
            //      prerequisites have been completed by this player.
            //   3. Optionally filtered by factionId.
            //
            // "Root quest" = a Quest with no incoming REQUIRES_COMPLETION edges.
            // "Unlocked by completion" = quest is reachable via UNLOCKS edges whose
            //   outcome matches the player's COMPLETED outcome on the predecessor.
            //
            // The query merges both cases:
            //   - Root quests (no prerequisites at all)
            //   - Quests whose every prerequisite has been fulfilled

            var parameters = new Dictionary<string, object>
            {
                ["playerId"] = playerIdStr,
                ["factionId"] = factionId.HasValue ? (int)factionId.Value : (object)0
            };

            var factionFilter = factionId.HasValue
                ? "AND q.factionId = $factionId"
                : string.Empty;

            var query = $$"""
                // Ensure player node exists conceptually (we only read here)
                MATCH (q:Quest)
                WHERE NOT (:Player {playerId: $playerId})-[:COMPLETED]->(q)
                  {{factionFilter}}

                // Root quests: no incoming REQUIRES_COMPLETION edges
                // OR: every prerequisite is completed by this player
                AND (
                  NOT (:Quest)-[:REQUIRES_COMPLETION]->(q)
                  OR
                  ALL(prereq IN [(pre:Quest)-[:REQUIRES_COMPLETION]->(q) | pre]
                      WHERE (:Player {playerId: $playerId})-[:COMPLETED]->(prereq))
                )

                // Must be reachable: either root (no incoming UNLOCKS) or unlocked via
                // a completion the player already has with the right outcome
                AND (
                  NOT (:Quest)-[:UNLOCKS]->(q)
                  OR
                  EXISTS {
                    MATCH (prev:Quest)-[ul:UNLOCKS]->(q)
                    WHERE (:Player {playerId: $playerId})-[:COMPLETED {outcome: ul.outcome}]->(prev)
                  }
                )

                OPTIONAL MATCH (p:Player {playerId: $playerId})-[pip:IN_PROGRESS]->(q)
                RETURN q, pip IS NOT NULL AS isTaken
                """;

            var cursor = await tx.RunAsync(query, parameters);
            var results = new List<QuestNode>();
            while (await cursor.FetchAsync())
            {
                results.Add(MapQuestNode(
                    cursor.Current["q"].As<INode>(),
                    cursor.Current["isTaken"].As<bool>()));
            }
            return results;
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // GetQuestsByTierAsync
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<QuestNode>> GetQuestsByTierAsync(
        ReputationTier tier,
        FactionId factionId,
        CancellationToken cancellationToken = default)
    {
        return await _driver.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                """
                MATCH (q:Quest {requiredTier: $tier, factionId: $factionId})
                OPTIONAL MATCH (ai:Player)-[ip:IN_PROGRESS {takenByAi: true}]->(q)
                RETURN q, ai IS NOT NULL AS isTaken
                """,
                new { tier = (int)tier, factionId = (int)factionId });

            var results = new List<QuestNode>();
            while (await cursor.FetchAsync())
            {
                results.Add(MapQuestNode(
                    cursor.Current["q"].As<INode>(),
                    cursor.Current["isTaken"].As<bool>()));
            }
            return results;
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // MarkQuestCompletedAsync
    // -------------------------------------------------------------------------

    public async Task MarkQuestCompletedAsync(
        Guid playerId,
        string questId,
        string chosenOutcome,
        CancellationToken cancellationToken = default)
    {
        var playerIdStr = playerId.ToString();
        var completedAt = DateTimeOffset.UtcNow.ToString("O");

        await _driver.ExecuteWriteAsync(async tx =>
        {
            // 1. Ensure player node exists.
            // 2. Remove any IN_PROGRESS relationship.
            // 3. Create COMPLETED relationship with outcome and timestamp.
            await tx.RunAsync(
                """
                MERGE (p:Player {playerId: $playerId})
                WITH p
                MATCH (q:Quest {questId: $questId})
                OPTIONAL MATCH (p)-[ip:IN_PROGRESS]->(q)
                DELETE ip
                MERGE (p)-[:COMPLETED {outcome: $outcome, completedAt: $completedAt}]->(q)
                """,
                new { playerId = playerIdStr, questId, outcome = chosenOutcome, completedAt });
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // GetUnlockedByCompletionAsync
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<QuestNode>> GetUnlockedByCompletionAsync(
        string completedQuestId,
        string outcome,
        CancellationToken cancellationToken = default)
    {
        return await _driver.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                """
                MATCH (completed:Quest {questId: $completedQuestId})-[:UNLOCKS {outcome: $outcome}]->(next:Quest)
                OPTIONAL MATCH (ai:Player)-[ip:IN_PROGRESS {takenByAi: true}]->(next)
                RETURN next AS q, ai IS NOT NULL AS isTaken
                """,
                new { completedQuestId, outcome });

            var results = new List<QuestNode>();
            while (await cursor.FetchAsync())
            {
                results.Add(MapQuestNode(
                    cursor.Current["q"].As<INode>(),
                    cursor.Current["isTaken"].As<bool>()));
            }
            return results;
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // IsQuestAvailableAsync
    // -------------------------------------------------------------------------

    public async Task<bool> IsQuestAvailableAsync(
        Guid playerId,
        string questId,
        CancellationToken cancellationToken = default)
    {
        var playerIdStr = playerId.ToString();

        return await _driver.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                """
                MATCH (q:Quest {questId: $questId})

                // Not already completed
                WHERE NOT (:Player {playerId: $playerId})-[:COMPLETED]->(q)

                // Prerequisites satisfied
                AND (
                  NOT (:Quest)-[:REQUIRES_COMPLETION]->(q)
                  OR
                  ALL(prereq IN [(pre:Quest)-[:REQUIRES_COMPLETION]->(q) | pre]
                      WHERE (:Player {playerId: $playerId})-[:COMPLETED]->(prereq))
                )

                // Reachable via UNLOCKS chain
                AND (
                  NOT (:Quest)-[:UNLOCKS]->(q)
                  OR
                  EXISTS {
                    MATCH (prev:Quest)-[ul:UNLOCKS]->(q)
                    WHERE (:Player {playerId: $playerId})-[:COMPLETED {outcome: ul.outcome}]->(prev)
                  }
                )

                RETURN count(q) > 0 AS available
                """,
                new { questId, playerId = playerIdStr });

            if (!await cursor.FetchAsync()) return false;
            return cursor.Current["available"].As<bool>();
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // IsQuestInProgressAsync
    // -------------------------------------------------------------------------

    public async Task<bool> IsQuestInProgressAsync(
        Guid playerId,
        string questId,
        CancellationToken cancellationToken = default)
    {
        var playerIdStr = playerId.ToString();

        return await _driver.ExecuteReadAsync(async tx =>
        {
            var cursor = await tx.RunAsync(
                """
                MATCH (p:Player {playerId: $playerId})-[:IN_PROGRESS]->(q:Quest {questId: $questId})
                RETURN count(q) > 0 AS inProgress
                """,
                new { playerId = playerIdStr, questId });

            if (!await cursor.FetchAsync()) return false;
            return cursor.Current["inProgress"].As<bool>();
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // MarkQuestInProgressAsync
    // -------------------------------------------------------------------------

    public async Task MarkQuestInProgressAsync(
        Guid playerId,
        string questId,
        bool takenByAi = false,
        CancellationToken ct = default)
    {
        var playerIdStr = playerId.ToString();

        await _driver.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync(
                """
                MERGE (p:Player {playerId: $playerId})
                MERGE (q:Quest {questId: $questId})
                MERGE (p)-[r:IN_PROGRESS]->(q)
                SET r.takenByAi = $takenByAi, r.startedAt = datetime()
                """,
                new { playerId = playerIdStr, questId, takenByAi });
        }, ct);
    }

    // -------------------------------------------------------------------------
    // Mapping helper
    // -------------------------------------------------------------------------

    private static QuestNode MapQuestNode(INode node, bool isTaken)
    {
        var possibleOutcomesRaw = node["possibleOutcomes"].As<List<object>>();
        var possibleOutcomes = possibleOutcomesRaw.Select(o => o.ToString()!).ToArray();

        return new QuestNode(
            QuestId: node["questId"].As<string>(),
            Title: node["title"].As<string>(),
            Description: node["description"].As<string>(),
            FactionId: (FactionId)node["factionId"].As<int>(),
            RequiredTier: (ReputationTier)node["requiredTier"].As<int>(),
            RequiredWorld: (WorldId)node["requiredWorld"].As<int>(),
            ReputationReward: node["reputationReward"].As<int>(),
            PossibleOutcomes: possibleOutcomes,
            IsWyrdQuest: node["isWyrdQuest"].As<bool>(),
            IsTaken: isTaken);
    }
}
