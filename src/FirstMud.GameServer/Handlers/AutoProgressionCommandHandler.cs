using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.Engine.Commands;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Start / Stop / Status for the auto-progression meta-mode.
///
/// Actual sub-mode dispatch runs in <c>AutoProgressionTickHandler</c> every 60s;
/// these handlers just manage the session record + send a banner.
/// See <c>docs/design/auto-progression-design.md</c>.
/// </summary>
public class AutoProgressionStartCommandHandler(
    AutoProgressionService service,
    AutoFarmService autoFarm,
    IPlayerRepository players,
    GameNotificationService notifier) : ICommandHandler<AutoProgressionStartCommand>
{
    public async Task<CommandResult> HandleAsync(AutoProgressionStartCommand cmd, CancellationToken ct)
    {
        var player = await players.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null) return new CommandResult(false, "Player not found.");

        // Only one auto-* session at a time — progression subsumes any active farm.
        if (autoFarm.IsActive(cmd.PlayerId))
            autoFarm.EndSession(cmd.PlayerId);

        if (service.IsActive(cmd.PlayerId))
        {
            await notifier.SendMessageAsync(cmd.PlayerId, "system", "Auto-progression already active.", ct);
            return new CommandResult(true, "Already active.");
        }

        service.StartSession(cmd.PlayerId);
        await notifier.SendMessageAsync(cmd.PlayerId, "system",
            "Auto-progression started. Party will pursue gear / imbue / companion gains until d10 ≥ 50% survival. Use [progression stop] to cancel.", ct);
        await notifier.SendEventAsync(cmd.PlayerId, "AutoProgressionStatus", new { active = true }, ct);
        return new CommandResult(true, "Auto-progression started.");
    }
}

public class AutoProgressionStopCommandHandler(
    AutoProgressionService service,
    GameNotificationService notifier) : ICommandHandler<AutoProgressionStopCommand>
{
    public async Task<CommandResult> HandleAsync(AutoProgressionStopCommand cmd, CancellationToken ct)
    {
        if (!service.IsActive(cmd.PlayerId))
        {
            await notifier.SendMessageAsync(cmd.PlayerId, "system", "Auto-progression is not active.", ct);
            return new CommandResult(true, "Not active.");
        }
        service.EndSession(cmd.PlayerId);
        await notifier.SendMessageAsync(cmd.PlayerId, "system", "Auto-progression stopped.", ct);
        await notifier.SendEventAsync(cmd.PlayerId, "AutoProgressionStatus", new { active = false }, ct);
        return new CommandResult(true, "Auto-progression stopped.");
    }
}

public class AutoProgressionStatusCommandHandler(
    AutoProgressionService service,
    GameNotificationService notifier) : ICommandHandler<AutoProgressionStatusCommand>
{
    public async Task<CommandResult> HandleAsync(AutoProgressionStatusCommand cmd, CancellationToken ct)
    {
        var session = service.GetSession(cmd.PlayerId);
        if (session is null)
        {
            await notifier.SendEventAsync(cmd.PlayerId, "AutoProgressionStatus", new { active = false }, ct);
            return new CommandResult(true, "Inactive.");
        }

        // Live evaluation so status reflects current binding constraint.
        var step = await service.EvaluateAsync(cmd.PlayerId, ct);
        await notifier.SendEventAsync(cmd.PlayerId, "AutoProgressionStatus", new
        {
            active = true,
            mode = step.Mode.ToString(),
            bindingConstraint = step.BindingConstraint.ToString(),
            reliability = step.Reliability,
            deficits = step.DeficitScores.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            reason = step.Reason,
            dispatched = step.DispatchedActual,
            startedAt = session.StartedAt,
        }, ct);
        await notifier.SendMessageAsync(cmd.PlayerId, "system",
            $"Auto-progression: {step.Mode} ({step.BindingConstraint}); d10 reliability {step.Reliability:P0}. {step.Reason}", ct);
        return new CommandResult(true, "Status reported.");
    }
}
