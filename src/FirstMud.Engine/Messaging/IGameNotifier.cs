namespace FirstMud.Engine.Messaging;

/// <summary>
/// Transport-agnostic abstraction for pushing messages and events to a specific player.
/// Implementations in game code bridge this to SignalR (or any other transport).
/// </summary>
public interface IGameNotifier
{
    Task SendMessageAsync(Guid playerId, string category, string text, CancellationToken ct = default);
    Task SendWorldStateAsync(Guid playerId, object snapshot, CancellationToken ct = default);
    Task SendEventAsync(Guid playerId, string eventName, object payload, CancellationToken ct = default);
}
