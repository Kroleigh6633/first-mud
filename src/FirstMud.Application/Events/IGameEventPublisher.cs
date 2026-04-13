namespace FirstMud.Application.Events;

public interface IGameEventPublisher
{
    Task PublishAsync<TEvent>(Guid playerId, TEvent @event, CancellationToken ct = default)
        where TEvent : IIntegrationEvent;
}
