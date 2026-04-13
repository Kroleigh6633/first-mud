using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Companion list, activate, and deactivate command handlers.
/// The companion roster is the primary strategic layer — players choose which 3 companions
/// to bring into combat; companions then act autonomously on their turns.
/// </summary>
public class ViewCompanionsCommandHandler(
    ICompanionRepository companionRepository,
    IHubContext<GameHub> hubContext) : ICommandHandler<ViewCompanionsCommand>
{
    public async Task<CommandResult> HandleAsync(ViewCompanionsCommand cmd, CancellationToken ct)
    {
        var companions = await companionRepository.GetByOwnerAsync(cmd.PlayerId, ct);

        var payload = companions
            .Where(c => !c.IsPermanentlyGone)
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenByDescending(c => c.CurrentLayer)
            .Select(c => CompanionDtoHelpers.ToDto(c))
            .ToList();

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CompanionList", payload, ct);

        return new CommandResult(true, $"Loaded {payload.Count} companion(s).", payload);
    }
}

public class ActivateCompanionCommandHandler(
    IPlayerRepository playerRepository,
    ICompanionRepository companionRepository,
    BuildingService buildingService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<ActivateCompanionCommand>
{
    public async Task<CommandResult> HandleAsync(ActivateCompanionCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var companion = await companionRepository.GetByIdAsync(cmd.CompanionId, ct);
        if (companion is null)
            return new CommandResult(false, "Companion not found.");

        if (companion.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "That companion does not belong to you.");

        if (companion.IsPermanentlyGone)
            return new CommandResult(false, $"{companion.Name} has left you permanently.");

        if (companion.IsActive)
            return new CommandResult(false, $"{companion.Name} is already active.");

        // Auto-recall from duty if needed (ATM model — just pick who you want)
        if (companion.AssignedDuty.HasValue && companion.AssignedDuty != HomesteadDuty.None)
        {
            companion.RecallFromHomestead();
            await buildingService.ClearCompanionFromBuildingsAsync(cmd.PlayerId, companion.Id, ct);
        }

        if (!player.TryAddActiveCompanion(companion.Id))
        {
            await notificationService.SendMessageAsync(cmd.PlayerId, "system",
                "You already have 3 active companions. Deactivate one first.", ct);
            return new CommandResult(false, "Active companion slots full (max 3).");
        }

        companion.SetActive(true);

        await playerRepository.UpdateAsync(player, ct);
        await companionRepository.UpdateAsync(companion, ct);

        // Backfill: auto-assign any remaining idle companions to city duties
        await buildingService.AutoAssignIdleCompanionsAsync(cmd.PlayerId, player.ActiveCompanionIds, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"{companion.Name} joins your active party. (Layer {companion.CurrentLayer} {companion.Type})", ct);

        await CompanionDtoHelpers.BroadcastCompanionListAsync(cmd.PlayerId, companionRepository, hubContext, ct);

        return new CommandResult(true, $"{companion.Name} activated.");
    }
}

public class DeactivateCompanionCommandHandler(
    IPlayerRepository playerRepository,
    ICompanionRepository companionRepository,
    BuildingService buildingService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<DeactivateCompanionCommand>
{
    public async Task<CommandResult> HandleAsync(DeactivateCompanionCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var companion = await companionRepository.GetByIdAsync(cmd.CompanionId, ct);
        if (companion is null)
            return new CommandResult(false, "Companion not found.");

        if (companion.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "That companion does not belong to you.");

        if (!companion.IsActive)
            return new CommandResult(false, $"{companion.Name} is not active.");

        player.RemoveActiveCompanion(companion.Id);
        companion.SetActive(false);

        await playerRepository.UpdateAsync(player, ct);
        await companionRepository.UpdateAsync(companion, ct);

        // Auto-assign the deactivated companion (and any other idle ones) to city duties
        await buildingService.AutoAssignIdleCompanionsAsync(cmd.PlayerId, player.ActiveCompanionIds, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"{companion.Name} returns to the roster and has been assigned to city duty.", ct);

        await CompanionDtoHelpers.BroadcastCompanionListAsync(cmd.PlayerId, companionRepository, hubContext, ct);

        return new CommandResult(true, $"{companion.Name} deactivated.");
    }
}

/// <summary>
/// Assigns a companion to homestead duty.
/// The companion must first be deactivated (removed from the adventuring party).
/// </summary>
public class AssignCompanionDutyCommandHandler(
    ICompanionRepository companionRepository,
    IHomesteadRepository homesteadRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<AssignCompanionDutyCommand>
{
    public async Task<CommandResult> HandleAsync(AssignCompanionDutyCommand cmd, CancellationToken ct)
    {
        if (!Enum.TryParse<HomesteadDuty>(cmd.Duty, ignoreCase: true, out var duty) || duty == HomesteadDuty.None)
            return new CommandResult(false, $"Unknown duty '{cmd.Duty}'. Choose: Harvester, Salvager, Guard, Crafter.");

        var companion = await companionRepository.GetByIdAsync(cmd.CompanionId, ct);
        if (companion is null)
            return new CommandResult(false, "Companion not found.");

        if (companion.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "That companion does not belong to you.");

        if (companion.IsPermanentlyGone)
            return new CommandResult(false, $"{companion.Name} is gone permanently.");

        if (companion.IsActive)
            return new CommandResult(false,
                $"{companion.Name} is still adventuring. Deactivate them first before assigning homestead duty.");

        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "You don't have a homestead yet.");

        try
        {
            companion.AssignToHomestead(duty);
        }
        catch (InvalidOperationException ex)
        {
            return new CommandResult(false, ex.Message);
        }

        await companionRepository.UpdateAsync(companion, ct);

        var aptitude = companion.GetAptitude(duty);
        var stars = new string('★', aptitude) + new string('☆', 3 - aptitude);

        var dutyMsg = duty switch
        {
            HomesteadDuty.Guard   => $"{companion.Name} stands watch over your homestead. [{stars}]",
            HomesteadDuty.Crafter => $"{companion.Name} heads to the crafting station... (awaiting crafting station construction). [{stars}]",
            _                     => $"{companion.Name} is now assigned as {duty}. [{stars}] Passive income begins."
        };

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", dutyMsg, ct);
        await CompanionDtoHelpers.BroadcastCompanionListAsync(cmd.PlayerId, companionRepository, hubContext, ct);

        return new CommandResult(true, dutyMsg);
    }
}

/// <summary>
/// Recalls a companion from homestead duty, allowing them to return to adventuring.
/// </summary>
public class RecallCompanionCommandHandler(
    ICompanionRepository companionRepository,
    BuildingService buildingService,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<RecallCompanionCommand>
{
    public async Task<CommandResult> HandleAsync(RecallCompanionCommand cmd, CancellationToken ct)
    {
        var companion = await companionRepository.GetByIdAsync(cmd.CompanionId, ct);
        if (companion is null)
            return new CommandResult(false, "Companion not found.");

        if (companion.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "That companion does not belong to you.");

        if (!companion.AssignedDuty.HasValue || companion.AssignedDuty == HomesteadDuty.None)
            return new CommandResult(false, $"{companion.Name} is not on homestead duty.");

        var prevDuty = companion.AssignedDuty.ToString();
        companion.RecallFromHomestead();
        await companionRepository.UpdateAsync(companion, ct);

        // Clear the building's AssignedCompanionId so it shows as unstaffed
        await buildingService.ClearCompanionFromBuildingsAsync(cmd.PlayerId, companion.Id, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"{companion.Name} has been recalled from {prevDuty} duty and is ready to adventure.", ct);

        await CompanionDtoHelpers.BroadcastCompanionListAsync(cmd.PlayerId, companionRepository, hubContext, ct);

        return new CommandResult(true, $"{companion.Name} recalled from homestead.");
    }
}

/// <summary>
/// Queues a player's inventory item for companion-assisted salvaging at the homestead.
/// The item stays in inventory; a Salvager companion will process it during the next duty tick.
/// </summary>
public class QueueSalvageCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    GameNotificationService notificationService) : ICommandHandler<QueueSalvageCommand>
{
    public async Task<CommandResult> HandleAsync(QueueSalvageCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "Item not found in your inventory.");

        if (item.IsLocked)
            return new CommandResult(false, $"{item.Name} is locked. Unlock it first.");

        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "You don't have a homestead yet.");

        homestead.EnqueueForSalvage(item.Id);
        await homesteadRepository.UpdateAsync(homestead, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"{item.Name} added to the homestead salvage queue. A Salvager companion will process it.", ct);

        return new CommandResult(true, $"{item.Name} queued for salvage.");
    }
}

