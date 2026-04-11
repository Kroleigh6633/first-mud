using FirstMud.Application.Models;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class QuestServiceTests
{
    private static Player CreatePlayer() => Player.Create("Tester", 42);

    private static QuestNode CreateQuestNode(
        string questId = "q_001",
        FactionId faction = FactionId.AshenCourt,
        int repReward = 100,
        bool isWyrdQuest = false)
        => new QuestNode(
            questId,
            "Test Quest",
            "A test quest",
            faction,
            ReputationTier.Known,
            WorldId.Aeldran,
            repReward,
            ["accept", "reject"],
            isWyrdQuest,
            false);

    // ---- available quests ----

    [Fact]
    public async Task GetAvailableQuestsAsync_ReturnsQuestsFromRepo_ForValidPlayer()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        var expectedQuests = new List<QuestNode> { CreateQuestNode() };

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.GetAvailableQuestsAsync(player.Id, null, Arg.Any<CancellationToken>())
            .Returns(expectedQuests.AsReadOnly());

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        var result = await svc.GetAvailableQuestsAsync(player.Id, null);

        result.Should().HaveCount(1);
        result[0].QuestId.Should().Be("q_001");
    }

    [Fact]
    public async Task GetAvailableQuestsAsync_PlayerNotFound_ThrowsInvalidOperationException()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        players.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Player?)null);

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        Func<Task> act = () => svc.GetAvailableQuestsAsync(Guid.NewGuid(), null);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    // ---- quest completion ----

    [Fact]
    public async Task CompleteQuestAsync_QuestNotAvailable_ReturnsFalseResult()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, "q_001", Arg.Any<CancellationToken>()).Returns(false);

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        var result = await svc.CompleteQuestAsync(player.Id, "q_001", "accept");

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not available");
    }

    [Fact]
    public async Task CompleteQuestAsync_QuestAvailable_InvokesMarkQuestCompletedAsync()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        var quest = CreateQuestNode();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, "q_001", Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetQuestAsync("q_001", Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.GetUnlockedByCompletionAsync("q_001", "accept", Arg.Any<CancellationToken>())
            .Returns(new List<QuestNode>().AsReadOnly());

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        await svc.CompleteQuestAsync(player.Id, "q_001", "accept");

        await questGraph.Received(1).MarkQuestCompletedAsync(player.Id, "q_001", "accept", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompleteQuestAsync_QuestAvailable_AwardsReputationToPlayer()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        var quest = CreateQuestNode(faction: FactionId.AshenCourt, repReward: 150);

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, "q_001", Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetQuestAsync("q_001", Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.GetUnlockedByCompletionAsync("q_001", "accept", Arg.Any<CancellationToken>())
            .Returns(new List<QuestNode>().AsReadOnly());

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        var result = await svc.CompleteQuestAsync(player.Id, "q_001", "accept");

        result.ReputationGained.Should().Be(150);
        // The player's AshenCourt reputation should have increased
        var rep = player.Reputations.First(r => r.FactionId == FactionId.AshenCourt);
        rep.Score.Points.Should().Be(150);
    }

    [Fact]
    public async Task CompleteQuestAsync_WyrdQuest_SetsWyrdSettledTrue_AndReducesWyrdTangle()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        player.AccumulateWyrdTangle(30); // give player some wyrd tangle first

        var quest = CreateQuestNode(isWyrdQuest: true);

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, "q_001", Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetQuestAsync("q_001", Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.GetUnlockedByCompletionAsync("q_001", "accept", Arg.Any<CancellationToken>())
            .Returns(new List<QuestNode>().AsReadOnly());

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        var result = await svc.CompleteQuestAsync(player.Id, "q_001", "accept");

        result.WyrdSettled.Should().BeTrue();
        // WyrdTangle should have been reduced by 10 (as per service logic)
        player.WyrdTangle.Should().Be(20); // 30 - 10
    }

    [Fact]
    public async Task CompleteQuestAsync_OutcomePassedThrough_ReturnsUnlockedQuests()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        var quest = CreateQuestNode();
        var unlocked = new List<QuestNode> { CreateQuestNode("q_002") };

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, "q_001", Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetQuestAsync("q_001", Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.GetUnlockedByCompletionAsync("q_001", "reject", Arg.Any<CancellationToken>())
            .Returns(unlocked.AsReadOnly());

        var reputationSvc = new ReputationService(players);
        var svc = new QuestService(questGraph, reputationSvc, players);

        var result = await svc.CompleteQuestAsync(player.Id, "q_001", "reject");

        result.UnlockedQuests.Should().HaveCount(1);
        result.UnlockedQuests[0].QuestId.Should().Be("q_002");
        // Verify the chosen outcome was passed to the unlocked quests query
        await questGraph.Received(1).GetUnlockedByCompletionAsync("q_001", "reject", Arg.Any<CancellationToken>());
    }
}
