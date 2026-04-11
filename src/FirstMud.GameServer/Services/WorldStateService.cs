using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services;

public record TileDto(int X, int Y, string Symbol, string Name, bool IsPassable);

public record PlayerStateDto(
    Guid Id,
    string Name,
    int Level,
    int CurrentHp,
    int MaxHp,
    float WeavePercent,
    string WeaveState,
    int X,
    int Y,
    WorldId World,
    Dictionary<string, string> FactionTiers,
    List<string> ActiveCompanionIds,
    List<string> UnlockedPortals);

public record WorldStateSnapshot(
    PlayerStateDto Player,
    List<AiPlayerState> AiPlayers,
    string WorldName);

public class WorldStateService
{
    private readonly IPlayerRepository _playerRepository;
    private readonly AiPlayerService _aiPlayerService;

    private static readonly Dictionary<WorldId, string> WorldNames = new()
    {
        [WorldId.Aeldran]        = "Aeldran",
        [WorldId.ArdweldRemnant] = "Ardweld Remnant",
        [WorldId.FairgeanDeep]   = "Fairgean Deep",
        [WorldId.GolvariDeeps]   = "Golvari Deeps",
        [WorldId.WyrdPaths]      = "Wyrd Paths",
        [WorldId.TheDream]       = "The Dream",
    };

    public WorldStateService(IPlayerRepository playerRepository, AiPlayerService aiPlayerService)
    {
        _playerRepository = playerRepository;
        _aiPlayerService = aiPlayerService;
    }

    public async Task<WorldStateSnapshot> GetSnapshotAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _playerRepository.GetByIdAsync(playerId, ct)
            ?? throw new InvalidOperationException($"Player {playerId} not found.");

        var factionTiers = player.Reputations.ToDictionary(
            r => r.FactionId.ToString(),
            r => r.Score.Tier.ToString());

        var activeCompanionIds = player.ActiveCompanionIds
            .Select(id => id.ToString())
            .ToList();

        var unlockedPortals = player.UnlockedPortals
            .Select(w => w.ToString())
            .ToList();

        var playerDto = new PlayerStateDto(
            player.Id,
            player.Name,
            player.Level,
            player.CurrentHp,
            player.MaxHp,
            player.Weave.Percentage,
            player.Weave.VisibleState.ToString(),
            player.Position.X,
            player.Position.Y,
            player.Position.World,
            factionTiers,
            activeCompanionIds,
            unlockedPortals);

        var aiStates = _aiPlayerService.GetAiStates().ToList();

        var worldName = WorldNames.TryGetValue(player.Position.World, out var name)
            ? name
            : player.Position.World.ToString();

        return new WorldStateSnapshot(playerDto, aiStates, worldName);
    }
}
