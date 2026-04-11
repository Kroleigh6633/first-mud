using FirstMud.Domain.Enums;
using Neo4j.Driver;

namespace FirstMud.Infrastructure.Neo4j;

/// <summary>
/// Seeds the initial quest graph from the FirstMud lore documents.
/// Uses MERGE throughout so it is safe to run on every application startup.
/// </summary>
public sealed class LoreSeeder
{
    private readonly Neo4jDriverWrapper _driver;

    public LoreSeeder(Neo4jDriverWrapper driver)
    {
        _driver = driver;
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
        var quests = BuildQuestSeedData();

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
                        possibleOutcomes = q.PossibleOutcomes,
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
        // UNLOCKS edges: (fromQuestId, outcome, toQuestId)
        var unlocks = new[]
        {
            // Chain 1 — The Rider's First Road
            ("RIDER_001", "reported",  "RIDER_002a"),
            ("RIDER_001", "concealed", "RIDER_002b"),

            // Chain 2 — The Thornwood Covens
            ("THORN_001", "helped",   "THORN_002"),
            ("THORN_002", "completed","THORN_003"),
            ("THORN_003", "completed","THORN_004"),

            // Chain 3 — The Gravenguard Portal Chain
            ("GRAVE_001", "completed","GRAVE_002"),
            ("GRAVE_002", "completed","GRAVE_003"),

            // Chain 4 — The Fairgean First Contact
            ("FAIR_001",  "survived_peacefully", "FAIR_002"),
        };

        // REQUIRES_COMPLETION edges: (prereqQuestId, dependentQuestId)
        // These mirror the primary chain prerequisites explicitly.
        var requires = new[]
        {
            ("RIDER_001", "RIDER_002a"),
            ("RIDER_001", "RIDER_002b"),
            ("THORN_001", "THORN_002"),
            ("THORN_002", "THORN_003"),
            ("THORN_003", "THORN_004"),
            ("GRAVE_001", "GRAVE_002"),
            ("GRAVE_002", "GRAVE_003"),
            ("FAIR_001",  "FAIR_002"),
        };

        await _driver.ExecuteWriteAsync(async tx =>
        {
            foreach (var (from, outcome, to) in unlocks)
            {
                await tx.RunAsync(
                    """
                    MATCH (a:Quest {questId: $from}), (b:Quest {questId: $to})
                    MERGE (a)-[:UNLOCKS {outcome: $outcome}]->(b)
                    """,
                    new { from, outcome, to });
            }

            foreach (var (prereq, dependent) in requires)
            {
                await tx.RunAsync(
                    """
                    MATCH (a:Quest {questId: $prereq}), (b:Quest {questId: $dependent})
                    MERGE (a)-[:REQUIRES_COMPLETION {questId: $prereq}]->(b)
                    """,
                    new { prereq, dependent });
            }
        }, cancellationToken);
    }

    // -------------------------------------------------------------------------
    // Seed data definitions
    // -------------------------------------------------------------------------

