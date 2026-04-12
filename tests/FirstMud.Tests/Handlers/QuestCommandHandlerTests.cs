using FirstMud.Application.Models;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Handlers;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FirstMud.Tests.Handlers;

public class QuestCommandHandlerTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static Player CreatePlayer() => Player.Create("TestRider", 42);

    private static QuestNode CreateQuestNode(
        string questId = "q_test",
        string title = "Investigate the Ruins",
        string description = "Explore the ancient ruins.",
        FactionId faction = FactionId.AshenCourt,
        int repReward = 100,
        bool isWyrdQuest = false,
        string[]? outcomes = null)
        => new QuestNode(
            questId,
            title,
            description,
            faction,
            ReputationTier.Known,
            WorldId.Aeldran,
            repReward,
            outcomes ?? ["accept", "reject"],
            isWyrdQuest,
            false);

    /// <summary>
    /// Creates an IHubContext mock that accepts any .Clients.Group(...).SendAsync(...) calls.
    /// </summary>
    private static IHubContext<GameHub> CreateHubContext()
    {
        var hub = Substitute.For<IHubContext<GameHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(clientProxy);
        return hub;
    }

    private static (QuestService questService, IQuestGraphRepository questGraph, IPlayerRepository players)
        CreateQuestService()
    {
        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();
        var reputationSvc = new ReputationService(players);
        var questService = new QuestService(questGraph, reputationSvc, players);
        return (questService, questGraph, players);
    }

    // =========================================================================
    // AcceptQuestCommandHandler
    // =========================================================================

    [Fact]
    public async Task AcceptQuest_MarksQuestInProgress()
    {
        // Arrange
        var player = CreatePlayer();
        var quest = CreateQuestNode();

        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, quest.QuestId, Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetQuestAsync(quest.QuestId, Arg.Any<CancellationToken>()).Returns(quest);

        var handler = new AcceptQuestCommandHandler(players, questGraph, hub);

        // Act
        var result = await handler.HandleAsync(new AcceptQuestCommand(player.Id, quest.QuestId), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        await questGraph.Received(1).MarkQuestInProgressAsync(
            player.Id,
            quest.QuestId,
            takenByAi: false,
            ct: Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AcceptQuest_UnavailableQuest_ReturnsFalse()
    {
        // Arrange
        var player = CreatePlayer();

        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, "q_locked", Arg.Any<CancellationToken>()).Returns(false);

        var handler = new AcceptQuestCommandHandler(players, questGraph, hub);

        // Act
        var result = await handler.HandleAsync(new AcceptQuestCommand(player.Id, "q_locked"), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not available");
    }

    [Fact]
    public async Task AcceptQuest_AlreadyTaken_ReturnsFalse()
    {
        // A quest that is already in progress is no longer "available" from the
        // repository's perspective (IsQuestAvailableAsync returns false once the
        // player has the IN_PROGRESS relationship).
        var player = CreatePlayer();
        var questId = "q_active";

        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        // Quest already taken → not available to accept again
        questGraph.IsQuestAvailableAsync(player.Id, questId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new AcceptQuestCommandHandler(players, questGraph, hub);

        // Act
        var result = await handler.HandleAsync(new AcceptQuestCommand(player.Id, questId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        await questGraph.DidNotReceive().MarkQuestInProgressAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<bool>(),
            Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // InteractQuestCommandHandler
    // =========================================================================

    [Fact]
    public async Task InteractQuest_ExploreType_AutoCompletes()
    {
        // An "Investigate" quest is an explore type — arriving is sufficient.
        var player = CreatePlayer();
        var quest = CreateQuestNode(
            questId: "q_explore",
            title: "Investigate the Hollow",
            description: "Find out what lurks in the hollow.",
            repReward: 80);

        var (questService, questGraph, players) = CreateQuestService();
        var itemRepo = Substitute.For<IItemRepository>();
        var progressTracker = new QuestProgressTracker();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.GetQuestAsync(quest.QuestId, Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.IsQuestInProgressAsync(player.Id, quest.QuestId, Arg.Any<CancellationToken>()).Returns(true);
        // Quest is available for completion check inside QuestService
        questGraph.IsQuestAvailableAsync(player.Id, quest.QuestId, Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetUnlockedByCompletionAsync(quest.QuestId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<QuestNode>().AsReadOnly());

        var handler = new InteractQuestCommandHandler(
            players, itemRepo, questService, questGraph, progressTracker, hub,
            NullLogger<InteractQuestCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(new InteractQuestCommand(player.Id, quest.QuestId), CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue("explore quests auto-complete on arrival");
        await questGraph.Received(1).MarkQuestCompletedAsync(
            player.Id, quest.QuestId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task InteractQuest_NotAccepted_ReturnsError()
    {
        // Quest has NOT been accepted by the player.
        var player = CreatePlayer();
        var quest = CreateQuestNode(questId: "q_unaccepted", title: "Investigate Something");

        var (questService, questGraph, players) = CreateQuestService();
        var itemRepo = Substitute.For<IItemRepository>();
        var progressTracker = new QuestProgressTracker();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.GetQuestAsync(quest.QuestId, Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.IsQuestInProgressAsync(player.Id, quest.QuestId, Arg.Any<CancellationToken>()).Returns(false);

        var handler = new InteractQuestCommandHandler(
            players, itemRepo, questService, questGraph, progressTracker, hub,
            NullLogger<InteractQuestCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(new InteractQuestCommand(player.Id, quest.QuestId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("not accepted");
    }

    [Fact]
    public async Task InteractQuest_KillQuest_NeedsKills()
    {
        // A "Defeat" quest needs the required kill count before completion.
        var player = CreatePlayer();
        // Description says "defeat 5 bandits" → ParseKillCount returns 5
        var quest = CreateQuestNode(
            questId: "q_kill",
            title: "Defeat the Bandits",
            description: "Defeat 5 bandits plaguing the road.");

        var (questService, questGraph, players) = CreateQuestService();
        var itemRepo = Substitute.For<IItemRepository>();
        var progressTracker = new QuestProgressTracker();
        // Player has only 2 kills — not enough
        progressTracker.RecordKill(player.Id, quest.QuestId, 2);

        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.GetQuestAsync(quest.QuestId, Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.IsQuestInProgressAsync(player.Id, quest.QuestId, Arg.Any<CancellationToken>()).Returns(true);

        var handler = new InteractQuestCommandHandler(
            players, itemRepo, questService, questGraph, progressTracker, hub,
            NullLogger<InteractQuestCommandHandler>.Instance);

        // Act
        var result = await handler.HandleAsync(new InteractQuestCommand(player.Id, quest.QuestId), CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Message.Should().Contain("defeat");
        result.Message.Should().MatchRegex(@"\d+\s+more\s+enemies", "should tell player how many kills remain");
    }

    // =========================================================================
    // CompleteQuestCommandHandler
    // =========================================================================

    [Fact]
    public async Task CompleteQuest_AwardsReputationAndXP()
    {
        var player = CreatePlayer();
        var quest = CreateQuestNode(repReward: 120, faction: FactionId.AshenCourt);

        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        questGraph.IsQuestAvailableAsync(player.Id, quest.QuestId, Arg.Any<CancellationToken>()).Returns(true);
        questGraph.GetQuestAsync(quest.QuestId, Arg.Any<CancellationToken>()).Returns(quest);
        questGraph.GetUnlockedByCompletionAsync(quest.QuestId, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new List<QuestNode>().AsReadOnly());

        var reputationSvc = new ReputationService(players);
        var questService = new QuestService(questGraph, reputationSvc, players);
        var handler = new CompleteQuestCommandHandler(players, questService, hub);

        // Act
        var result = await handler.HandleAsync(
            new CompleteQuestCommand(player.Id, quest.QuestId, "accept"),
            CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        // Reputation should have been awarded to the faction
        var rep = player.Reputations.FirstOrDefault(r => r.FactionId == FactionId.AshenCourt);
        rep.Should().NotBeNull();
        rep!.Score.Points.Should().Be(120, "reputation reward of 120 should be applied");

        // XP = reputationReward * 2 = 240; player.GainExperience was called
        player.Experience.Should().BeGreaterThan(0, "quest XP should be awarded to the player");

        // Player record should be persisted
        await players.Received().UpdateAsync(player, Arg.Any<CancellationToken>());
    }

    // =========================================================================
    // Bulk accept (AcceptAll scenario)
    // =========================================================================

    [Fact]
    public async Task AcceptAll_MultipleBulkAccepts_AllMarkedInProgress()
    {
        // Simulate accepting 5 quests rapidly (sequential, as the hub handler does).
        var player = CreatePlayer();

        var questIds = Enumerable.Range(1, 5).Select(i => $"q_bulk_{i:D2}").ToArray();
        var questNodes = questIds
            .Select(id => CreateQuestNode(questId: id, title: $"Investigate Zone {id}"))
            .ToArray();

        var questGraph = Substitute.For<IQuestGraphRepository>();
        var players = Substitute.For<IPlayerRepository>();
        var hub = CreateHubContext();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        foreach (var (id, node) in questIds.Zip(questNodes))
        {
            questGraph.IsQuestAvailableAsync(player.Id, id, Arg.Any<CancellationToken>()).Returns(true);
            questGraph.GetQuestAsync(id, Arg.Any<CancellationToken>()).Returns(node);
        }

        var handler = new AcceptQuestCommandHandler(players, questGraph, hub);

        // Act — accept all 5 in sequence
        var results = new List<CommandResult>();
        foreach (var id in questIds)
        {
            var r = await handler.HandleAsync(new AcceptQuestCommand(player.Id, id), CancellationToken.None);
            results.Add(r);
        }

        // Assert
        results.Should().AllSatisfy(r => r.Success.Should().BeTrue());

        foreach (var id in questIds)
        {
            await questGraph.Received(1).MarkQuestInProgressAsync(
                player.Id, id, takenByAi: false, ct: Arg.Any<CancellationToken>());
        }
    }
}
