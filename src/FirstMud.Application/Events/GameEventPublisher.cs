using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Events;

/// <summary>
/// Default in-process event bus. Resolves all IGameEventSubscriber&lt;TEvent&gt; from DI and
/// invokes them sequentially so orchestrators see a stable world-state between handlers.
/// Subscriber failures are logged and do not abort sibling subscribers.
/// </summary>
public class GameEventPublisher(IServiceProvider services, ILogger<GameEventPublisher> logger) : IGameEventPublisher
{
    public async Task PublishAsync<TEvent>(Guid playerId, TEvent @event, CancellationToken ct = default)
        where TEvent : IIntegrationEvent
    {
        var subscribers = services.GetServices<IGameEventSubscriber<TEvent>>().ToList();
        if (subscribers.Count == 0)
        {
            logger.LogDebug("No subscribers registered for {EventType}", typeof(TEvent).Name);
            return;
        }

        foreach (var subscriber in subscribers)
        {
            try
            {
                await subscriber.HandleAsync(playerId, @event, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Subscriber {Subscriber} failed handling {EventType} for player {PlayerId}",
                    subscriber.GetType().Name, typeof(TEvent).Name, playerId);
            }
        }
    }
}
