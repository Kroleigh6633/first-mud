using FirstMud.Application.Events;
using FirstMud.Engine.Events;
using FirstMud.GameServer.Services.Snapshots;

namespace FirstMud.GameServer.Services.EventOrchestrators;

/// <summary>
/// Re-broadcasts CityView whenever companion or city state shifts in ways that affect
/// the building panel (worker assignments, construction progress, placement).
/// </summary>
public class CityRefreshOrchestrator(CitySnapshotService snapshots)
    : IGameEventSubscriber<CompanionStateChangedEvent>,
      IGameEventSubscriber<CityStateChangedEvent>
{
    public Task HandleAsync(Guid playerId, CompanionStateChangedEvent @event, CancellationToken ct)
        => snapshots.BroadcastAsync(playerId, ct);

    public Task HandleAsync(Guid playerId, CityStateChangedEvent @event, CancellationToken ct)
        => snapshots.BroadcastAsync(playerId, ct);
}
