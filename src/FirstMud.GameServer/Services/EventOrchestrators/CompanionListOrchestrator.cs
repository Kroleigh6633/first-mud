using FirstMud.Application.Events;
using FirstMud.Engine.Events;
using FirstMud.GameServer.Services.Snapshots;

namespace FirstMud.GameServer.Services.EventOrchestrators;

/// <summary>
/// Re-broadcasts CompanionList whenever any companion transitions state — active/inactive,
/// duty assigned/cleared, usage counter advanced, layer unlocked.
/// </summary>
public class CompanionListOrchestrator(CompanionListSnapshotService snapshots)
    : IGameEventSubscriber<CompanionStateChangedEvent>
{
    public Task HandleAsync(Guid playerId, CompanionStateChangedEvent @event, CancellationToken ct)
        => snapshots.BroadcastAsync(playerId, ct);
}
