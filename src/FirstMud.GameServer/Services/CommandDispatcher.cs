using FirstMud.Application;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Dtos;
using FirstMud.GameServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

namespace FirstMud.GameServer.Services;

public record CommandResult(bool Success, string Message, object? Payload = null);

public record ZoneTileDto(
    string WorldId,
    int ZoneId,
    string Name,
    string Description,
    string AsciiSymbol,
    int DangerLevel,
    bool IsPortalZone,
    int X,
    int Y);

public record ZoneViewDto(List<ZoneTileDto> Tiles);

public class CommandDispatcher
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IItemRepository _itemRepository;
    private readonly IQuestGraphRepository _questGraphRepository;
    private readonly IZoneRepository _zoneRepository;
    private readonly IHomesteadRepository _homesteadRepository;
    private readonly IResourceNodeRepository _resourceNodeRepository;
    private readonly QuestService _questService;
    private readonly CombatService _combatService;
    private readonly LootService _lootService;
    private readonly AutoFarmService _autoFarmService;
    private readonly WorldStateService _worldStateService;
    private readonly GameNotificationService _notificationService;
    private readonly IHubContext<GameHub> _hubContext;
    private readonly ILogger<CommandDispatcher> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly Dictionary<Type, Func<IGameCommand, CancellationToken, Task<CommandResult>>> _handlers;

    public CommandDispatcher(
        IPlayerRepository playerRepository,
        IItemRepository itemRepository,
        IQuestGraphRepository questGraphRepository,
        IZoneRepository zoneRepository,
        IHomesteadRepository homesteadRepository,
        IResourceNodeRepository resourceNodeRepository,
        QuestService questService,
        CombatService combatService,
        LootService lootService,
        AutoFarmService autoFarmService,
        WorldStateService worldStateService,
        GameNotificationService notificationService,
        IHubContext<GameHub> hubContext,
        ILogger<CommandDispatcher> logger,
        IServiceScopeFactory scopeFactory)
    {
        _playerRepository = playerRepository;
        _itemRepository = itemRepository;
        _questGraphRepository = questGraphRepository;
        _zoneRepository = zoneRepository;
        _homesteadRepository = homesteadRepository;
        _resourceNodeRepository = resourceNodeRepository;
        _questService = questService;
        _combatService = combatService;
        _lootService = lootService;
        _autoFarmService = autoFarmService;
        _worldStateService = worldStateService;
        _notificationService = notificationService;
        _hubContext = hubContext;
        _logger = logger;
        _scopeFactory = scopeFactory;

        _handlers = new Dictionary<Type, Func<IGameCommand, CancellationToken, Task<CommandResult>>>
        {
            [typeof(MoveCommand)]               = (cmd, ct) => HandleMoveAsync((MoveCommand)cmd, ct),
            [typeof(InteractCommand)]           = (cmd, ct) => HandleInteractAsync((InteractCommand)cmd, ct),
            [typeof(OpenInventoryCommand)]      = (cmd, ct) => HandleOpenInventoryAsync((OpenInventoryCommand)cmd, ct),
            [typeof(AcceptQuestCommand)]        = (cmd, ct) => HandleAcceptQuestAsync((AcceptQuestCommand)cmd, ct),
            [typeof(CompleteQuestCommand)]      = (cmd, ct) => HandleCompleteQuestAsync((CompleteQuestCommand)cmd, ct),
            [typeof(GetAvailableQuestsCommand)] = (cmd, ct) => HandleGetAvailableQuestsAsync((GetAvailableQuestsCommand)cmd, ct),
            [typeof(EnterZoneCommand)]          = (cmd, ct) => HandleEnterZoneAsync((EnterZoneCommand)cmd, ct),
            [typeof(AttackCommand)]             = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(UseSkillCommand)]           = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(PickupItemCommand)]         = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(CraftCommand)]              = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(UsePortalCommand)]          = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(ManageBaseAssetCommand)]    = (cmd, _)  => Task.FromResult(new CommandResult(true, "Command queued.")),
            [typeof(StartCombatCommand)]        = (cmd, ct) => HandleStartCombatAsync((StartCombatCommand)cmd, ct),
            [typeof(UseCombatAbilityCommand)]   = (cmd, ct) => HandleUseCombatAbilityAsync((UseCombatAbilityCommand)cmd, ct),
            [typeof(FleeCombatCommand)]         = (cmd, ct) => HandleFleeCombatAsync((FleeCombatCommand)cmd, ct),
            [typeof(PortalHomeCommand)]         = (cmd, ct) => HandlePortalHomeAsync((PortalHomeCommand)cmd, ct),
            [typeof(PortalBackCommand)]         = (cmd, ct) => HandlePortalBackAsync((PortalBackCommand)cmd, ct),
            [typeof(HarvestCommand)]            = (cmd, ct) => HandleHarvestAsync((HarvestCommand)cmd, ct),
            [typeof(DepositCommand)]            = (cmd, ct) => HandleDepositAsync((DepositCommand)cmd, ct),
            [typeof(WithdrawCommand)]           = (cmd, ct) => HandleWithdrawAsync((WithdrawCommand)cmd, ct),
            [typeof(OpenStorageCommand)]        = (cmd, ct) => HandleOpenStorageAsync((OpenStorageCommand)cmd, ct),
            [typeof(AutoFarmCommand)]           = (cmd, ct) => HandleAutoFarmAsync((AutoFarmCommand)cmd, ct),
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

        // Random encounter check — if the player is near a dangerous zone
        // there's a (dangerLevel * 8)% chance an encounter fires each step.
        await TryTriggerEncounterAsync(cmd.PlayerId, newPosition, ct);

        return new CommandResult(true, $"Moved to ({newPosition.X}, {newPosition.Y}).", positionPayload);
    }

    private async Task TryTriggerEncounterAsync(Guid playerId, Position pos, CancellationToken ct)
    {
        var zones = await _zoneRepository.GetByWorldAsync(pos.World, ct);
        Domain.Entities.Zone? nearbyZone = null;
        foreach (var z in zones)
        {
            var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
            var (zx, zy) = known ?? ZoneGridLayout.GetPosition(z.Id);
            if (Math.Abs(zx - pos.X) <= 1 && Math.Abs(zy - pos.Y) <= 1)
            {
                nearbyZone = z;
                break;
            }
        }

        if (nearbyZone is null || nearbyZone.DangerLevel <= 0) return;

        // Roll: dangerLevel * 8% chance per step, capped at 80%
        var chance = Math.Min(nearbyZone.DangerLevel * 8, 80);
        if (Random.Shared.Next(100) >= chance) return;

        // Trigger encounter!
        var player = await _playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var monsters = BuildMonsterPack(nearbyZone.DangerLevel);
        var encounter = await _combatService.StartEncounterAsync(
            playerId, Guid.NewGuid(), player, [], monsters, ct);

        // Narrate the encounter in the game log
        var monsterNames = string.Join(", ", monsters.Select(m => m.Name));
        await _hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "combat",
                text = $"Hostile creatures emerge from {nearbyZone.Name}! You face: {monsterNames}."
            }, ct);

        // Auto-process any enemy turns that fire before the player
        await ProcessEnemyTurnsAsync(playerId, encounter, ct);

        var dto = BuildCombatUpdateDto(encounter);
        await _hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);
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
            PlayerId = player.Id,
            player.Name,
            ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
            CraftingSkill = player.CraftingSkill,
            SalvageSkill = player.SalvageSkill,
            Items = items.Select(i => new
            {
                Id = i.Id.ToString(),
                i.Name,
                i.Description,
                Workmanship = i.Workmanship.Value
            }).ToList()
        };

        // Broadcast so the client can open an Inventory panel.
        await _notificationService.SendEventAsync(cmd.PlayerId, "Inventory", payload, ct);

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

    // -------------------------------------------------------------------------
    // GetAvailableQuestsCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleGetAvailableQuestsAsync(GetAvailableQuestsCommand cmd, CancellationToken ct)
    {
        var quests = await _worldStateService.GetAvailableQuestsAsync(cmd.PlayerId, ct);

        var questPayload = quests.Select(q => new
        {
            q.QuestId,
            q.Title,
            q.Description,
            FactionId = (int)q.FactionId,
            RequiredTier = (int)q.RequiredTier,
            q.ReputationReward,
            q.PossibleOutcomes,
            q.IsWyrdQuest,
            q.IsTaken,
        }).ToList();

        await _notificationService.SendEventAsync(cmd.PlayerId, "AvailableQuests", questPayload, ct);

        return new CommandResult(true, $"Fetched {quests.Count} available quests.", questPayload);
    }

    // -------------------------------------------------------------------------
    // EnterZoneCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleEnterZoneAsync(EnterZoneCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var worldId = player.Position.World;
        var zones = await _zoneRepository.GetByWorldAsync(worldId, ct);

        var tiles = zones.Select(z =>
        {
            var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
            var (x, y) = known ?? ZoneGridLayout.GetPosition(z.Id);
            return new ZoneTileDto(
                z.WorldId.ToString(),
                z.ZoneId,
                z.Name,
                z.Description,
                z.AsciiSymbol,
                z.DangerLevel,
                z.IsPortalZone,
                x,
                y);
        }).ToList();

        var viewDto = new ZoneViewDto(tiles);

        await _notificationService.SendEventAsync(cmd.PlayerId, "ZoneView", viewDto, ct);

        return new CommandResult(true, $"Zone view sent with {tiles.Count} tiles.", viewDto);
    }

    // -------------------------------------------------------------------------
    // StartCombatCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleStartCombatAsync(StartCombatCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        // Derive a simple danger level from the zone id (deterministic, no DB lookup)
        var dangerLevel = (int)(cmd.ZoneId.GetHashCode() & 0x7FFFFFFF) % 3 + 1;
        var monsters = BuildMonsterPack(dangerLevel);

        var encounter = await _combatService.StartEncounterAsync(
            cmd.PlayerId, cmd.ZoneId, player, [], monsters, ct);

        // If the fastest combatant is an enemy, auto-run enemy turns
        // until it's the player's turn.
        await ProcessEnemyTurnsAsync(cmd.PlayerId, encounter, ct);

        var dto = BuildCombatUpdateDto(encounter);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, "Combat started.", dto);
    }

    // -------------------------------------------------------------------------
    // UseCombatAbilityCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleUseCombatAbilityAsync(UseCombatAbilityCommand cmd, CancellationToken ct)
    {
        // Resolve the actor id — find the player-side combatant that belongs to this player
        var encounter = _combatService.GetEncounter(cmd.EncounterId);
        if (encounter is null)
            return new CommandResult(false, "Encounter not found.");

        // The actor whose turn it is must belong to this player
        var currentActor = encounter.CurrentActor;
        if (currentActor is null)
            return new CommandResult(false, "No current actor.");

        var (success, message, updated) = await _combatService.ExecuteActionAsync(
            cmd.EncounterId, currentActor.Id, cmd.AbilityName, cmd.TargetId, ct);

        if (!success || updated is null)
            return new CommandResult(false, message);

        // After the player acts, enemies may be next — auto-run them.
        await ProcessEnemyTurnsAsync(cmd.PlayerId, updated, ct);

        // On Victory — roll for loot drop
        if (updated.State == EncounterState.Victory)
            await TryRollLootAsync(cmd.PlayerId, updated.ZoneId, ct);

        var dto = BuildCombatUpdateDto(updated);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, message, dto);
    }

    // -------------------------------------------------------------------------
    // FleeCombatCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleFleeCombatAsync(FleeCombatCommand cmd, CancellationToken ct)
    {
        var (success, message, updated) = await _combatService.FleeAsync(cmd.EncounterId, ct);

        if (!success || updated is null)
            return new CommandResult(false, message);

        var dto = BuildCombatUpdateDto(updated);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CombatUpdate", dto, ct);

        return new CommandResult(true, message, dto);
    }

    // -------------------------------------------------------------------------
    // TryRollLootAsync — called after combat Victory
    // -------------------------------------------------------------------------

    private async Task TryRollLootAsync(Guid playerId, Guid zoneId, CancellationToken ct)
    {
        // Determine danger level from the zone if available, fall back to a deterministic value
        var zones = await _zoneRepository.GetByWorldAsync(Domain.Enums.WorldId.Aeldran, ct);
        var zone = zones.FirstOrDefault(z => z.Id == zoneId);
        var dangerLevel = zone?.DangerLevel ?? 2;

        var items = await _itemRepository.GetByOwnerAsync(playerId, ct);
        var player = await _playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return;

        var result = await _lootService.RollLootDropAsync(
            dangerLevel,
            playerId,
            player.Position.World,
            items.Count,
            player.MaxInventorySlots,
            ct);

        if (!result.Dropped)
        {
            if (!string.IsNullOrEmpty(result.Message))
            {
                await _hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "loot",
                        text = result.Message
                    }, ct);
            }
            return;
        }

        var item = result.Item!;
        var lootPayload = new
        {
            item.Id,
            item.Name,
            item.Description,
            Workmanship = item.Workmanship.Value,
            Category = item.Category.ToString()
        };

        await _hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "loot",
                text = result.Message
            }, ct);

        await _hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("LootDropped", lootPayload, ct);
    }

    // -------------------------------------------------------------------------
    // PortalHomeCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandlePortalHomeAsync(PortalHomeCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        // Homestead is located at a fixed "sanctuary" coordinate outside normal map
        var homesteadPos = new Position(player.Position.World, 0, -100, -100);
        player.PortalHome(homesteadPos);
        await _playerRepository.UpdateAsync(player, ct);

        var posPayload = new
        {
            player.Id,
            X = homesteadPos.X,
            Y = homesteadPos.Y,
            ZoneId = homesteadPos.ZoneId,
            World = homesteadPos.World.ToString()
        };

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", posPayload, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "system",
                text = "You step through the portal and arrive at your homestead. The world is quiet here."
            }, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("AtHomestead", new { playerId = cmd.PlayerId }, ct);

        return new CommandResult(true, "Portaled home.", posPayload);
    }

    // -------------------------------------------------------------------------
    // PortalBackCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandlePortalBackAsync(PortalBackCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.SavedReturnPosition is null)
            return new CommandResult(false, "No saved return position. Explore the world first.");

        player.PortalBack();
        await _playerRepository.UpdateAsync(player, ct);

        var posPayload = new
        {
            player.Id,
            player.Position.X,
            player.Position.Y,
            ZoneId = player.Position.ZoneId,
            World = player.Position.World.ToString()
        };

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("PlayerMoved", posPayload, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "system",
                text = "You step back through the portal and return to where you were."
            }, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("LeftHomestead", new { playerId = cmd.PlayerId }, ct);

        return new CommandResult(true, "Portaled back.", posPayload);
    }

    // -------------------------------------------------------------------------
    // HarvestCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleHarvestAsync(HarvestCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        // Find zones near the player to identify current zone
        var zones = await _zoneRepository.GetByWorldAsync(player.Position.World, ct);
        Domain.Entities.Zone? nearbyZone = null;
        foreach (var z in zones)
        {
            var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
            var (zx, zy) = known ?? ZoneGridLayout.GetPosition(z.Id);
            if (Math.Abs(zx - player.Position.X) <= 1 && Math.Abs(zy - player.Position.Y) <= 1)
            {
                nearbyZone = z;
                break;
            }
        }

        if (nearbyZone is null)
            return new CommandResult(false, "There is nothing to harvest here.");

        var nodes = await _resourceNodeRepository.GetByZoneIdAsync(nearbyZone.Id, ct);
        if (nodes.Count == 0)
            return new CommandResult(false, "No resource nodes in this area.");

        // Pick the first node with remaining yield
        var node = nodes.FirstOrDefault(n => n.RemainingYield > 0);
        if (node is null)
            return new CommandResult(false, "The resources here are depleted. Return later.");

        // Check carry capacity
        var currentItems = await _itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        if (!player.CanCarryMore(currentItems.Count))
        {
            await _hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "system",
                    text = "Your inventory is full!"
                }, ct);
            return new CommandResult(false, "Inventory full.");
        }

        var harvestAmount = Random.Shared.Next(1, 4); // 1-3 units
        var actual = node.Harvest(harvestAmount);
        await _resourceNodeRepository.UpdateAsync(node, ct);

        // Create a resource item in the player's inventory
        var resourceName = node.ResourceType.ToString();
        var resourceItem = Domain.Entities.Item.Create(
            $"{resourceName} Bundle",
            $"A bundle of {actual} unit(s) of {resourceName.ToLowerInvariant()} gathered from the wilds.",
            ItemCategory.Component,
            Domain.ValueObjects.Workmanship.Of(1),
            player.Position.World);
        resourceItem.SetOwner(cmd.PlayerId);

        await _itemRepository.AddAsync(resourceItem, ct);

        var message = $"You harvested {actual} unit(s) of {resourceName}.";

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "loot",
                text = message
            }, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("HarvestComplete", new
            {
                ItemId = resourceItem.Id,
                resourceItem.Name,
                Amount = actual,
                ResourceType = node.ResourceType.ToString()
            }, ct);

        return new CommandResult(true, message);
    }

    // -------------------------------------------------------------------------
    // DepositCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleDepositAsync(DepositCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        // Must be at homestead
        if (player.Position.X != -100 || player.Position.Y != -100)
            return new CommandResult(false, "You must be at your homestead to deposit items.");

        var homestead = await _homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "Homestead not found.");

        var item = await _itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "Item not found in your inventory.");

        // Check storage capacity
        var storageItems = await _homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
        if (storageItems.Count >= homestead.StorageSlots)
            return new CommandResult(false, "Homestead storage is full.");

        // Move item: clear owner, create storage entry
        item.SetOwner(null);
        await _itemRepository.UpdateAsync(item, ct);

        var storageItem = Domain.Entities.HomesteadStorageItem.Create(homestead.Id, item.Id);
        await _homesteadRepository.AddStorageItemAsync(storageItem, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "system",
                text = $"Deposited {item.Name} into homestead storage."
            }, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("StorageUpdated", new { homesteadId = homestead.Id }, ct);

        return new CommandResult(true, $"Deposited {item.Name}.");
    }

    // -------------------------------------------------------------------------
    // WithdrawCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleWithdrawAsync(WithdrawCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.Position.X != -100 || player.Position.Y != -100)
            return new CommandResult(false, "You must be at your homestead to withdraw items.");

        var homestead = await _homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "Homestead not found.");

        var storageItems = await _homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
        var storageEntry = storageItems.FirstOrDefault(s => s.ItemId == cmd.ItemId);
        if (storageEntry is null)
            return new CommandResult(false, "Item not found in storage.");

        var inventoryItems = await _itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        if (!player.CanCarryMore(inventoryItems.Count))
            return new CommandResult(false, "Your inventory is full!");

        var item = await _itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null)
            return new CommandResult(false, "Item not found.");

        // Move item back to player
        item.SetOwner(cmd.PlayerId);
        await _itemRepository.UpdateAsync(item, ct);
        await _homesteadRepository.RemoveStorageItemAsync(homestead.Id, cmd.ItemId, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "system",
                text = $"Withdrew {item.Name} from homestead storage."
            }, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("StorageUpdated", new { homesteadId = homestead.Id }, ct);

        return new CommandResult(true, $"Withdrew {item.Name}.");
    }

    // -------------------------------------------------------------------------
    // OpenStorageCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleOpenStorageAsync(OpenStorageCommand cmd, CancellationToken ct)
    {
        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.Position.X != -100 || player.Position.Y != -100)
            return new CommandResult(false, "You must be at your homestead to open storage.");

        var homestead = await _homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "Homestead not found.");

        var storageEntries = await _homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
        var itemIds = storageEntries.Select(s => s.ItemId).ToList();
        var items = await _itemRepository.GetByIdsAsync(itemIds, ct);

        var payload = new
        {
            HomesteadId = homestead.Id,
            homestead.Name,
            homestead.StorageSlots,
            UsedSlots = storageEntries.Count,
            Items = items.Select(i => new
            {
                Id = i.Id.ToString(),
                i.Name,
                i.Description,
                Category = i.Category.ToString(),
                Workmanship = i.Workmanship.Value
            }).ToList()
        };

        await _notificationService.SendEventAsync(cmd.PlayerId, "StorageView", payload, ct);

        return new CommandResult(true, "Storage opened.", payload);
    }

    // -------------------------------------------------------------------------
    // AutoFarmCommand
    // -------------------------------------------------------------------------

    private async Task<CommandResult> HandleAutoFarmAsync(AutoFarmCommand cmd, CancellationToken ct)
    {
        // Cancel any existing session
        if (_autoFarmService.IsActive(cmd.PlayerId))
        {
            _autoFarmService.EndSession(cmd.PlayerId);
            await _hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "system",
                    text = "Auto-farm cancelled."
                }, ct);
            await _hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("AutoFarmStatus", new { active = false }, ct);
            return new CommandResult(true, "Auto-farm cancelled.");
        }

        var player = await _playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var session = _autoFarmService.StartSession(cmd.PlayerId, cmd.DurationSeconds);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category = "system",
                text = $"Auto-farm started. Duration: {cmd.DurationSeconds / 60} minute(s). Press [F] again to stop."
            }, ct);

        await _hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("AutoFarmStatus", new { active = true, durationSeconds = cmd.DurationSeconds }, ct);

        // Run the farm loop in the background (fire-and-forget with its own scope)
        _ = Task.Run(async () =>
        {
            await RunAutoFarmLoopAsync(cmd.PlayerId, session, ct);
        }, CancellationToken.None);

        return new CommandResult(true, "Auto-farm started.");
    }

    private async Task RunAutoFarmLoopAsync(Guid playerId, AutoFarmSession session, CancellationToken serverCt)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(serverCt, session.Cts.Token);
        var farmCt = linked.Token;

        var endTime = session.StartedAt.AddSeconds(session.DurationSeconds);
        var intervalMs = 3000;

        try
        {
            while (!farmCt.IsCancellationRequested && DateTimeOffset.UtcNow < endTime)
            {
                await Task.Delay(intervalMs, farmCt);
                if (farmCt.IsCancellationRequested) break;

                await using var scope = _scopeFactory.CreateAsyncScope();
                var playerRepo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
                var itemRepo = scope.ServiceProvider.GetRequiredService<IItemRepository>();
                var zoneRepo = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
                var lootSvc = scope.ServiceProvider.GetRequiredService<LootService>();

                var player = await playerRepo.GetByIdAsync(playerId, farmCt);
                if (player is null) break;

                // Periodic update
                var elapsed = DateTimeOffset.UtcNow - session.StartedAt;
                var remaining = endTime - DateTimeOffset.UtcNow;
                var remainingMin = Math.Max(0, (int)remaining.TotalMinutes);
                var remainingSec = Math.Max(0, (int)remaining.TotalSeconds % 60);

                await _hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "system",
                        text = $"Auto-farming... {remainingMin}m {remainingSec}s left. Kills: {session.Kills}. Items: {session.ItemsFound}."
                    }, farmCt);

                // Roll for encounter
                var zones = await zoneRepo.GetByWorldAsync(player.Position.World, farmCt);
                Domain.Entities.Zone? nearbyZone = null;
                foreach (var z in zones)
                {
                    var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
                    var (zx, zy) = known ?? ZoneGridLayout.GetPosition(z.Id);
                    if (Math.Abs(zx - player.Position.X) <= 1 && Math.Abs(zy - player.Position.Y) <= 1)
                    {
                        nearbyZone = z;
                        break;
                    }
                }

                var dangerLevel = nearbyZone?.DangerLevel ?? 2;
                var encounterChance = Math.Min(dangerLevel * 10, 70);
                if (Random.Shared.Next(100) >= encounterChance)
                    continue;

                // Start and auto-fight the encounter
                var monsters = BuildMonsterPack(dangerLevel);
                var combatSvc = scope.ServiceProvider.GetRequiredService<CombatService>();

                var encounter = await combatSvc.StartEncounterAsync(
                    playerId, nearbyZone?.Id ?? Guid.NewGuid(), player, [], monsters, farmCt);

                await _hubContext.Clients
                    .Group(playerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "combat",
                        text = $"[Auto-farm] Encounter! {string.Join(", ", monsters.Select(m => m.Name))}."
                    }, farmCt);

                // Auto-fight until victory or defeat
                var safety = 0;
                while (encounter.State == EncounterState.InProgress && safety++ < 50)
                {
                    var actor = encounter.CurrentActor;
                    if (actor is null) break;

                    if (actor.IsPlayerSide)
                    {
                        // Auto-attack: use Strike on a random enemy
                        var enemies = encounter.Combatants
                            .Where(c => !c.IsPlayerSide && !c.IsDefeated)
                            .ToList();
                        if (enemies.Count == 0) break;
                        var target = enemies[Random.Shared.Next(enemies.Count)];
                        await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, "Strike", target.Id, farmCt);
                    }
                    else
                    {
                        // Enemy turn — auto-select ability and target
                        var abilities = actor.Abilities.Where(a => a.Category == AbilityCategory.Attack).ToList();
                        if (abilities.Count == 0) break;
                        var ability = abilities[Random.Shared.Next(abilities.Count)];
                        var allies = encounter.Combatants.Where(c => c.IsPlayerSide && !c.IsDefeated).ToList();
                        if (allies.Count == 0) break;
                        var target = allies[Random.Shared.Next(allies.Count)];
                        await combatSvc.ExecuteActionAsync(encounter.Id, actor.Id, ability.Name, target.Id, farmCt);
                    }

                    encounter = combatSvc.GetEncounter(encounter.Id) ?? encounter;
                }

                if (encounter.State == EncounterState.Defeat)
                {
                    _autoFarmService.EndSession(playerId);
                    await _hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("GameMessage", new
                        {
                            timestamp = DateTime.UtcNow.ToString("O"),
                            category = "combat",
                            text = "Auto-farm ended: you were defeated."
                        }, serverCt);
                    await _hubContext.Clients
                        .Group(playerId.ToString())
                        .SendAsync("AutoFarmStatus", new { active = false, reason = "defeat" }, serverCt);
                    return;
                }

                if (encounter.State == EncounterState.Victory)
                {
                    _autoFarmService.RecordKill(playerId);

                    // Auto-loot
                    var currentItems = await itemRepo.GetByOwnerAsync(playerId, farmCt);
                    var lootResult = await lootSvc.RollLootDropAsync(
                        dangerLevel, playerId, player.Position.World,
                        currentItems.Count, player.MaxInventorySlots, farmCt);

                    if (lootResult.Dropped)
                    {
                        _autoFarmService.RecordItem(playerId);
                        await _hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("GameMessage", new
                            {
                                timestamp = DateTime.UtcNow.ToString("O"),
                                category = "loot",
                                text = $"[Auto-farm] {lootResult.Message}"
                            }, farmCt);
                        if (lootResult.Item is not null)
                            await _hubContext.Clients
                                .Group(playerId.ToString())
                                .SendAsync("LootDropped", new
                                {
                                    lootResult.Item.Id,
                                    lootResult.Item.Name,
                                    lootResult.Item.Description,
                                    Workmanship = lootResult.Item.Workmanship.Value,
                                    Category = lootResult.Item.Category.ToString()
                                }, farmCt);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Cancelled — normal exit
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Auto-farm loop error for player {PlayerId}", playerId);
        }
        finally
        {
            var finalSession = _autoFarmService.GetSession(playerId);
            var kills = finalSession?.Kills ?? session.Kills;
            var items = finalSession?.ItemsFound ?? session.ItemsFound;
            _autoFarmService.EndSession(playerId);

            await _hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "system",
                    text = $"Auto-farm complete: {kills} kill(s), {items} item(s) found."
                }, serverCt);

            await _hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("AutoFarmStatus", new { active = false, kills, items }, serverCt);
        }
    }

    // -------------------------------------------------------------------------
    // Enemy auto-turn processing
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the current actor is an enemy, automatically picks an ability
    /// and a target, executes the action, narrates it, and advances the
    /// turn — repeating until it's a player-side combatant's turn or the
    /// encounter ends. This makes combat feel responsive: the player
    /// always sees the panel on THEIR turn with results of enemy attacks.
    /// </summary>
    private async Task ProcessEnemyTurnsAsync(Guid playerId, Domain.Entities.Encounter encounter, CancellationToken ct)
    {
        var safety = 0;
        while (encounter.State == EncounterState.InProgress && safety++ < 20)
        {
            var actor = encounter.CurrentActor;
            if (actor is null || actor.IsPlayerSide) break;

            // Pick a random ability
            var abilities = actor.Abilities.Where(a => a.Category == AbilityCategory.Attack).ToList();
            if (abilities.Count == 0) break;
            var ability = abilities[Random.Shared.Next(abilities.Count)];

            // Pick a random living player-side target
            var targets = encounter.Combatants
                .Where(c => c.IsPlayerSide && !c.IsDefeated)
                .ToList();
            if (targets.Count == 0) break;
            var target = targets[Random.Shared.Next(targets.Count)];

            // Execute
            var (success, _, _) = await _combatService.ExecuteActionAsync(
                encounter.Id, actor.Id, ability.Name, target.Id, ct);

            if (!success) break;

            // Narrate in the game log
            int rawPower = ability.BasePower + actor.Level * 2;
            float mult = ElementMatchup.GetMultiplier(ability.Element, target.Element);
            int dmg = (int)(rawPower * mult);
            var multLabel = mult > 1f ? " (super effective!)" : mult < 1f ? " (resisted)" : "";

            await _hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category = "combat",
                    text = $"{actor.Name} uses {ability.Name} on {target.Name} for {dmg} damage{multLabel}."
                }, ct);
        }
    }

    // -------------------------------------------------------------------------
    // Combat helpers
    // -------------------------------------------------------------------------

    private static List<MonsterTemplate> BuildMonsterPack(int dangerLevel)
    {
        // Ability templates — power values are kept low so a level 1 player
        // with 100 HP and ~20 damage/turn can actually win fights.
        var claw      = new CombatAbility("Claw",        6, 0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var bite      = new CombatAbility("Bite",        8, 0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var fireSpit  = new CombatAbility("Fire Spit",  10, 0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var waterJet  = new CombatAbility("Water Jet",  10, 0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
        var airSlash  = new CombatAbility("Air Slash",   9, 0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

        // Monster pools per danger tier — randomly pick from the pool
        // so the player doesn't always face the same pack.
        MonsterTemplate[][] pools = [
            // Danger 1-2: single weak creature
            [
                new("Cave Rat",      25, 5, 1, MagicElement.Earth, [claw]),
                new("Marsh Bat",     20, 8, 1, MagicElement.Air,   [airSlash]),
                new("Fire Beetle",   28, 4, 1, MagicElement.Fire,  [fireSpit]),
                new("Stream Eel",    22, 7, 1, MagicElement.Water, [waterJet]),
            ],
            // Danger 3-4: one tougher creature or two weak ones
            [
                new("Thornwood Wolf", 35, 7, 2, MagicElement.Earth, [bite]),
                new("Fire Imp",       30, 6, 2, MagicElement.Fire,  [fireSpit]),
                new("Bog Wraith",     28, 8, 2, MagicElement.Water, [waterJet]),
                new("Wind Sprite",    25, 9, 2, MagicElement.Air,   [airSlash]),
            ],
            // Danger 5-7: one strong creature
            [
                new("Grave Stalker", 50, 8, 3, MagicElement.Earth, [bite, claw]),
                new("Ashlands Drake",55, 7, 3, MagicElement.Fire,  [fireSpit, bite]),
                new("Tide Serpent",  45, 9, 3, MagicElement.Water, [waterJet, bite]),
            ],
            // Danger 8-10: one elite or two strong
            [
                new("Elder Drake",    70, 9, 4, MagicElement.Fire,  [fireSpit, bite]),
                new("Deep Horror",    65, 8, 4, MagicElement.Water, [waterJet, bite]),
                new("Wyrd Stalker",   60, 10, 4, MagicElement.Aether, [bite, claw]),
            ],
        ];

        var tierIndex = dangerLevel switch
        {
            <= 2 => 0,
            <= 4 => 1,
            <= 7 => 2,
            _    => 3,
        };

        var pool = pools[tierIndex];
        var pack = new List<MonsterTemplate>();

        // Pick 1 monster for low danger, maybe 2 for mid, 2 for high
        var primary = pool[Random.Shared.Next(pool.Length)];
        pack.Add(primary);

        // At danger 3+, 50% chance of a second (weaker) creature
        if (dangerLevel >= 3 && Random.Shared.Next(2) == 0)
        {
            var weakPool = pools[Math.Max(0, tierIndex - 1)];
            pack.Add(weakPool[Random.Shared.Next(weakPool.Length)]);
        }

        // At danger 7+, always add a second creature
        if (dangerLevel >= 7 && pack.Count == 1)
        {
            var midPool = pools[Math.Max(0, tierIndex - 1)];
            pack.Add(midPool[Random.Shared.Next(midPool.Length)]);
        }

        return pack;
    }

    private static CombatUpdateDto BuildCombatUpdateDto(Domain.Entities.Encounter encounter)
    {
        var combatantDtos = encounter.Combatants
            .Select(c => new CombatantDto(
                c.Id,
                c.Name,
                c.CombatantType.ToString(),
                c.CurrentHp,
                c.MaxHp,
                c.Speed,
                c.Element.ToString(),
                c.IsPlayerSide,
                c.IsDefeated,
                c.Abilities.Select(a => new AbilityDto(
                    a.Name,
                    a.BasePower,
                    a.WeaveCost,
                    a.Element.ToString(),
                    a.Category.ToString())).ToList()))
            .ToList();

        var currentActorId = encounter.CurrentActor?.Id ?? Guid.Empty;

        return new CombatUpdateDto(
            encounter.Id,
            encounter.State.ToString(),
            combatantDtos,
            currentActorId,
            encounter.RoundNumber);
    }
}
