using System.Collections.Concurrent;
using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using Microsoft.AspNetCore.SignalR;
using FirstMud.GameServer.Hubs;

namespace FirstMud.GameServer.Services;

public class GameLoopService : BackgroundService
{
    private const int TickIntervalMs = 100;                  // 10 ticks per second
    private const int AiTickIntervalSeconds = 60;            // AI player tick every 60 seconds
    private const int AutomationTickIntervalSeconds = 300;   // Automation upkeep every 300 seconds
    private const int HomesteadHealIntervalSeconds = 1;      // Homestead healing: 10 HP per second
    private const int ResourceRegenIntervalSeconds = 60;     // Resource node regen every 60 seconds
    private const int WeaveRegenIntervalSeconds = 10;        // Out-of-combat Weave regen: 1 point every 10 seconds
    private const int HomesteadCompanionIntervalSeconds = 60; // Homestead companion duties every 60 seconds
    private const int CompanionDriftIntervalSeconds = 60;    // Companion drift accumulation every 60 seconds
    private const int BuildingConstructionIntervalSeconds = 60; // Building construction progress every 60 seconds

    private readonly ConcurrentQueue<IGameCommand> _commandQueue = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AiPlayerService _aiPlayerService;
    private readonly ILogger<GameLoopService> _logger;

    private long _tickCount;

    public GameLoopService(
        IServiceScopeFactory scopeFactory,
        AiPlayerService aiPlayerService,
        ILogger<GameLoopService> logger)
    {
        _scopeFactory = scopeFactory;
        _aiPlayerService = aiPlayerService;
        _logger = logger;
    }

    public void EnqueueCommand(IGameCommand command) => _commandQueue.Enqueue(command);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GameLoopService starting. Tick rate: {TickRate}ms.", TickIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTimeOffset.UtcNow;

            try
            {
                await ProcessTickAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled exception in game loop tick {TickCount}.", _tickCount);
            }

            _tickCount++;

