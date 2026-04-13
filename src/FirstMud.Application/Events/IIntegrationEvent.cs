namespace FirstMud.Application.Events;

/// <summary>
/// Marker for cross-cutting integration events published to the in-process event bus
/// (GameEventPublisher) so orchestrators can fan out refreshes to multiple UI surfaces.
/// Distinct from Domain.Events.IDomainEvent, which represents events raised by aggregates
/// (and not yet dispatched anywhere).
/// </summary>
public interface IIntegrationEvent { }
