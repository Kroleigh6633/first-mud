using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Handles a specific command type. Each implementation is responsible for
/// exactly one command — satisfying Single Responsibility and Open/Closed
/// (new commands add a new handler, never modify existing ones).
/// </summary>
public interface ICommandHandler<TCommand> where TCommand : IGameCommand
{
    Task<CommandResult> HandleAsync(TCommand command, CancellationToken ct);
}
