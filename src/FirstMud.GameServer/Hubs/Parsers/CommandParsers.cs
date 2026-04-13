using System.Text.Json;
using FirstMud.Domain.Enums;
using FirstMud.Engine.Commands;
using FirstMud.GameServer.Commands;

namespace FirstMud.GameServer.Hubs.Parsers;

// One tiny parser per command name. Adding a new command = add a class here + one
// DI registration in Program.cs. The dispatcher (GameServerCommandFactory) looks
// them up by CommandName (case-insensitive).

public sealed class MoveCommandParser : ICommandParser
{
    public string CommandName => "move";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new MoveCommand(playerId, payload.TryGetInt("deltaX"), payload.TryGetInt("deltaY"));
}

public sealed class AttackCommandParser : ICommandParser
{
    public string CommandName => "attack";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new AttackCommand(playerId, payload.TryGetGuid("targetId"));
}

public sealed class UseSkillCommandParser : ICommandParser
{
    public string CommandName => "useskill";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new UseSkillCommand(playerId, payload.TryGetString("skillId") ?? string.Empty, payload.TryGetNullableGuid("targetId"));
}

public sealed class InteractCommandParser : ICommandParser
{
    public string CommandName => "interact";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new InteractCommand(playerId, payload.TryGetGuid("objectId"));
}