            // Throttle: wait remainder of tick interval
            var elapsed = (DateTimeOffset.UtcNow - tickStart).TotalMilliseconds;
            var remaining = TickIntervalMs - elapsed;
            if (remaining > 0)
                await Task.Delay((int)remaining, stoppingToken);
        }

        _logger.LogInformation("GameLoopService stopped after {TickCount} ticks.", _tickCount);
    }

    private async Task ProcessTickAsync(CancellationToken ct)
    {
        // 1. Process all queued commands
        await ProcessCommandsAsync(ct);

        // 2. Every 60 seconds: AI player tick
        var ticksPerAiCycle = (long)(AiTickIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerAiCycle == 0)
            await ProcessAiTickAsync(ct);

        // 3. Every 300 seconds: automation upkeep tick
        var ticksPerAutomationCycle = (long)(AutomationTickIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerAutomationCycle == 0 && _tickCount > 0)
            await ProcessAutomationTickAsync(ct);

        // 4. Every 1 second: heal players at homestead (10 HP/s)
        var ticksPerHealCycle = (long)(HomesteadHealIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerHealCycle == 0 && _tickCount > 0)
            await ProcessHomesteadHealAsync(ct);

        // 5. Every 60 seconds: regenerate resource nodes
        var ticksPerRegenCycle = (long)(ResourceRegenIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerRegenCycle == 0 && _tickCount > 0)
            await ProcessResourceRegenAsync(ct);

        // 6. Every 10 seconds: regenerate Weave for players out of combat
        var ticksPerWeaveRegen = (long)(WeaveRegenIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerWeaveRegen == 0 && _tickCount > 0)
            await ProcessWeaveRegenAsync(ct);

        // 7. Every 60 seconds: process homestead companion duties
        var ticksPerHomesteadCompanion = (long)(HomesteadCompanionIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerHomesteadCompanion == 0 && _tickCount > 0)
            await ProcessHomesteadCompanionsAsync(ct);

        // 8. Every 60 seconds: accumulate drift for inactive companions not on homestead duty
        var ticksPerDrift = (long)(CompanionDriftIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerDrift == 0 && _tickCount > 0)
            await ProcessCompanionDriftAsync(ct);

        // 9. Every 60 seconds: advance building construction progress
        var ticksPerConstruction = (long)(BuildingConstructionIntervalSeconds * 1000.0 / TickIntervalMs);
        if (_tickCount % ticksPerConstruction == 0 && _tickCount > 0)
            await ProcessBuildingConstructionAsync(ct);
    }

    private async Task ProcessCommandsAsync(CancellationToken ct)
    {
        // Drain up to a bounded batch per tick to avoid starving the loop
        const int maxCommandsPerTick = 50;
        var processed = 0;

        while (processed < maxCommandsPerTick && _commandQueue.TryDequeue(out var command))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<CommandDispatcher>();

            var result = await dispatcher.DispatchAsync(command, ct);

            if (!result.Success)
            {
                _logger.LogWarning("Command {CommandType} for player {PlayerId} failed: {Message}",
                    command.GetType().Name, command.PlayerId, result.Message);

                // Broadcast the failure reason to the player so it's not a silent no-op.
                try
                {
                    var notificationService = scope.ServiceProvider.GetRequiredService<GameNotificationService>();
                    await notificationService.SendMessageAsync(command.PlayerId, "error", result.Message, ct);
                }
                catch (Exception broadcastEx) when (broadcastEx is not OperationCanceledException)
                {
                    _logger.LogError(broadcastEx, "Failed to broadcast command error to player {PlayerId}.", command.PlayerId);
                }
            }

            processed++;
        }
    }

    private async Task ProcessAiTickAsync(CancellationToken ct)
    {
        _logger.LogDebug("Running AI player tick at game tick {TickCount}.", _tickCount);
        await _aiPlayerService.TickAsync(ct);
    }

    private Task ProcessAutomationTickAsync(CancellationToken ct)
    {
        _logger.LogDebug("Running automation upkeep tick at game tick {TickCount}.", _tickCount);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Heals players who are at the homestead (position -100,-100) by 10 HP per second
    /// and restores 5 Weave per second while at homestead.
    /// Broadcasts a WorldState update so the status panel reflects the change.
    /// </summary>
    private async Task ProcessHomesteadHealAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                // Homestead is at the sentinel coordinate -100,-100
                if (player.Position.X != -100 || player.Position.Y != -100) continue;

                var changed = false;
                var hpBefore = player.CurrentHp;

                if (player.CurrentHp < player.MaxHp)
                {
                    player.HealHp(10);
                    changed = true;
                }

                // Homestead also restores 5 Weave per second
                if (player.Weave.Current < player.Weave.Maximum)
                {
                    player.RestoreWeave(5);
                    changed = true;
                }

                if (!changed) continue;

                _logger.LogInformation(
                    "Homestead heal tick: player {Name} at ({X},{Y}), HP {Before} → {After}",
                    player.Name, player.Position.X, player.Position.Y, hpBefore, player.CurrentHp);

                await playerRepo.UpdateAsync(player, ct);

                // Notify the player of the heal
                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "system",
                        text = $"Homestead sanctuary heals you. HP: {player.CurrentHp}/{player.MaxHp} | Weave: {player.Weave.Current}/{player.Weave.Maximum}."
                    }, ct);

                // Push an updated HP reading via a lightweight event
                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("PlayerHealed", new
                    {
                        player.Id,
                        player.CurrentHp,
                        player.MaxHp,
                        WeavePercent = player.Weave.Percentage,
                        WeaveState = player.Weave.VisibleState.ToString()
                    }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in homestead heal tick.");
        }
    }

    /// <summary>
    /// Restores 1 Weave per 10-second tick for players who are out of combat (not at homestead).
    /// Homestead gives 5 per second via ProcessHomesteadHealAsync.
    /// </summary>
    private async Task ProcessWeaveRegenAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                // Skip players at homestead (handled by homestead tick at a faster rate)
                if (player.Position.X == -100 && player.Position.Y == -100) continue;
                if (player.Weave.Current >= player.Weave.Maximum) continue;

                player.RestoreWeave(1);
                await playerRepo.UpdateAsync(player, ct);

                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("PlayerHealed", new
                    {
                        player.Id,
                        player.CurrentHp,
                        player.MaxHp,
                        WeavePercent = player.Weave.Percentage,
                        WeaveState = player.Weave.VisibleState.ToString()
                    }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in Weave regen tick.");
        }
    }

    /// <summary>
    /// Processes all homestead companion duties (Harvester, Salvager, Guard, Crafter).
    /// Broadcasts a message to each player whose companion did something.
    /// </summary>
    private async Task ProcessHomesteadCompanionsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var service = scope.ServiceProvider.GetRequiredService<HomesteadCompanionService>();
            var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            var results = await service.ProcessAllDutiesAsync(ct);

            foreach (var result in results)
            {
                await hubContext.Clients
                    .Group(result.PlayerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "system",
                        text = result.Message
                    }, ct);
            }

            if (results.Count > 0)
                _logger.LogDebug("Homestead companion tick complete. {Count} duty action(s) processed.", results.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in homestead companion tick.");
        }
    }

    /// <summary>
    /// Accumulates drift for companions that are not active and not on homestead duty.
    /// Called every 60 seconds; passes 1/60th of an hour (≈ 0.0167) so drift is in hours.
    /// Active companions accumulate drift at 10% of the passive rate (handled inside AccumulateDrift).
    /// If drift reaches 50 the companion drops a layer — this is a soft penalty for neglect.
    /// </summary>
    private async Task ProcessCompanionDriftAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var companionRepo = scope.ServiceProvider.GetRequiredService<FirstMud.Domain.Interfaces.ICompanionRepository>();
            var hubContext    = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            // Fetch all players so we know which companions are idle (not active, not on duty)
            var playerRepo = scope.ServiceProvider.GetRequiredService<FirstMud.Domain.Interfaces.IPlayerRepository>();
            var players    = await playerRepo.GetActivePlayersAsync(ct);

            foreach (var player in players)
            {
                var companions = await companionRepo.GetByOwnerAsync(player.Id, ct);
                foreach (var companion in companions)
                {
                    if (companion.IsPermanentlyGone) continue;
                    // Skip companions on homestead duty — they are productively occupied
                    if (companion.AssignedDuty.HasValue && companion.AssignedDuty != FirstMud.Domain.Enums.HomesteadDuty.None) continue;

                    var layerBefore = companion.CurrentLayer;
                    // 1 tick = 60 seconds = 1/60 of an hour
                    companion.AccumulateDrift(1f / 60f);
                    await companionRepo.UpdateAsync(companion, ct);

                    // Notify player if the companion drifted down a layer
                    if (companion.CurrentLayer < layerBefore)
                    {
                        await hubContext.Clients
                            .Group(player.Id.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category  = "system",
                                text      = $"{companion.Name} has drifted to Layer {companion.CurrentLayer} due to inactivity. Bring them on an adventure!"
                            }, ct);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in companion drift tick.");
        }
    }

    /// <summary>
    /// Advances construction progress for all buildings that have a builder assigned.
    /// Each builder companion contributes 5% progress per tick (60-second cycle).
    /// Broadcasts a notification to the owning player when a building completes.
    /// </summary>
    private async Task ProcessBuildingConstructionAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var buildingService = scope.ServiceProvider.GetRequiredService<FirstMud.Application.Services.BuildingService>();
            var hubContext      = scope.ServiceProvider.GetRequiredService<IHubContext<GameHub>>();

            // Populate the homestead→player cache so notifications can be targeted
            var playerRepo = scope.ServiceProvider.GetRequiredService<FirstMud.Domain.Interfaces.IPlayerRepository>();
            var homesteadRepo = scope.ServiceProvider.GetRequiredService<FirstMud.Domain.Interfaces.IHomesteadRepository>();
            var players = await playerRepo.GetActivePlayersAsync(ct);
            foreach (var player in players)
            {
                var homestead = await homesteadRepo.GetByPlayerIdAsync(player.Id, ct);
                if (homestead is not null)
                    buildingService.RegisterHomesteadOwner(homestead.Id, player.Id);
            }

            var results = await buildingService.TickConstructionAsync(ct);

            foreach (var result in results)
            {
                if (result.PlayerId == Guid.Empty) continue;

                var msg = result.AutoAssignedCompanionName is not null
                    ? $"{result.BuildingType} construction complete! {result.AutoAssignedCompanionName} has been assigned to work there."
                    : $"{result.BuildingType} construction complete! Visit your homestead [P] and open the city panel [G] to assign a worker.";

                await hubContext.Clients
                    .Group(result.PlayerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category  = "system",
                        text      = msg,
                    }, ct);
            }

            if (results.Count > 0)
                _logger.LogDebug("Building construction tick complete. {Count} building(s) finished.", results.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in building construction tick.");
        }
    }

    /// <summary>
    /// Adds RegenerationRate to each resource node's RemainingYield, capped at MaxYield.
    /// </summary>
    private async Task ProcessResourceRegenAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var nodeRepo = scope.ServiceProvider.GetRequiredService<IResourceNodeRepository>();

            var nodes = await nodeRepo.GetAllAsync(ct);
            foreach (var node in nodes)
            {
                if (node.RemainingYield >= node.MaxYield) continue;
                node.Regenerate();
                await nodeRepo.UpdateAsync(node, ct);
            }

            _logger.LogDebug("Resource node regen tick complete. Nodes updated: {Count}",
                nodes.Count(n => n.RemainingYield < n.MaxYield));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in resource node regen tick.");
        }
    }
}
