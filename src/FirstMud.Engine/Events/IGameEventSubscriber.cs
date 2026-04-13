namespace FirstMud.Engine.Events;

public interface IGameEventSubscriber<TEvent> where TEvent : IIntegrationEvent
{
    Task HandleAsync(Guid playerId, TEvent @event, CancellationToken ct);
}
