using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services;

public record TileDto(int X, int Y, string Symbol, string Name, bool IsPassable);

public record PlayerStateDto(
    Guid Id,
    string Name,
    int Level,
    int Experience,
    int CurrentHp,
    int MaxHp,
    int ActionPoints,
    int MaxActionPoints,
    float WeavePercent,
    string WeaveState,
    int X,
    int Y,
    WorldId World,
    int Strength,
    int Agility,
    int Intellect,
    int Fortitude,
    int Speed,
    int CraftingSkill,
    int SalvageSkill,
    string PrimaryElement,
    string Polarity,
    bool ElementRevealed,
    bool PolarityRevealed,
    int WyrdTangle,
    Dictionary<string, string> FactionTiers,
    List<string> ActiveCompanionIds,
    List<string> UnlockedPortals,
    List<string> CurrentQuestIds);

public record WorldStateSnapshot(
    PlayerStateDto Player,
    List<AiPlayerState> AiPlayers,
    string WorldName);

public class WorldStateService
{
    private readonly IPlayerRepository _playerRepository;
    private readonly IQuestGraphRepository _questGraphRepository;
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

    public WorldStateService(
        IPlayerRepository playerRepository,
        IQuestGraphRepository questGraphRepository,
        AiPlayerService aiPlayerService)
    {
        _playerRepository = playerRepository;
        _questGraphRepository = questGraphRepository;
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

        // Fetch quests currently in progress for this player
        var availableQuests = await _questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
        var currentQuestIds = availableQuests
            .Where(q => q.IsTaken)
            .Select(q => q.QuestId)
            .ToList();

        var playerDto = new PlayerStateDto(
            player.Id,
            player.Name,
            player.Level,
            player.Experience,
            player.CurrentHp,
            player.MaxHp,
            player.ActionPoints,
            player.MaxActionPoints,
            player.Weave.Percentage,
            player.Weave.VisibleState.ToString(),
            player.Position.X,
            player.Position.Y,
            player.Position.World,
            player.Strength,
            player.Agility,
            player.Intellect,
            player.Fortitude,
            player.Speed,
            player.CraftingSkill,
            player.SalvageSkill,
            player.PrimaryElement.ToString(),
            player.Polarity.ToString(),
            player.ElementRevealed,
            player.PolarityRevealed,
            player.WyrdTangle,
            factionTiers,
            activeCompanionIds,
            unlockedPortals,
            currentQuestIds);

        var aiStates = _aiPlayerService.GetAiStates().ToList();

        var worldName = WorldNames.TryGetValue(player.Position.World, out var name)
            ? name
            : player.Position.World.ToString();

        return new WorldStateSnapshot(playerDto, aiStates, worldName);
    }

    public async Task<IReadOnlyList<QuestNode>> GetAvailableQuestsAsync(Guid playerId, CancellationToken ct = default)
    {
        return await _questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
    }
}