    private static IReadOnlyList<QuestSeedRecord> BuildQuestSeedData() =>
    [
        // ----------------------------------------------------------------
        // Chain 1: The Rider's First Road (House Caervorn)
        // ----------------------------------------------------------------
        new(
            QuestId: "RIDER_001",
            Title: "A Delivery Gone Wrong",
            Description:
                "Your first delivery as a Rider is intercepted on the road. Someone wanted what was in that package — " +
                "and they knew you were carrying it. You must decide: report the interception to your Caervorn contact, " +
                "or conceal what happened and try to recover the package yourself.",
            FactionId: FactionId.HouseCaervorn,
            RequiredTier: ReputationTier.Unknown,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 150,
            PossibleOutcomes: ["reported", "concealed"],
            IsWyrdQuest: false),

        new(
            QuestId: "RIDER_002a",
            Title: "What Was In The Package",
            Description:
                "You reported the interception to your Caervorn handler. Now they want you to find out what was stolen " +
                "and why it matters. The answer is more uncomfortable than expected — the package contained correspondence " +
                "that House Caervorn would prefer stayed private.",
            FactionId: FactionId.HouseCaervorn,
            RequiredTier: ReputationTier.Known,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 300,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        new(
            QuestId: "RIDER_002b",
            Title: "Finding The Interceptors",
            Description:
                "You concealed the interception and now you're working alone. Track down whoever hit your delivery — " +
                "but the trail leads toward the Thornwood and people who are very good at not being found.",
            FactionId: FactionId.ThornwoodCovens,
            RequiredTier: ReputationTier.Known,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 300,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        // ----------------------------------------------------------------
        // Chain 2: The Thornwood Covens
        // ----------------------------------------------------------------
        new(
            QuestId: "THORN_001",
            Title: "The Mage on the Moor",
            Description:
                "On the open moor, you come across a mage being hunted by Caervorn patrols. She is injured. " +
                "Helping her will risk your Caervorn standing. Ignoring her is the safe choice. " +
                "The Thornwood Covens will remember which one you make.",
            FactionId: FactionId.ThornwoodCovens,
            RequiredTier: ReputationTier.Unknown,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 200,
            PossibleOutcomes: ["helped", "ignored"],
            IsWyrdQuest: false),

        new(
            QuestId: "THORN_002",
            Title: "Safe Passage",
            Description:
                "The mage you helped on the moor needs to reach the Thornwood. Three days on foot through " +
                "Caervorn-patrolled roads — and she is still not well enough to Work defensively. " +
                "Your horse, your brooch, and your judgment will be tested.",
            FactionId: FactionId.ThornwoodCovens,
            RequiredTier: ReputationTier.Known,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 400,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        new(
            QuestId: "THORN_003",
            Title: "The Rootweave Hums",
            Description:
                "Something is wrong with the Rootweave — the living magical network embedded in the Thornwood. " +
                "The Covens can hear it but not locate the cause. You are new to the Thornwood and that means " +
                "you might notice something the Covens have stopped seeing. Find the dissonance before it spreads.",
            FactionId: FactionId.ThornwoodCovens,
            RequiredTier: ReputationTier.Trusted,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 600,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        new(
            QuestId: "THORN_004",
            Title: "Eldest Mira Asks",
            Description:
                "Eldest Mira Ashvale wants to meet you personally. She does not explain why. " +
                "She has lived through events that destroyed three other factions and she is not alarmed by Aldric Caervorn. " +
                "That either means she has a plan, or she knows something no one else does. " +
                "This meeting will change the shape of everything that follows.",
            FactionId: FactionId.ThornwoodCovens,
            RequiredTier: ReputationTier.Honored,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 800,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: true),

        // ----------------------------------------------------------------
        // Chain 3: The Gravenguard Portal Chain
        // ----------------------------------------------------------------
        new(
            QuestId: "GRAVE_001",
            Title: "Maps and Rumors",
            Description:
                "A cartographer in Gravenhold mentions an Ardweld ruin at Coldmere that none of the Gravenguard's " +
                "official maps acknowledge. Commander Drest has not approved an expedition. " +
                "The cartographer will not say how she knows. Find out if the ruin is real.",
            FactionId: FactionId.Gravenguard,
            RequiredTier: ReputationTier.Known,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 350,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        new(
            QuestId: "GRAVE_002",
            Title: "The Ruin at Coldmere",
            Description:
                "The ruin is real. It is also partially active — ward-engines still responding to the Weave " +
                "after a thousand years. Among the debris you find a partial portal key. " +
                "It was not lost by accident. Someone hid it here deliberately. " +
                "The Gravenguard needs to know what they are looking at.",
            FactionId: FactionId.Gravenguard,
            RequiredTier: ReputationTier.Trusted,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 600,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        new(
            QuestId: "GRAVE_003",
            Title: "Commander Drest's Secret",
            Description:
                "The portal key from Coldmere connects to the lost expedition's disappearance — " +
                "and Commander Drest knows it. He has been carrying this secret alone for months. " +
                "You must decide: press him for the full truth and risk the Gravenguard fracturing, " +
                "or help him bury it deeper and protect what stability remains.",
            FactionId: FactionId.Gravenguard,
            RequiredTier: ReputationTier.Honored,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 900,
            PossibleOutcomes: ["revealed", "buried"],
            IsWyrdQuest: true),

        // ----------------------------------------------------------------
        // Chain 4: The Fairgean First Contact
        // ----------------------------------------------------------------
        new(
            QuestId: "FAIR_001",
            Title: "Shore Crossing",
            Description:
                "Riding the Drowned Coast, you encounter Fairgean who have come inland — the first time in " +
                "two hundred years. They are not attacking. They are watching. Whether that changes " +
                "depends entirely on what you do in the next few moments.",
            FactionId: FactionId.Fairgean,
            RequiredTier: ReputationTier.Unknown,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 250,
            PossibleOutcomes: ["survived_peacefully", "survived_violently"],
            IsWyrdQuest: false),

        new(
            QuestId: "FAIR_002",
            Title: "Why They Came Inland",
            Description:
                "A Fairgean scout who speaks the surface tongue is willing to talk — because you did not attack. " +
                "What drove the Fairgean from the deep is not a human problem yet. " +
                "It will be. They need someone on the surface to understand before it is too late to act.",
            FactionId: FactionId.Fairgean,
            RequiredTier: ReputationTier.Known,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 450,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: false),

        // ----------------------------------------------------------------
        // Chain 5: The Ashen Watcher
        // ----------------------------------------------------------------
        new(
            QuestId: "ASHEN_001",
            Title: "A Dream You Can't Explain",
            Description:
                "You did not choose to sleep. You did not choose to dream. And you did not choose to find " +
                "yourself in a grey, ash-lit city that cannot exist — standing before a figure that knows " +
                "your name before you have said it. This is not a nightmare. The Ashen Court has noticed you.",
            FactionId: FactionId.AshenCourt,
            RequiredTier: ReputationTier.Unknown,
            RequiredWorld: WorldId.Aeldran,
            ReputationReward: 0,
            PossibleOutcomes: ["completed"],
            IsWyrdQuest: true),
    ];

    // -------------------------------------------------------------------------
    // Private seed record type
    // -------------------------------------------------------------------------

    private sealed record QuestSeedRecord(
        string QuestId,
        string Title,
        string Description,
        FactionId FactionId,
        ReputationTier RequiredTier,
        WorldId RequiredWorld,
        int ReputationReward,
        string[] PossibleOutcomes,
        bool IsWyrdQuest);
}
