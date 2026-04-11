using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using Microsoft.AspNetCore.SignalR;
using FirstMud.GameServer.Hubs;

namespace FirstMud.GameServer.Services;

public record CommandResult(bool Success, string Message, object? Payload = null);

public class CommandDispatcher
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<CommandDispatcher> _logger;

    private readonly Dictionary<Type, Func<IGameCommand, CancellationToken, Task<CommandResult>>> _handlers;

    public CommandDispatcher(
        IPlayerRepository playerRepository,
        IHubContext<GameHub> hubContext,
        ILogger<CommandDispatcher> logger)
    {
        _playerRepository = playerRepository;
        _hubContext = hubContext;
        _logger = logger;

        _handlers = new Dictionary<Type, Func<IGameCommand, CancellationToken, Task<CommandResult>>>
        {
            [typeof(MoveCommand)]          = (cmd, ct) => HandleMoveAsync((MoveCommand)cmd, ct),
            [typeof(InteractCommand)]      = (cmd, ct) => HandleInteractAsync((InteractCommand)cmd, ct),
            [typeof(OpenInventoryCommand)] = (cmd, ct) => HandleOpenInventoryAsync((OpenInventoryCommand)cmd, ct),
            [typeof(AttackCommand)]        = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(UseSkillCommand)]      = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(PickupItemCommand)]    = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(CraftCommand)]         = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(AcceptQuestCommand)]   = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(CompleteQuestCommand)] = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(UsePortalCommand)]     = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(ManageBaseAssetCommand)] = (cmd, _) => Task.FromResult(new CommandResult(true, "Command queued.")),
        };
    }

    public async Task<CommandResult> DispatchAsync(IGameCommand command, CancellationToken ct = default)
    {
        var commandType = command.GetType();
        if (!_handlers.TryGetValue(commandType, out var handler))
        {
            _logger.LogWarning("No handler registered for command type {CommandType}", commandType.Name);
            return new CommandResult(false, $"Unknown command type: {commandType.Name}");
        }

        try
        {
            return await handler(command, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error dispatching command {CommandType} for player {PlayerId}",
                commandType.Name, command.PlayerId);
            return new CommandResult(false, $"Command failed: {ex.Message}");
        }
    }

    private async Task<CommandResult> HandleMoveAsync(MoveCommand cmd, CancellationToken ct)
    {
        if (Math.Abs(cmd.DeltaX) > 1 || Math.Abs(cmd.DeltaY) > 1)
            return new CommandResult(false, "Move delta must be within ±1 per axis.");

        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var current = player.Position;
        var newPosition = new Position(
            current.World,
            current.ZoneId,
            current.X + cmd.DeltaX,
            current.Y + cmd.DeltaY);

        player.Move(newPosition);
        await _playerRepository.UpdateAsync(player, ct);

        var positionPayload = new
        {
            player.Id,
            newPosition.X,
            newPosition.Y,
            newPosition.ZoneId,
            World = newPosition.World.ToString()
        };

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", positionPayload, ct);

        return new CommandResult(true, "Moved.", positionPayload);
    }

    private async Task<CommandResult> HandleInteractAsync(InteractCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var payload = new
        {
            player.Id,
            player.Name,
            player.Level,
            player.CurrentHp,
            player.MaxHp,
            Position = new { player.Position.X, player.Position.Y, player.Position.ZoneId },
            ObjectId = cmd.ObjectId
        };

        return new CommandResult(true, "Interaction noted.", payload);
    }

    private async Task<CommandResult> HandleOpenInventoryAsync(OpenInventoryCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var payload = new
        {
            player.Id,
            player.Name,
            ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
            CraftingSkill = player.CraftingSkill,
            SalvageSkill = player.SalvageSkill
        };

        return new CommandResult(true, "Inventory opened.", payload);
    }
}
