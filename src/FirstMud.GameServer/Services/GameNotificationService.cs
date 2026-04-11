using FirstMud.GameServer.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Services;

public class GameNotificationService(IHubContext<GameHub> hubContext)
{
    /// <summary>
    /// Send a colored game message to a player's SignalR group.
    /// Client receives a "GameMessage" event with { timestamp, category, text }.
    /// </summary>
    public Task SendMessageAsync(Guid playerId, string category, string text, CancellationToken ct = default)
    {
        var payload = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            category,
            text
        };

        return hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", payload, ct);
    }

    /// <summary>
    /// Broadcast an updated world-state snapshot to a player.
    /// Client receives a "WorldState" event with the snapshot object.
    /// </summary>
    public Task SendWorldStateAsync(Guid playerId, object snapshot, CancellationToken ct = default)
    {
        return hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("WorldState", snapshot, ct);
    }

    /// <summary>
    /// Broadcast a named event with an arbitrary payload to a player.
    /// Client receives an event named <paramref name="eventName"/> with <paramref name="payload"/>.
    /// </summary>
    public Task SendEventAsync(Guid playerId, string eventName, object payload, CancellationToken ct = default)
    {
        return hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync(eventName, payload, ct);
    }
}
