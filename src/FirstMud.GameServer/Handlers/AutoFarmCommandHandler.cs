using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Thin entry/exit toggler for auto-farm.
/// All loop logic lives in <see cref="FarmingOrchestrator"/>.
/// </summary>
public class AutoFarmCommandHandler(
    IPlayerRepository playerRepository,
    AutoFarmService autoFarmService,
    GameNotificationService notificationService,
    FarmingOrchestrator farmingOrchestrator) : ICommandHandler<AutoFarmCommand>
{
    public async Task<CommandResult> HandleAsync(AutoFarmCommand cmd, CancellationToken ct)
    {
        // Toggle: if already active, cancel the session
        if (autoFarmService.IsActive(cmd.PlayerId))
        {
            autoFarmService.EndSession(cmd.PlayerId);
            await notificationService.SendMessageAsync(cmd.PlayerId, "system", "Auto-farm cancelled.", ct);
            await notificationService.SendEventAsync(cmd.PlayerId, "AutoFarmStatus", new { active = false }, ct);
            return new CommandResult(true, "Auto-farm cancelled.");
        }

        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var session = autoFarmService.StartSession(
            cmd.PlayerId,
            cmd.TargetX,
            cmd.TargetY,
            cmd.MaxDanger,
            cmd.Priority);

        var settingsDesc = cmd.Priority != "balanced" || cmd.TargetX.HasValue
            ? $" [{cmd.Priority}{(cmd.TargetX.HasValue ? $", heading to ({cmd.TargetX},{cmd.TargetY})" : "")}]"
            : string.Empty;

        await notificationService.SendMessageAsync(
            cmd.PlayerId,
            "system",
            $"Auto-farm started{settingsDesc}. Runs until stopped. Press [F] again to stop.",
            ct);

        await notificationService.SendEventAsync(cmd.PlayerId, "AutoFarmStatus",
            new
            {
                active    = true,
                state     = "idle",
                kills     = 0,
                items     = 0,
                salvaged  = 0,
                deposited = 0
            }, ct);

        farmingOrchestrator.StartLoop(cmd.PlayerId, session, ct);

        return new CommandResult(true, "Auto-farm started.");
    }
}
