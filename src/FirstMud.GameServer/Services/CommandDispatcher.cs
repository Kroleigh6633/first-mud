using FirstMud.Application.Services;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Services;

public record CommandResult(bool Success, string Message, object? Payload = null);

public class CommandDispatcher
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IItemRepository _itemRepository;
    private readonly IQuestGraphRepository _questGraphRepository;
    private readonly QuestService _questService;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<CommandDispatcher> _logger;

    private readonly Dictionary<Type, Func<IGameCommand, CancellationToken, Task<CommandResult>>> _handlers;

    public CommandDispatcher(
        IPlayerRepository playerRepository,
        IItemRepository itemRepository,
        IQuestGraphRepository questGraphRepository,
        QuestService questService,
        IHubContext<GameHub> hubContext,
        ILogger<CommandDispatcher> logger)
    {
        _playerRepository = playerRepository;
        _itemRepository = itemRepository;
        _questGraphRepository = questGraphRepository;
        _questService = questService;
        _hubContext = hubContext;
        _logger = logger;

        _handlers = new Dictionary<Type, Func<IGameCommand, CancellationToken, Task<CommandResult>>>
        {
            [typeof(MoveCommand)]            = (cmd, ct) => HandleMoveAsync((MoveCommand)cmd, ct),
            [typeof(InteractCommand)]        = (cmd, ct) => HandleInteractAsync((InteractCommand)cmd, ct),
            [typeof(OpenInventoryCommand)]   = (cmd, ct) => HandleOpenInventoryAsync((OpenInventoryCommand)cmd, ct),
            [typeof(AcceptQuestCommand)]     = (cmd, ct) => HandleAcceptQuestAsync((AcceptQuestCommand)cmd, ct),
            [typeof(CompleteQuestCommand)]   = (cmd, ct) => HandleCompleteQuestAsync((CompleteQuestCommand)cmd, ct),
            [typeof(AttackCommand)]          = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(UseSkillCommand)]        = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(PickupItemCommand)]      = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(CraftCommand)]           = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(UsePortalCommand)]       = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(ManageBaseAssetCommand)] = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
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

    // -------------------------------------------------------------------------
    // MoveCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleMoveAsync(MoveCommand cmd, CancellationToken ct)
    {
        if (Math.Abs(cmd.DeltaX) > 1 || Math.Abs(cmd.DeltaY) > 1)
            return new CommandResult(false, "Move delta must be within ±1 per axis.");

        if (cmd.DeltaX == 0 && cmd.DeltaY == 0)
            return new CommandResult(false, "Move delta cannot be (0, 0).");

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

        return new CommandResult(true, $"Moved to ({newPosition.X}, {newPosition.Y}).", positionPayload);
    }

    // -------------------------------------------------------------------------
    // InteractCommand
    // -------------------------------------------------------------------------

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

    // -------------------------------------------------------------------------
    // OpenInventoryCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleOpenInventoryAsync(OpenInventoryCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var items = await _itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);

        var payload = new
        {
            player.Id,
            player.Name,
            ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
            CraftingSkill = player.CraftingSkill,
            SalvageSkill = player.SalvageSkill,
            Items = items.Select(i => new
            {
                i.Id,
                i.Name,
                i.Description,
                Workmanship = i.Workmanship.Value
            }).ToList()
        };

        return new CommandResult(true, "Inventory opened.", payload);
    }

    // -------------------------------------------------------------------------
    // AcceptQuestCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleAcceptQuestAsync(AcceptQuestCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var isAvailable = await _questGraphRepository.IsQuestAvailableAsync(cmd.PlayerId, cmd.QuestId, ct);
        if (!isAvailable)
            return new CommandResult(false, "Quest is not available for this player.");

        await _questGraphRepository.MarkQuestInProgressAsync(cmd.PlayerId, cmd.QuestId, takenByAi: false, ct: ct);

        var questPayload = new { cmd.PlayerId, QuestId = cmd.QuestId };

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("QuestAccepted", questPayload, ct);

        return new CommandResult(true, $"Quest '{cmd.QuestId}' accepted.", questPayload);
    }

    // -------------------------------------------------------------------------
    // CompleteQuestCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleCompleteQuestAsync(CompleteQuestCommand cmd, CancellationToken ct)
    {
        var result = await _questService.CompleteQuestAsync(cmd.PlayerId, cmd.QuestId, cmd.ChosenOutcome, ct);

        if (!result.Success)
            return new CommandResult(false, result.Message);

        // Broadcast QuestCompleted
        var questCompletedPayload = new
        {
            cmd.PlayerId,
            QuestId = cmd.QuestId,
            result.Message,
            result.ReputationGained,
            UnlockedQuests = result.UnlockedQuests.Select(q => q.QuestId).ToList(),
            result.WyrdSettled
        };

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("QuestCompleted", questCompletedPayload, ct);

        // Broadcast ReputationChanged with updated faction tiers
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is not null)
        {
            var factionTiers = player.Reputations.ToDictionary(
                r => r.FactionId.ToString(),
                r => r.Score.Tier.ToString());

            await _hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("ReputationChanged", new { cmd.PlayerId, FactionTiers = factionTiers }, ct);

            // Broadcast domain events
            foreach (var domainEvent in player.DomainEvents)
            {
                switch (domainEvent)
                {
                    case PortalUnlockedEvent portalEvent:
                        await _hubContext.Clients
                            .Group(cmd.PlayerId.ToString())
                            .SendAsync("PortalUnlocked", new
                            {
                                PlayerId = portalEvent.PlayerId,
                                WorldId = portalEvent.WorldId.ToString(),
                                Message = $"Portal to {portalEvent.WorldId} unlocked!"
                            }, ct);
                        break;

                    case PlayerLeveledUpEvent levelEvent:
                        await _hubContext.Clients
                            .Group(cmd.PlayerId.ToString())
                            .SendAsync("PlayerLeveledUp", new
                            {
                                PlayerId = levelEvent.PlayerId,
                                NewLevel = levelEvent.NewLevel,
                                Message = $"You reached level {levelEvent.NewLevel}!"
                            }, ct);
                        break;
                }
            }

            player.ClearDomainEvents();
        }

        return new CommandResult(true, result.Message, questCompletedPayload);
    }
}
