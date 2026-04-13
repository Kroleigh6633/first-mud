using FirstMud.Engine.Events;

namespace FirstMud.Application.Events;

/// <summary>
/// Published when any item held by a player (or in their homestead storage) is consumed or reduced.
/// Subscribers: InventoryRefreshOrchestrator, StorageRefreshOrchestrator.
/// </summary>
/// <param name="ItemId">Item that was consumed (may already be deleted by the time subscribers run).</param>
/// <param name="Quantity">Number of units consumed.</param>
/// <param name="FromStorage">True if the item lived in homestead storage (OwnerId == null).</param>
/// <param name="HomesteadId">Set when FromStorage is true so the storage orchestrator can resolve scope without extra queries.</param>
public record ItemConsumedEvent(
    Guid ItemId,
    int Quantity,
    bool FromStorage,
    Guid? HomesteadId = null) : IIntegrationEvent;

/// <summary>
/// Published when an item is added to a player's inventory (crafted, looted, withdrawn, etc.).
/// Subscribers: InventoryRefreshOrchestrator.
/// </summary>
public record ItemAddedToInventoryEvent(Guid ItemId) : IIntegrationEvent;

/// <summary>
/// Published when homestead storage contents change (item added, removed, or quantity updated)
/// and the change did not originate from a DepositCommand/WithdrawCommand (those already broadcast).
/// Subscribers: StorageRefreshOrchestrator.
/// </summary>
public record StorageChangedEvent(Guid HomesteadId) : IIntegrationEvent;

/// <summary>
/// Published when a companion's lifecycle or roster state changes (activate, deactivate, duty assigned,
/// usage counter advanced, layer unlocked, etc.).
/// Subscribers: CompanionListOrchestrator, CityRefreshOrchestrator.
/// </summary>
public record CompanionStateChangedEvent(Guid CompanionId, string Reason) : IIntegrationEvent;

/// <summary>
/// Published when city/building state changes in ways that affect the CityView panel
/// (building placement, construction progress, worker auto-assignment).
/// Subscribers: CityRefreshOrchestrator.
/// </summary>
public record CityStateChangedEvent(string Reason) : IIntegrationEvent;

/// <summary>
/// Published when a player equips or unequips an item. The item itself still exists in their
/// inventory row, but panels that derive availability from equipped-state (Crafting ingredient
/// counts, inventory equipped markers) need a refresh.
/// Subscribers: InventoryRefreshOrchestrator.
/// </summary>
public record EquipmentChangedEvent(Guid? EquippedItemId, Guid? UnequippedItemId) : IIntegrationEvent;
