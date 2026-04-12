using FirstMud.Application.Services;
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
            .Select(c => new CompanionDto(
                c.Id,
                c.Name,
                c.Type.ToString(),
                c.Element.ToString(),
                c.Level,
                c.CurrentLayer,
                c.UsageCounter,
                c.DriftAccumulator,
                c.IsActive,
                c.RelationshipDepth))
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

        if (!player.TryAddActiveCompanion(companion.Id))
        {
            await notificationService.SendMessageAsync(cmd.PlayerId, "system",
                "You already have 3 active companions. Deactivate one first.", ct);
            return new CommandResult(false, "Active companion slots full (max 3).");
        }

        companion.SetActive(true);

        await playerRepository.UpdateAsync(player, ct);
        await companionRepository.UpdateAsync(companion, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"{companion.Name} joins your active party. (Layer {companion.CurrentLayer} {companion.Type})", ct);

        // Broadcast updated companion list
        var allCompanions = await companionRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        var payload = allCompanions
            .Where(c => !c.IsPermanentlyGone)
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenByDescending(c => c.CurrentLayer)
            .Select(c => new CompanionDto(
                c.Id, c.Name, c.Type.ToString(), c.Element.ToString(),
                c.Level, c.CurrentLayer, c.UsageCounter, c.DriftAccumulator,
                c.IsActive, c.RelationshipDepth))
            .ToList();

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CompanionList", payload, ct);

        return new CommandResult(true, $"{companion.Name} activated.");
    }
}

public class DeactivateCompanionCommandHandler(
    IPlayerRepository playerRepository,
    ICompanionRepository companionRepository,
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

        await notificationService.SendMessageAsync(cmd.PlayerId, "system",
            $"{companion.Name} returns to the roster. (Remember: unused companions drift!)", ct);

        // Broadcast updated companion list
        var allCompanions = await companionRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        var payload = allCompanions
            .Where(c => !c.IsPermanentlyGone)
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenByDescending(c => c.CurrentLayer)
            .Select(c => new CompanionDto(
                c.Id, c.Name, c.Type.ToString(), c.Element.ToString(),
                c.Level, c.CurrentLayer, c.UsageCounter, c.DriftAccumulator,
                c.IsActive, c.RelationshipDepth))
            .ToList();

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CompanionList", payload, ct);

        return new CommandResult(true, $"{companion.Name} deactivated.");
    }
}

/// <summary>
/// DTO for broadcasting companion state to the client.
/// </summary>
public record CompanionDto(
    Guid Id,
    string Name,
    string Type,
    string Element,
    int Level,
    int CurrentLayer,
    int UsageCounter,
    float DriftAccumulator,
    bool IsActive,
    int RelationshipDepth);
