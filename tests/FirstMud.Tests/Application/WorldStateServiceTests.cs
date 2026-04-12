using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Services;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class WorldStateServiceTests
{
    private static QuestNode MakeQuest(string id, FactionId faction, bool isTaken = false) => new(
        QuestId: id,
        Title: $"Quest {id}",
        Description: "A test quest.",
        FactionId: faction,
        RequiredTier: ReputationTier.Unknown,
        RequiredWorld: WorldId.Aeldran,
        ReputationReward: 50,
        PossibleOutcomes: ["succeed", "fail"],
        IsWyrdQuest: false,
        IsTaken: isTaken);

    private static WorldStateService BuildService(IQuestGraphRepository questRepo)
    {
        var playerRepo      = Substitute.For<IPlayerRepository>();
        var itemRepo        = Substitute.For<IItemRepository>();
        var companionRepo   = Substitute.For<ICompanionRepository>();
        // AiPlayerService is not called by GetAvailableQuestsAsync; pass null safely
        return new WorldStateService(playerRepo, questRepo, null!, itemRepo, companionRepo);
    }

    [Fact]
    public async Task GetAvailableQuestsAsync_ReturnsQuestsFromRepository()
    {
        var playerId = Guid.NewGuid();
        var questRepo = Substitute.For<IQuestGraphRepository>();
        var expected = new List<QuestNode>
        {
            MakeQuest("q1", FactionId.HouseCaervorn),
            MakeQuest("q2", FactionId.Gravenguard),
        };
        questRepo.GetAvailableQuestsAsync(playerId, null, Arg.Any<CancellationToken>())
            .Returns(expected);

        var svc = BuildService(questRepo);
        var result = await svc.GetAvailableQuestsAsync(playerId);

        result.Should().HaveCount(2);
        result.Select(q => q.QuestId).Should().BeEquivalentTo(["q1", "q2"]);
    }

    [Fact]
    public async Task GetAvailableQuestsAsync_ReturnsEmptyList_WhenNoQuestsAvailable()
    {
        var playerId = Guid.NewGuid();
        var questRepo = Substitute.For<IQuestGraphRepository>();
        questRepo.GetAvailableQuestsAsync(playerId, null, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<QuestNode>());

        var svc = BuildService(questRepo);
        var result = await svc.GetAvailableQuestsAsync(playerId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAvailableQuestsAsync_PassesPlayerIdToRepository()
    {
        var playerId = Guid.NewGuid();
        var questRepo = Substitute.For<IQuestGraphRepository>();
        questRepo.GetAvailableQuestsAsync(default, null, default)
            .ReturnsForAnyArgs(Array.Empty<QuestNode>());

        var svc = BuildService(questRepo);
        await svc.GetAvailableQuestsAsync(playerId);

        await questRepo.Received(1)
            .GetAvailableQuestsAsync(playerId, null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetAvailableQuestsAsync_IncludesInProgressQuests()
    {
        var playerId = Guid.NewGuid();
        var questRepo = Substitute.For<IQuestGraphRepository>();
        var quests = new List<QuestNode>
        {
            MakeQuest("q-available", FactionId.EmeraldCompact, isTaken: false),
            MakeQuest("q-inprogress", FactionId.ThornwoodCovens, isTaken: true),
        };
        questRepo.GetAvailableQuestsAsync(playerId, null, Arg.Any<CancellationToken>())
            .Returns(quests);

        var svc = BuildService(questRepo);
        var result = await svc.GetAvailableQuestsAsync(playerId);

        result.Should().HaveCount(2);
        result.Single(q => q.IsTaken).QuestId.Should().Be("q-inprogress");
    }

    [Fact]
    public async Task GetAvailableQuestsAsync_ReturnsReadOnlyList()
    {
        var playerId = Guid.NewGuid();
        var questRepo = Substitute.For<IQuestGraphRepository>();
        questRepo.GetAvailableQuestsAsync(playerId, null, Arg.Any<CancellationToken>())
            .Returns([MakeQuest("q1", FactionId.AshenCourt)]);

        var svc = BuildService(questRepo);
        var result = await svc.GetAvailableQuestsAsync(playerId);

        result.Should().BeAssignableTo<IReadOnlyList<QuestNode>>();
    }
}
