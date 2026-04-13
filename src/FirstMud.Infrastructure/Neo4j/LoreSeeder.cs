using FirstMud.Application.Content;
using FirstMud.Domain.Enums;
using Neo4j.Driver;

namespace FirstMud.Infrastructure.Neo4j;

/// <summary>
/// Seeds the initial quest graph from <see cref="IContentProvider"/>
/// (content/quests.json). Uses MERGE throughout so it is safe to run on
/// every application startup — identical idempotency model to the
/// consumable/recipe/zone migrations that preceded it.
///
/// <para>
/// The runtime quest-graph stored in Neo4j, traversal code, and live
/// per-player state are unchanged by the JSON migration. Only the
/// authoring surface moved — the hardcoded <c>BuildQuestSeedData()</c>
/// table and the <c>unlocks</c> / <c>requires</c> tuple arrays are now
/// loaded by <see cref="ContentProvider"/> and replayed here.
/// </para>
/// </summary>
public sealed class LoreSeeder
{
    private readonly Neo4jDriverWrapper _driver;
    private readonly IContentProvider _content;

    public LoreSeeder(Neo4jDriverWrapper driver, IContentProvider content)
    {
        _driver = driver;
        _content = content;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await CreateIndexesAsync(cancellationToken);
        await SeedQuestsAsync(cancellationToken);
        await SeedRelationshipsAsync(cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Indexes
    // -------------------------------------------------------------------------

    private async Task CreateIndexesAsync(CancellationToken cancellationToken)
    {
        await _driver.ExecuteWriteAsync(async tx =>
        {
            await tx.RunAsync("CREATE INDEX quest_id IF NOT EXISTS FOR (q:Quest) ON (q.questId)");
            await tx.RunAsync("CREATE INDEX player_id IF NOT EXISTS FOR (p:Player) ON (p.playerId)");
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Quest nodes
    // -------------------------------------------------------------------------

    private async Task SeedQuestsAsync(CancellationToken cancellationToken)
    {
        var quests = _content.AllQuests();

        await _driver.ExecuteWriteAsync(async tx =>
        {
            foreach (var q in quests)
            {
                await tx.RunAsync(
                    """
                    MERGE (q:Quest {questId: $questId})
                    SET q.title            = $title,
                        q.description      = $description,
                        q.factionId        = $factionId,
                        q.requiredTier     = $requiredTier,
                        q.requiredWorld    = $requiredWorld,
                        q.reputationReward = $reputationReward,
                        q.possibleOutcomes = $possibleOutcomes,
                        q.isWyrdQuest      = $isWyrdQuest
                    """,
                    new
                    {
                        questId          = q.QuestId,
                        title            = q.Title,
                        description      = q.Description,
                        factionId        = (int)q.FactionId,
                        requiredTier     = (int)q.RequiredTier,
                        requiredWorld    = (int)q.RequiredWorld,
                        reputationReward = q.ReputationReward,
                        possibleOutcomes = q.PossibleOutcomes.ToArray(),
                        isWyrdQuest      = q.IsWyrdQuest
                    });
            }
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Relationships
    // -------------------------------------------------------------------------

    private async Task SeedRelationshipsAsync(CancellationToken cancellationToken)
    {
        var edges = _content.AllQuestEdges();

        await _driver.ExecuteWriteAsync(async tx =>
        {
            foreach (var e in edges)
            {
                if (string.Equals(e.Kind, "unlocks", StringComparison.Ordinal))
                {
                    await tx.RunAsync(
                        """
                        MATCH (a:Quest {questId: $from}), (b:Quest {questId: $to})
                        MERGE (a)-[:UNLOCKS {outcome: $outcome}]->(b)
                        """,
                        new { from = e.FromQuestId, to = e.ToQuestId, outcome = e.Outcome ?? "" });
                }
                else if (string.Equals(e.Kind, "requires", StringComparison.Ordinal))
                {
                    await tx.RunAsync(
                        """
                        MATCH (a:Quest {questId: $prereq}), (b:Quest {questId: $dependent})
                        MERGE (a)-[:REQUIRES_COMPLETION {questId: $prereq}]->(b)
                        """,
                        new { prereq = e.FromQuestId, dependent = e.ToQuestId });
                }
                // "internal" edges live inside a quest's state-machine; not
                // currently projected into the cross-quest Neo4j graph.
            }
        }, cancellationToken);
    }
}
