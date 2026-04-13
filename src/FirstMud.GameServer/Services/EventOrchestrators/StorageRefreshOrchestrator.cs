using FirstMud.Application.Events;
using FirstMud.GameServer.Services.Snapshots;

namespace FirstMud.GameServer.Services.EventOrchestrators;

/// <summary>
/// Re-broadcasts the StorageView snapshot whenever homestead storage contents change.
/// Subscribes to:
///   - ItemConsumedEvent (when FromStorage == true)
///   - StorageChangedEvent (items added by background ticks, harvester/salvager duties, etc.)
/// </summary>
public class StorageRefreshOrchestrator(StorageSnapshotService snapshots)
    : IGameEventSubscriber<ItemConsumedEvent>,
      IGameEventSubscriber<StorageChangedEvent>
{
    public Task HandleAsync(Guid playerId, ItemConsumedEvent @event, CancellationToken ct)
        => @event.FromStorage ? snapshots.BroadcastAsync(playerId, ct) : Task.CompletedTask;

    public Task HandleAsync(Guid playerId, StorageChangedEvent @event, CancellationToken ct)
        => snapshots.BroadcastAsync(playerId, ct);
}
