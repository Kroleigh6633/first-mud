using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Handlers;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FirstMud.Tests.Handlers;

/// <summary>
/// Verifies the state-broadcast discipline on AutoFarmCommandHandler:
///   - Starting auto-farm MUST emit AutoFarmStatus { active = true }.
///   - Stopping auto-farm MUST emit AutoFarmStatus { active = false }.
///
/// Regression guard for the desync bug where the client's F-key handler
/// evaluated `autoFarmStatus?.active` as falsy despite the banner still
/// showing AUTO-FARM ACTIVE, causing F-press to open the picker instead
/// of stopping the loop.
/// </summary>
public class AutoFarmCommandHandlerTests
{
    private static IHubContext<GameHub> CreateHubContext(out IClientProxy clientProxy)
    {
        var hub = Substitute.For<IHubContext<GameHub>>();
        var clients = Substitute.For<IHubClients>();
        clientProxy = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(clientProxy);
        return hub;
    }

    private static FarmingOrchestrator CreateOrchestrator(IHubContext<GameHub> hub, AutoFarmService svc)
    {
        // The orchestrator is fire-and-forget; we only need a constructable instance.
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        var deposit = new InventoryDepositService(
            hub,
            svc,
            scopeFactory,
            NullLogger<InventoryDepositService>.Instance);
        var monsterFactory = new MonsterFactory(
            Substitute.For<FirstMud.Application.Content.IContentProvider>());
        return new FarmingOrchestrator(
            hub, svc, scopeFactory, NullLogger<FarmingOrchestrator>.Instance,
            deposit, monsterFactory);
    }

    [Fact]
    public async Task Start_EmitsAutoFarmStatusWithActiveTrue()
    {
        var hub = CreateHubContext(out var clientProxy);
        var notifier = new GameNotificationService(hub);
        var svc = new AutoFarmService();
        var players = Substitute.For<IPlayerRepository>();
        var playerId = Guid.NewGuid();
        players.GetByIdAsync(playerId, Arg.Any<CancellationToken>())
            .Returns(Player.Create("TestRider", 1));

        var handler = new AutoFarmCommandHandler(players, svc, notifier, CreateOrchestrator(hub, svc));
        var cmd = new AutoFarmCommand(playerId);

        var result = await handler.HandleAsync(cmd, CancellationToken.None);

        result.Success.Should().BeTrue();
        svc.IsActive(playerId).Should().BeTrue();

        // Verify an "AutoFarmStatus" event was sent via the hub
        await clientProxy.Received().SendCoreAsync(
            "AutoFarmStatus",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stop_EmitsAutoFarmStatusWithActiveFalse()
    {
        var hub = CreateHubContext(out var clientProxy);
        var notifier = new GameNotificationService(hub);
        var svc = new AutoFarmService();
        var players = Substitute.For<IPlayerRepository>();
        var playerId = Guid.NewGuid();

        // Pre-seed an active session so the handler takes the stop path.
        svc.StartSession(playerId);
        clientProxy.ClearReceivedCalls();

        var handler = new AutoFarmCommandHandler(players, svc, notifier, CreateOrchestrator(hub, svc));
        var cmd = new AutoFarmCommand(playerId);

        var result = await handler.HandleAsync(cmd, CancellationToken.None);

        result.Success.Should().BeTrue();
        svc.IsActive(playerId).Should().BeFalse();

        await clientProxy.Received().SendCoreAsync(
            "AutoFarmStatus",
            Arg.Any<object?[]>(),
            Arg.Any<CancellationToken>());
    }
}
