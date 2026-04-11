using FirstMud.Domain.Enums;
using FirstMud.Infrastructure.Neo4j;
using FirstMud.IntegrationTests.Fixtures;
using FluentAssertions;

namespace FirstMud.IntegrationTests.Neo4j;

/// <summary>
/// Integration tests for <see cref="QuestGraphRepository"/> against a real Neo4j 5 container.
/// Each test creates its own driver wrapper to avoid session state leaking between tests.
/// LoreSeeder is run once per test class because the collection shares a single container.
/// </summary>
[Collection("Neo4j")]
[Trait("Category", "Integration")]
public sealed class QuestGraphRepositoryTests : IAsyncLifetime
{
    private readonly Neo4jFixture _fixture;

    public QuestGraphRepositoryTests(Neo4jFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        // Seed lore once for all tests in this class. MERGE is idempotent.
        var seeder = _fixture.CreateLoreSeeder();
        await seeder.SeedAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetAvailableQuests_ForNewPlayer_ReturnsRootQuests()
    {
        var repo = _fixture.CreateQuestGraphRepository();
        var newPlayerId = Guid.NewGuid();

        var quests = await repo.GetAvailableQuestsAsync(newPlayerId);

        // Root quests have no prerequisites: RIDER_001, THORN_001, GRAVE_001 (Known tier — not available?),
        // FAIR_001, ASHEN_001 are roots. GRAVE_001 requires Known tier but no REQUIRES_COMPLETION edge
        // pointing to it, so it appears as a root in graph terms.
        quests.Should().NotBeEmpty(because: "a fresh player should have root quests available");
        quests.Select(q => q.QuestId).Should().Contain("RIDER_001",
            because: "RIDER_001 is a root quest with no prerequisites");
        quests.Select(q => q.QuestId).Should().Contain("THORN_001",
            because: "THORN_001 is a root quest with no prerequisites");
        quests.Select(q => q.QuestId).Should().Contain("FAIR_001",
            because: "FAIR_001 is a root quest with no prerequisites");
        quests.Select(q => q.QuestId).Should().Contain("ASHEN_001",
            because: "ASHEN_001 is a root quest with no prerequisites");
    }

    [Fact]
    public async Task MarkQuestCompleted_ThenIsQuestAvailable_ReturnsFalse()
    {
        var repo = _fixture.CreateQuestGraphRepository();
        var playerId = Guid.NewGuid();

        await repo.MarkQuestCompletedAsync(playerId, "RIDER_001", "reported");
        var available = await repo.IsQuestAvailableAsync(playerId, "RIDER_001");

        available.Should().BeFalse(
            because: "a quest already completed by the player must not appear as available");
    }

    [Fact]
    public async Task GetUnlockedByCompletion_ReturnsExpectedNextQuest()
    {
        var repo = _fixture.CreateQuestGraphRepository();

        var unlocked = await repo.GetUnlockedByCompletionAsync("RIDER_001", "reported");

        unlocked.Should().ContainSingle(q => q.QuestId == "RIDER_002a",
            because: "completing RIDER_001 with outcome 'reported' unlocks RIDER_002a");
    }

    [Fact]
    public async Task GetUnlockedByCompletion_DifferentOutcome_ReturnsAlternateBranch()
    {
        var repo = _fixture.CreateQuestGraphRepository();

        var unlocked = await repo.GetUnlockedByCompletionAsync("RIDER_001", "concealed");

        unlocked.Should().ContainSingle(q => q.QuestId == "RIDER_002b",
            because: "completing RIDER_001 with outcome 'concealed' unlocks RIDER_002b");
    }

    [Fact]
    public async Task MarkQuestInProgress_ThenGetQuest_ShowsIsTakenTrue()
    {
        var repo = _fixture.CreateQuestGraphRepository();
        var playerId = Guid.NewGuid();

        await repo.MarkQuestInProgressAsync(playerId, "THORN_001", takenByAi: true);
        var quest = await repo.GetQuestAsync("THORN_001");

        quest.Should().NotBeNull();
        quest!.IsTaken.Should().BeTrue(
            because: "an AI player has marked THORN_001 as in-progress");
    }
}
