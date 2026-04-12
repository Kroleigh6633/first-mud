using System.Collections.Concurrent;
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
                _logger.LogWarning("Command {CommandType} for player {PlayerId} failed: {Message}",
                    command.GetType().Name, command.PlayerId, result.Message);

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
    /// Heals players who are at the homestead (position -100,-100) by 10 HP per second.
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
                if (player.CurrentHp >= player.MaxHp) continue;

                player.HealHp(10);
                await playerRepo.UpdateAsync(player, ct);

                // Notify the player of the heal
                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "system",
                        text = $"Homestead sanctuary heals you. HP: {player.CurrentHp}/{player.MaxHp}."
                    }, ct);

                // Push an updated HP reading via a lightweight event
                await hubContext.Clients
                    .Group(player.Id.ToString())
                    .SendAsync("PlayerHealed", new
                    {
                        player.Id,
                        player.CurrentHp,
                        player.MaxHp
                    }, ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error in homestead heal tick.");
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
