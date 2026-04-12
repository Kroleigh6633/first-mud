using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Handlers;

namespace FirstMud.GameServer.Services;

public record CommandResult(bool Success, string Message, object? Payload = null);

/// <summary>
/// Thin router that resolves a typed <see cref="ICommandHandler{TCommand}"/> from DI
/// and delegates to it. Adding new commands never requires modifying this class
/// (Open/Closed principle) — register a new handler in DI instead.
/// </summary>
public class CommandDispatcher(
    IServiceProvider serviceProvider,
    ILogger<CommandDispatcher> logger)
{
    public async Task<CommandResult> DispatchAsync(IGameCommand command, CancellationToken ct = default)
    {
        var commandType = command.GetType();
        var handlerType = typeof(ICommandHandler<>).MakeGenericType(commandType);

        var handler = serviceProvider.GetService(handlerType);
        if (handler is null)
        {
            logger.LogWarning("No handler registered for command type {CommandType}", commandType.Name);
            return new CommandResult(false, $"Unknown command type: {commandType.Name}");
        }

        try
        {
            // ICommandHandler<TCommand>.HandleAsync(TCommand, CancellationToken)
            // Invoked via reflection because the concrete type is only known at runtime.
            var method = handlerType.GetMethod(nameof(ICommandHandler<IGameCommand>.HandleAsync))!;
            var task = (Task<CommandResult>)method.Invoke(handler, [command, ct])!;
            return await task;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error dispatching command {CommandType} for player {PlayerId}",
                commandType.Name, command.PlayerId);
            return new CommandResult(false, $"Command failed: {ex.Message}");
        }
    }
}
