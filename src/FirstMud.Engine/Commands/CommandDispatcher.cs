using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FirstMud.Engine.Commands;

/// <summary>
/// Thin, domain-agnostic router that resolves a typed <see cref="ICommandHandler{TCommand}"/>
/// from DI and delegates to it. Adding new commands never requires modifying this class
/// (Open/Closed) — register a new handler in DI instead.
/// </summary>
public class CommandDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CommandDispatcher> _logger;

    public CommandDispatcher(IServiceProvider serviceProvider, ILogger<CommandDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<CommandResult> DispatchAsync(IGameCommand command, CancellationToken ct = default)
    {
        var commandType = command.GetType();
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);

        var handler = _serviceProvider.GetService(handlerType);
        if (handler is null)
        {
            _logger.LogWarning("No handler registered for command type {CommandType}", commandType.Name);
            return new CommandResult(false, $"Unknown command type: {commandType.Name}");
        }

        try
        {
            var method = handlerType.GetMethod(nameof(ICommandHandler<IGameCommand>.HandleAsync))!;
            var task = (Task<CommandResult>)method.Invoke(handler, [command, ct])!;
            return await task;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching command {CommandType} for player {PlayerId}",
                commandType.Name, command.PlayerId);
            return new CommandResult(false, $"Command failed: {ex.Message}");
        }
    }
}
