using System.Collections.Concurrent;
using FirstMud.GameServer.Commands;

namespace FirstMud.GameServer.Services;

public class GameLoopService : BackgroundService
{
    private const int TickIntervalMs = 100;              // 10 ticks per second
    private const int AiTickIntervalSeconds = 60;        // AI player tick every 60 seconds
    private const int AutomationTickIntervalSeconds = 300; // Automation upkeep every 300 seconds

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
        // TODO: Wire up base/asset automation upkeep when Application.AutomationService is available
        _logger.LogDebug("Running automation upkeep tick at game tick {TickCount}.", _tickCount);
        return Task.CompletedTask;
    }
}
