using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Hubs;

public class GameHub : Hub
{
    private static readonly Dictionary<string, Guid> ConnectionPlayerMap = new();

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        ConnectionPlayerMap.Remove(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Authenticate(Guid playerId)
    {
        ConnectionPlayerMap[Context.ConnectionId] = playerId;
        await Groups.AddToGroupAsync(Context.ConnectionId, playerId.ToString());
        await Clients.Caller.SendAsync("Authenticated", playerId);
    }

    public async Task SendCommand(string command, object? payload = null)
    {
        if (!ConnectionPlayerMap.TryGetValue(Context.ConnectionId, out var playerId))
        {
            await Clients.Caller.SendAsync("Error", "Not authenticated.");
            return;
        }

        // Commands are dispatched to the game loop — stub for now
        await Clients.Caller.SendAsync("CommandReceived", new { command, playerId });
    }

    public async Task RequestWorldState()
    {
        if (!ConnectionPlayerMap.TryGetValue(Context.ConnectionId, out var playerId))
        {
            await Clients.Caller.SendAsync("Error", "Not authenticated.");
            return;
        }

        // World state snapshot dispatched from game loop
        await Clients.Caller.SendAsync("WorldStateRequested", playerId);
    }
}
