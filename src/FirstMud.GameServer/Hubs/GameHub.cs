using Microsoft.AspNetCore.SignalR;
using FirstMud.Engine.Tick;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Hubs;

public class GameHub : Hub
{
    private static readonly Dictionary<string, Guid> ConnectionPlayerMap = new();

    private readonly GameLoopService _gameLoop;
    private readonly WorldStateService _worldStateService;
    private readonly IPlayerRepository _playerRepository;
    private readonly GameServerCommandFactory _commandFactory;

    public GameHub(
        GameLoopService gameLoop,
        WorldStateService worldStateService,
        IPlayerRepository playerRepository,
        GameServerCommandFactory commandFactory)
    {
        _gameLoop = gameLoop;
        _worldStateService = worldStateService;
        _playerRepository = playerRepository;
        _commandFactory = commandFactory;
    }

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

        // Stamp LastSeenAt so the player stays within the 24-hour active-player
        // window used by background ticks (homestead healing, weave regen, etc.).
        await _playerRepository.TouchLastSeenAsync(playerId, Context.ConnectionAborted);

        // Push initial world-state snapshot so the status panel populates immediately
        try
        {
            var snapshot = await _worldStateService.GetSnapshotAsync(playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("WorldState", snapshot);
        }
        catch (InvalidOperationException ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }

        // Immediately seed the client with available quests, current zone view, equipment, and companion roster
        _gameLoop.EnqueueCommand(new GetAvailableQuestsCommand(playerId));
        _gameLoop.EnqueueCommand(new EnterZoneCommand(playerId, 0, Guid.Empty));
        _gameLoop.EnqueueCommand(new OpenInventoryCommand(playerId));
        _gameLoop.EnqueueCommand(new ViewCompanionsCommand(playerId));

        // If the player is already at homestead when they connect, send CityView so buildings render immediately
        var connectingPlayer = await _playerRepository.GetByIdAsync(playerId, Context.ConnectionAborted);
        if (connectingPlayer is not null
            && connectingPlayer.Position.X == -100
            && connectingPlayer.Position.Y == -100)
        {
            _gameLoop.EnqueueCommand(new ViewCityCommand(playerId));
            await Clients.Caller.SendAsync("AtHomestead", cancellationToken: Context.ConnectionAborted);
        }
    }

    public async Task SendCommand(string command, object? payload)
    {
        if (!ConnectionPlayerMap.TryGetValue(Context.ConnectionId, out var playerId))
        {
            await Clients.Caller.SendAsync("Error", "Not authenticated.");
            return;
        }

        var cmd = _commandFactory.TryParse(command, playerId, payload);
        if (cmd is null)
        {
            await Clients.Caller.SendAsync("Error", $"Unknown command: {command}");
            return;
        }

        _gameLoop.EnqueueCommand(cmd);
        await Clients.Caller.SendAsync("CommandReceived", new { command, playerId });
    }

    public async Task RequestWorldState()
    {
        if (!ConnectionPlayerMap.TryGetValue(Context.ConnectionId, out var playerId))
        {
            await Clients.Caller.SendAsync("Error", "Not authenticated.");
            return;
        }

        try
        {
            var snapshot = await _worldStateService.GetSnapshotAsync(playerId, Context.ConnectionAborted);
            await Clients.Caller.SendAsync("WorldState", snapshot);
        }
        catch (InvalidOperationException ex)
        {
            await Clients.Caller.SendAsync("Error", ex.Message);
        }
    }
}
