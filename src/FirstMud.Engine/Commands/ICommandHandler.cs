namespace FirstMud.Engine.Commands;

/// <summary>
/// Handles a specific command type. Each implementation is responsible for
/// exactly one command. Register per-command in DI; the dispatcher resolves
/// the correct handler at runtime.
/// </summary>
public interface ICommandHandler<TCommand> where TCommand : IGameCommand
{
    Task<CommandResult> HandleAsync(TCommand command, CancellationToken ct);
}
