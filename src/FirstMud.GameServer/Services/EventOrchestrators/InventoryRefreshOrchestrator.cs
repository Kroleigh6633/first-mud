using FirstMud.Application.Events;
using FirstMud.GameServer.Services.Snapshots;

namespace FirstMud.GameServer.Services.EventOrchestrators;

/// <summary>
/// Re-broadcasts the Inventory snapshot whenever an item change could alter what the player
/// carries. Subscribes to:
///   - ItemConsumedEvent (when FromStorage == false — i.e., consumed from the player's bag)
///   - ItemAddedToInventoryEvent
/// Consuming from storage does NOT trigger this orchestrator — StorageRefreshOrchestrator handles that.
/// </summary>
public class InventoryRefreshOrchestrator(InventorySnapshotService snapshots)
    : IGameEventSubscriber<ItemConsumedEvent>,
      IGameEventSubscriber<ItemAddedToInventoryEvent>,
      IGameEventSubscriber<EquipmentChangedEvent>
{
    public Task HandleAsync(Guid playerId, ItemConsumedEvent @event, CancellationToken ct)
        => @event.FromStorage ? Task.CompletedTask : snapshots.BroadcastAsync(playerId, ct);

    public Task HandleAsync(Guid playerId, ItemAddedToInventoryEvent @event, CancellationToken ct)
        => snapshots.BroadcastAsync(playerId, ct);

    public Task HandleAsync(Guid playerId, EquipmentChangedEvent @event, CancellationToken ct)
        => snapshots.BroadcastAsync(playerId, ct);
}
