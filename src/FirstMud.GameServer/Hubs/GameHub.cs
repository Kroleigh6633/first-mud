using Microsoft.AspNetCore.SignalR;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;
using FirstMud.Domain.Enums;

namespace FirstMud.GameServer.Hubs;

public class GameHub : Hub
{
    private static readonly Dictionary<string, Guid> ConnectionPlayerMap = new();

    private readonly GameLoopService _gameLoop;
    private readonly WorldStateService _worldStateService;

    public GameHub(GameLoopService gameLoop, WorldStateService worldStateService)
    {
        _gameLoop = gameLoop;
        _worldStateService = worldStateService;
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectionPlayerMap.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Authenticate(Guid playerId)
    {
        ConnectionPlayerMap[Context.ConnectionId] = playerId;
        await Groups.AddToGroupAsync(Context.ConnectionId, playerId.ToString());
        await Clients.Caller.SendAsync("Authenticated", playerId);

        // Push initial world-state snapshot so the status panel populates immediately
        try
        {
            var snapshot = await _worldStateService.GetSnapshotAsync(playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("WorldState", snapshot);
        }
        catch (InvalidOperationException ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }

        // Immediately seed the client with available quests, current zone view, and equipment state
        _gameLoop.EnqueueCommand(new GetAvailableQuestsCommand(playerId));
        _gameLoop.EnqueueCommand(new EnterZoneCommand(playerId, 0, Guid.Empty));
        _gameLoop.EnqueueCommand(new OpenInventoryCommand(playerId));
    }

    public async Task SendCommand(string command, object? payload)
    {
        if (!ConnectionPlayerMap.TryGetValue(Context.ConnectionId, out var playerId))
        {
            await Clients.Caller.SendAsync("Error", "Not authenticated.");
            return;
        }

        var cmd = ParseCommand(command, playerId, payload);
        if (cmd is null)
        {
            await Clients.Caller.SendAsync("Error", $"Unknown command: {command}");
            return;
        }

        _gameLoop.EnqueueCommand(cmd);
        await Clients.Caller.SendAsync("CommandReceived", new { command, playerId });
    }

    public async Task RequestWorldState()
    {
        if (!ConnectionPlayerMap.TryGetValue(Context.ConnectionId, out var playerId))
        {
            await Clients.Caller.SendAsync("Error", "Not authenticated.");
            return;
        }

        try
        {
            var snapshot = await _worldStateService.GetSnapshotAsync(playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("WorldState", snapshot);
        }
        catch (InvalidOperationException ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }

    /// <summary>
    /// Parses a command name and optional payload object into a typed IGameCommand.
    /// The payload object is expected to be a Dictionary&lt;string, object&gt; (as SignalR passes anonymous objects).
    /// </summary>
    private static IGameCommand? ParseCommand(string command, Guid playerId, object? payload)
    {
        return command.ToLowerInvariant() switch
        {
            "move" => new MoveCommand(
                playerId,
                TryGetInt(payload, "deltaX"),
                TryGetInt(payload, "deltaY")),

            "attack" => new AttackCommand(
                playerId,
                TryGetGuid(payload, "targetId")),

            "useskill" => new UseSkillCommand(
                playerId,
                TryGetString(payload, "skillId") ?? string.Empty,
                TryGetNullableGuid(payload, "targetId")),

            "interact" => new InteractCommand(
                playerId,
                TryGetGuid(payload, "objectId")),

            "pickupitem" => new PickupItemCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "openinventory" => new OpenInventoryCommand(playerId),

            "craft" => new CraftCommand(
                playerId,
                TryGetString(payload, "recipeId") ?? string.Empty,
                TryGetGuidList(payload, "componentIds"),
                TryGetNullableGuid(payload, "taperId")),

            "acceptquest" => new AcceptQuestCommand(
                playerId,
                TryGetString(payload, "questId") ?? string.Empty),

            "completequest" => new CompleteQuestCommand(
                playerId,
                TryGetString(payload, "questId") ?? string.Empty,
                TryGetString(payload, "chosenOutcome") ?? string.Empty),

            "useportal" when Enum.TryParse<WorldId>(TryGetString(payload, "destinationWorld"), out var world)
                => new UsePortalCommand(playerId, world),

            "managebaseasset" => new ManageBaseAssetCommand(
                playerId,
                TryGetString(payload, "action") ?? string.Empty,
                TryGetNullableGuid(payload, "assetId")),

            "combat start" => new StartCombatCommand(
                playerId,
                TryGetGuid(payload, "zoneId")),

            "combat use" => new UseCombatAbilityCommand(
                playerId,
                TryGetGuid(payload, "encounterId"),
                TryGetString(payload, "abilityName") ?? string.Empty,
                TryGetNullableGuid(payload, "targetId")),

            "combat flee" => new FleeCombatCommand(
                playerId,
                TryGetGuid(payload, "encounterId")),

            "getquests" => new GetAvailableQuestsCommand(playerId),

            "enterzone" => new EnterZoneCommand(
                playerId,
                TryGetInt(payload, "worldId"),
                TryGetGuid(payload, "zoneId")),

            "portalhome" => new PortalHomeCommand(playerId),

            "portalback" => new PortalBackCommand(playerId),

            "harvest" => new HarvestCommand(playerId),

            "deposit" => new DepositCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "withdraw" => new WithdrawCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "openstorage" => new OpenStorageCommand(playerId),

            "expandstorage" => new ExpandStorageCommand(playerId),

            "autofarm" => new AutoFarmCommand(
                playerId,
                TryGetNullableIntFromNested(payload, "targetZone", "x"),
                TryGetNullableIntFromNested(payload, "targetZone", "y"),
                TryGetInt(payload, "maxDanger") is int md and > 0 ? md : 10,
                TryGetString(payload, "priority") is string pr and { Length: > 0 } ? pr : "balanced"),

            "equip" => new EquipCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "unequip" => new UnequipCommand(
                playerId,
                TryGetString(payload, "slot") ?? "Weapon"),

            "salvage" => new SalvageCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "salvageall" => new SalvageAllCommand(
                playerId,
                TryGetString(payload, "category") ?? "Weapon"),

            "autosalvage" => new SetAutoSalvageCommand(
                playerId,
                TryGetString(payload, "category") ?? "weapon",
                TryGetInt(payload, "maxWorkmanship")),

            "lockitem" => new LockItemCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "viewcompanions" => new ViewCompanionsCommand(playerId),

            "activatecompanion" => new ActivateCompanionCommand(
                playerId,
                TryGetGuid(payload, "companionId")),

            "deactivatecompanion" => new DeactivateCompanionCommand(
                playerId,
                TryGetGuid(payload, "companionId")),

            "imbue" => new ImbueCommand(
                playerId,
                TryGetGuid(payload, "itemId"),
                TryGetGuid(payload, "taperId")),

            "assigncompanionduty" => new AssignCompanionDutyCommand(
                playerId,
                TryGetGuid(payload, "companionId"),
                TryGetString(payload, "duty") ?? string.Empty),

            "recallcompanion" => new RecallCompanionCommand(
                playerId,
                TryGetGuid(payload, "companionId")),

            "queuesalvage" => new QueueSalvageCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "viewrecipes" => new ViewRecipesCommand(playerId),

            "useconsumable" => new UseConsumableCommand(
                playerId,
                TryGetGuid(payload, "itemId")),

            "interactquest" => new InteractQuestCommand(
                playerId,
                TryGetString(payload, "questId") ?? string.Empty),

            "smelt" => new SmeltCommand(
                playerId,
                TryGetInt(payload, "amount") is int sa and > 0 ? sa : 1),

            _ => null
        };
    }

    private static int? TryGetNullableIntFromNested(object? payload, string outerKey, string innerKey)
    {
        if (payload is not System.Text.Json.JsonElement el
            || el.ValueKind != System.Text.Json.JsonValueKind.Object
            || !el.TryGetProperty(outerKey, out var outer)
            || outer.ValueKind != System.Text.Json.JsonValueKind.Object
            || !outer.TryGetProperty(innerKey, out var prop)
            || !prop.TryGetInt32(out var val))
            return null;
        return val;
    }

    private static int TryGetInt(object? payload, string key)
    {
        if (payload is System.Text.Json.JsonElement el
            && el.ValueKind == System.Text.Json.JsonValueKind.Object
            && el.TryGetProperty(key, out var prop)
            && prop.TryGetInt32(out var val))
            return val;
        return 0;
    }

    private static Guid TryGetGuid(object? payload, string key)
        => TryGetNullableGuid(payload, key) ?? Guid.Empty;

    private static Guid? TryGetNullableGuid(object? payload, string key)
    {
        if (payload is System.Text.Json.JsonElement el
            && el.ValueKind == System.Text.Json.JsonValueKind.Object
            && el.TryGetProperty(key, out var prop)
            && prop.ValueKind == System.Text.Json.JsonValueKind.String
            && Guid.TryParse(prop.GetString(), out var guid))
            return guid;
        return null;
    }

    private static string? TryGetString(object? payload, string key)
    {
        if (payload is System.Text.Json.JsonElement el
            && el.ValueKind == System.Text.Json.JsonValueKind.Object
            && el.TryGetProperty(key, out var prop)
            && prop.ValueKind == System.Text.Json.JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static List<Guid> TryGetGuidList(object? payload, string key)
    {
        var result = new List<Guid>();
        if (payload is not System.Text.Json.JsonElement el
            || el.ValueKind != System.Text.Json.JsonValueKind.Object
            || !el.TryGetProperty(key, out var prop)
            || prop.ValueKind != System.Text.Json.JsonValueKind.Array)
            return result;

        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == System.Text.Json.JsonValueKind.String
                && Guid.TryParse(item.GetString(), out var guid))
                result.Add(guid);
        }

        return result;
    }
}