// ─── auto-rotate toggle handler ───────────────────────────────────────────────

public class ToggleCompanionAutoRotateCommandHandler(
    IPlayerRepository playerRepository,
    GameNotificationService notificationService) : ICommandHandler<ToggleCompanionAutoRotateCommand>
{
    public async Task<CommandResult> HandleAsync(ToggleCompanionAutoRotateCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var newValue = player.ToggleAutoRotateMaxedCompanions();
        await playerRepository.UpdateAsync(player, ct);

        var state = newValue ? "enabled" : "disabled";
        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"Companion auto-rotation is now {state}. " +
            (newValue
                ? "Maxed companions will be swapped to homestead duty every 5 victories."
                : "You will manage your companion roster manually."),
            ct);

        return new CommandResult(true, $"Auto-rotate {state}.", new { AutoRotate = newValue });
    }
}

// ─── shared helpers ───────────────────────────────────────────────────────────

internal static class CompanionDtoHelpers
{
    public static CompanionDto ToDto(Companion c) => new(
        c.Id,
        c.Name,
        c.Type.ToString(),
        c.Element.ToString(),
        c.Level,
        c.CurrentLayer,
        c.UsageCounter,
        c.NextLayerThreshold,
        c.DriftAccumulator,
        c.IsActive,
        c.RelationshipDepth,
        c.AssignedDuty?.ToString(),
        c.DutyStartedAt);

    public static async Task BroadcastCompanionListAsync(
        Guid playerId,
        ICompanionRepository companionRepository,
        IHubContext<GameHub> hubContext,
        CancellationToken ct)
    {
        var allCompanions = await companionRepository.GetByOwnerAsync(playerId, ct);
        var payload = allCompanions
            .Where(c => !c.IsPermanentlyGone)
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenByDescending(c => c.CurrentLayer)
            .Select(c => CompanionDtoHelpers.ToDto(c))
            .ToList();

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("CompanionList", payload, ct);
    }
}

/// <summary>
/// DTO for broadcasting companion state to the client.
/// NextLayerThreshold is the UsageCounter value needed to advance to the next layer
/// (0 when already at max layer 6). The client uses this to render the progress bar.
/// </summary>
public record CompanionDto(
    Guid Id,
    string Name,
    string Type,
    string Element,
    int Level,
    int CurrentLayer,
    int UsageCounter,
    int NextLayerThreshold,
    float DriftAccumulator,
    bool IsActive,
    int RelationshipDepth,
    string? AssignedDuty = null,
    DateTime? DutyStartedAt = null);