public sealed class PickupItemCommandParser : ICommandParser
{
    public string CommandName => "pickupitem";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new PickupItemCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class OpenInventoryCommandParser : ICommandParser
{
    public string CommandName => "openinventory";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new OpenInventoryCommand(playerId);
}

public sealed class CraftCommandParser : ICommandParser
{
    public string CommandName => "craft";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new CraftCommand(
            playerId,
            payload.TryGetString("recipeId") ?? string.Empty,
            payload.TryGetGuidList("componentIds"),
            payload.TryGetNullableGuid("taperId"));
}

public sealed class AcceptQuestCommandParser : ICommandParser
{
    public string CommandName => "acceptquest";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new AcceptQuestCommand(playerId, payload.TryGetString("questId") ?? string.Empty);
}

public sealed class CompleteQuestCommandParser : ICommandParser
{
    public string CommandName => "completequest";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new CompleteQuestCommand(
            playerId,
            payload.TryGetString("questId") ?? string.Empty,
            payload.TryGetString("chosenOutcome") ?? string.Empty);
}

public sealed class UsePortalCommandParser : ICommandParser
{
    public string CommandName => "useportal";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
    {
        // Preserves the original behaviour: unknown/missing destinationWorld → null
        // (dispatcher returns null, hub reports "Unknown command").
        if (Enum.TryParse<WorldId>(payload.TryGetString("destinationWorld"), out var world))
            return new UsePortalCommand(playerId, world);
        return null;
    }
}

public sealed class ManageBaseAssetCommandParser : ICommandParser
{
    public string CommandName => "managebaseasset";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new ManageBaseAssetCommand(
            playerId,
            payload.TryGetString("action") ?? string.Empty,
            payload.TryGetNullableGuid("assetId"));
}

public sealed class StartCombatCommandParser : ICommandParser
{
    public string CommandName => "combat start";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new StartCombatCommand(playerId, payload.TryGetGuid("zoneId"));
}

public sealed class UseCombatAbilityCommandParser : ICommandParser
{
    public string CommandName => "combat use";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new UseCombatAbilityCommand(
            playerId,
            payload.TryGetGuid("encounterId"),
            payload.TryGetString("abilityName") ?? string.Empty,
            payload.TryGetNullableGuid("targetId"));
}

public sealed class FleeCombatCommandParser : ICommandParser
{
    public string CommandName => "combat flee";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new FleeCombatCommand(playerId, payload.TryGetGuid("encounterId"));
}

public sealed class GetAvailableQuestsCommandParser : ICommandParser
{
    public string CommandName => "getquests";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new GetAvailableQuestsCommand(playerId);
}

public sealed class EnterZoneCommandParser : ICommandParser
{
    public string CommandName => "enterzone";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new EnterZoneCommand(playerId, payload.TryGetInt("worldId"), payload.TryGetGuid("zoneId"));
}

public sealed class PortalHomeCommandParser : ICommandParser
{
    public string CommandName => "portalhome";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new PortalHomeCommand(playerId);
}

public sealed class PortalBackCommandParser : ICommandParser
{
    public string CommandName => "portalback";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new PortalBackCommand(playerId);
}

public sealed class HarvestCommandParser : ICommandParser
{
    public string CommandName => "harvest";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new HarvestCommand(playerId);
}

public sealed class DepositCommandParser : ICommandParser
{
    public string CommandName => "deposit";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new DepositCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class WithdrawCommandParser : ICommandParser
{
    public string CommandName => "withdraw";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new WithdrawCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class OpenStorageCommandParser : ICommandParser
{
    public string CommandName => "openstorage";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new OpenStorageCommand(playerId);
}

public sealed class ExpandStorageCommandParser : ICommandParser
{
    public string CommandName => "expandstorage";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new ExpandStorageCommand(playerId);
}

public sealed class AutoFarmCommandParser : ICommandParser
{
    public string CommandName => "autofarm";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
    {
        var maxDanger = payload.TryGetInt("maxDanger");
        var priority = payload.TryGetString("priority");
        return new AutoFarmCommand(
            playerId,
            payload.TryGetNullableIntFromNested("targetZone", "x"),
            payload.TryGetNullableIntFromNested("targetZone", "y"),
            maxDanger > 0 ? maxDanger : 10,
            !string.IsNullOrEmpty(priority) ? priority : "balanced");
    }
}

public sealed class EquipCommandParser : ICommandParser
{
    public string CommandName => "equip";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new EquipCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class UnequipCommandParser : ICommandParser
{
    public string CommandName => "unequip";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new UnequipCommand(playerId, payload.TryGetString("slot") ?? "Weapon");
}

public sealed class SalvageCommandParser : ICommandParser
{
    public string CommandName => "salvage";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new SalvageCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class SalvageAllCommandParser : ICommandParser
{
    public string CommandName => "salvageall";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new SalvageAllCommand(playerId, payload.TryGetString("category") ?? "Weapon");
}

public sealed class SetAutoSalvageCommandParser : ICommandParser
{
    public string CommandName => "autosalvage";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new SetAutoSalvageCommand(
            playerId,
            payload.TryGetString("category") ?? "weapon",
            payload.TryGetInt("maxWorkmanship"));
}

public sealed class LockItemCommandParser : ICommandParser
{
    public string CommandName => "lockitem";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new LockItemCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class ViewCompanionsCommandParser : ICommandParser
{
    public string CommandName => "viewcompanions";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new ViewCompanionsCommand(playerId);
}

public sealed class ActivateCompanionCommandParser : ICommandParser
{
    public string CommandName => "activatecompanion";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new ActivateCompanionCommand(playerId, payload.TryGetGuid("companionId"));
}

public sealed class DeactivateCompanionCommandParser : ICommandParser
{
    public string CommandName => "deactivatecompanion";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new DeactivateCompanionCommand(playerId, payload.TryGetGuid("companionId"));
}

public sealed class ImbueCommandParser : ICommandParser
{
    public string CommandName => "imbue";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new ImbueCommand(playerId, payload.TryGetGuid("itemId"), payload.TryGetGuid("taperId"));
}

public sealed class AssignCompanionDutyCommandParser : ICommandParser
{
    public string CommandName => "assigncompanionduty";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new AssignCompanionDutyCommand(
            playerId,
            payload.TryGetGuid("companionId"),
            payload.TryGetString("duty") ?? string.Empty);
}

public sealed class RecallCompanionCommandParser : ICommandParser
{
    public string CommandName => "recallcompanion";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new RecallCompanionCommand(playerId, payload.TryGetGuid("companionId"));
}

public sealed class QueueSalvageCommandParser : ICommandParser
{
    public string CommandName => "queuesalvage";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new QueueSalvageCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class ViewRecipesCommandParser : ICommandParser
{
    public string CommandName => "viewrecipes";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new ViewRecipesCommand(playerId);
}

public sealed class UseConsumableCommandParser : ICommandParser
{
    public string CommandName => "useconsumable";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new UseConsumableCommand(playerId, payload.TryGetGuid("itemId"));
}

public sealed class InteractQuestCommandParser : ICommandParser
{
    public string CommandName => "interactquest";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new InteractQuestCommand(playerId, payload.TryGetString("questId") ?? string.Empty);
}

public sealed class SmeltCommandParser : ICommandParser
{
    public string CommandName => "smelt";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
    {
        var amount = payload.TryGetInt("amount");
        return new SmeltCommand(playerId, amount > 0 ? amount : 1);
    }
}

public sealed class ToggleCompanionAutoRotateCommandParser : ICommandParser
{
    public string CommandName => "toggleautorotate";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new ToggleCompanionAutoRotateCommand(playerId);
}

public sealed class PlaceBuildingCommandParser : ICommandParser
{
    public string CommandName => "placebuilding";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new PlaceBuildingCommand(
            playerId,
            payload.TryGetString("buildingType") ?? string.Empty,
            payload.TryGetInt("gridX"),
            payload.TryGetInt("gridY"));
}

public sealed class AssignBuilderCommandParser : ICommandParser
{
    public string CommandName => "assignbuilder";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new AssignBuilderCommand(
            playerId,
            payload.TryGetGuid("companionId"),
            payload.TryGetGuid("buildingId"));
}

public sealed class UnassignBuilderCommandParser : ICommandParser
{
    public string CommandName => "unassignbuilder";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
        => new UnassignBuilderCommand(playerId, payload.TryGetGuid("buildingId"));
}

public sealed class ViewCityCommandParser : ICommandParser
{
    public string CommandName => "viewcity";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new ViewCityCommand(playerId);
}

public sealed class BuildStaffEverythingCommandParser : ICommandParser
{
    public string CommandName => "buildstaffeverything";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new BuildStaffEverythingCommand(playerId);
}

public sealed class PracticeEnchantingCommandParser : ICommandParser
{
    public string CommandName => "practiceenchanting";
    public IGameCommand? Parse(JsonElement payload, Guid playerId) => new PracticeEnchantingCommand(playerId);
}

/// <summary>
/// Auto-progression meta-mode. Single command name "autoprogression" with an
/// "action" payload field (start / stop / status) — matches the client's
/// typed <c>autoProgression(action)</c> surface.
/// </summary>
public sealed class AutoProgressionCommandParser : ICommandParser
{
    public string CommandName => "autoprogression";
    public IGameCommand? Parse(JsonElement payload, Guid playerId)
    {
        var action = (payload.TryGetString("action") ?? "start").ToLowerInvariant();
        return action switch
        {
            "stop"   => new AutoProgressionStopCommand(playerId),
            "status" => new AutoProgressionStatusCommand(playerId),
            _        => new AutoProgressionStartCommand(playerId),
        };
    }
}
