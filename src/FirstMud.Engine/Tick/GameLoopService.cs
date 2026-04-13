using System.Collections.Concurrent;
using FirstMud.Engine.Commands;
using FirstMud.Engine.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FirstMud.Engine.Tick;

/// <summary>
/// Domain-agnostic tick loop. Drives two things:
///   1. A bounded per-tick command queue, dispatched via <see cref="CommandDispatcher"/>.
///   2. A set of <see cref="ITickHandler"/> instances, each invoked on its own cadence.
///
/// The engine does not know about companions, weave, crafting, etc. Game code
/// registers tick handlers and command handlers; the loop simply drives them.
/// </summary>
public class GameLoopService : BackgroundService
{
    public const int TickIntervalMs = 100; // 10 ticks per second
    private const int MaxCommandsPerTick = 50;

    private readonly ConcurrentQueue<IGameCommand> _commandQueue = new();
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<GameLoopService> _logger;

    private long _tickCount;

    public GameLoopService(IServiceScopeFactory scopeFactory, ILogger<GameLoopService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void EnqueueCommand(IGameCommand command) => _commandQueue.Enqueue(command);

    public long CurrentTick => _tickCount;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("GameLoopService starting. Tick rate: {TickRate}ms.", TickIntervalMs);

        // Snapshot registered tick handlers once at startup.
        ITickHandler[] handlers;
        using (var scope = _scopeFactory.CreateScope())
        {
            handlers = scope.ServiceProvider.GetServices<ITickHandler>().ToArray();
            _logger.LogInformation("Registered tick handlers: {Count}", handlers.Length);
            foreach (var h in handlers)
                _logger.LogInformation("  • {Name} (every {Interval})", h.Name, h.Interval);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            var tickStart = DateTimeOffset.UtcNow;

            try
            {
                await ProcessCommandsAsync(stoppingToken);
                await ProcessTickHandlersAsync(handlers, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Unhandled exception in game loop tick {TickCount}.", _tickCount);
            }

            _tickCount++;

            var elapsed = (DateTimeOffset.UtcNow - tickStart).TotalMilliseconds;
            var remaining = TickIntervalMs - elapsed;
            if (remaining > 0)
                await Task.Delay((int)remaining, stoppingToken);
        }

        _logger.LogInformation("GameLoopService stopped after {TickCount} ticks.", _tickCount);
    }

    private async Task ProcessCommandsAsync(CancellationToken ct)
    {
        var processed = 0;
        while (processed < MaxCommandsPerTick && _commandQueue.TryDequeue(out var command))
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dispatcher = scope.ServiceProvider.GetRequiredService<CommandDispatcher>();

            var result = await dispatcher.DispatchAsync(command, ct);

            if (!result.Success)
            {
                _logger.LogWarning("Command {CommandType} for player {PlayerId} failed: {Message}",
                    command.GetType().Name, command.PlayerId, result.Message);

                // Best-effort notify the player via the injected notifier abstraction.
                try
                {
                    var notifier = scope.ServiceProvider.GetService<IGameNotifier>();
                    if (notifier is not null)
                        await notifier.SendMessageAsync(command.PlayerId, "error", result.Message, ct);
                }
                catch (Exception broadcastEx) when (broadcastEx is not OperationCanceledException)
                {
                    _logger.LogError(broadcastEx, "Failed to broadcast command error to player {PlayerId}.", command.PlayerId);
                }
            }

            processed++;
        }
    }

    private async Task ProcessTickHandlersAsync(ITickHandler[] handlers, CancellationToken ct)
    {
        foreach (var handler in handlers)
        {
            var intervalMs = handler.Interval.TotalMilliseconds;
            if (intervalMs <= 0) continue;

            var ticksPerCycle = (long)(intervalMs / TickIntervalMs);
            if (ticksPerCycle <= 0) ticksPerCycle = 1;

            if (_tickCount % ticksPerCycle != 0) continue;
            if (_tickCount == 0 && !handler.RunOnFirstTick) continue;

            try
            {
                await handler.HandleTickAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Tick handler {Name} threw at tick {Tick}.", handler.Name, _tickCount);
            }
        }
    }
}
