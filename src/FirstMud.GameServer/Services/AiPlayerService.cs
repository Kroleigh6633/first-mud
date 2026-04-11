using System.Collections.Concurrent;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services;

public enum AiState { Idle, Traveling, Questing, Resting, Dead }

public record AiPlayerState(
    Guid Id,
    string Name,
    string CurrentFaction,
    string State,
    string? CurrentQuestId,
    int TotalQuestsCompleted);

internal sealed class AiPlayerData
{
    public Guid Id { get; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public FactionId PrimaryFaction { get; init; }
    public Dictionary<FactionId, int> Reputation { get; } = new();
    public AiState State { get; set; } = AiState.Idle;
    public string? CurrentQuestId { get; set; }
    public int TotalQuestsCompleted { get; set; }
    public int TravelTicksRemaining { get; set; }
    public int QuestTicksRemaining { get; set; }
    public int DeadTicksRemaining { get; set; }
}

public class AiPlayerService
{
    private readonly List<AiPlayerData> _aiPlayers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AiPlayerService> _logger;
    private readonly Random _rng = new();

    // Tick rate: 10 ticks/sec, so ticks per second = 10
    private const int TicksPerSecond = 10;

    public AiPlayerService(IServiceScopeFactory scopeFactory, ILogger<AiPlayerService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        _aiPlayers = new List<AiPlayerData>
        {
            BuildAiPlayer("Kira Dawnmere",  FactionId.ThornwoodCovens, FactionId.ThornwoodCovens, 200),
            BuildAiPlayer("Osric Vane",     FactionId.Gravenguard,     FactionId.Gravenguard,     200),
            BuildAiPlayer("Sable Ashford",  FactionId.EmeraldCompact,  FactionId.EmeraldCompact,  200),
        };

        _logger.LogInformation("AiPlayerService initialized with {Count} AI players.", _aiPlayers.Count);
    }

    private static AiPlayerData BuildAiPlayer(string name, FactionId primary, FactionId startingFaction, int startingRep)
    {
        var ai = new AiPlayerData { Name = name, PrimaryFaction = primary };

        foreach (FactionId factionId in Enum.GetValues<FactionId>())
            ai.Reputation[factionId] = 0;

        ai.Reputation[startingFaction] = startingRep;
        return ai;
    }

    public IReadOnlyList<AiPlayerState> GetAiStates() =>
        _aiPlayers.Select(ai => new AiPlayerState(
            ai.Id,
            ai.Name,
            ai.PrimaryFaction.ToString(),
            ai.State.ToString(),
            ai.CurrentQuestId,
            ai.TotalQuestsCompleted)).ToList().AsReadOnly();

    public async Task TickAsync(CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var questRepo = scope.ServiceProvider.GetRequiredService<IQuestGraphRepository>();

        foreach (var ai in _aiPlayers)
        {
            try
            {
                await ProcessAiPlayerAsync(ai, questRepo, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing AI tick for {AiName}", ai.Name);
            }
        }
    }

    private async Task ProcessAiPlayerAsync(
        AiPlayerData ai,
        IQuestGraphRepository questRepo,
        CancellationToken ct)
    {
        switch (ai.State)
        {
            case AiState.Idle:
                await HandleIdleAsync(ai, questRepo, ct);
                break;

            case AiState.Traveling:
                HandleTraveling(ai);
                break;

            case AiState.Questing:
                await HandleQuestingAsync(ai, questRepo, ct);
                break;

            case AiState.Resting:
                // Resting transitions back to Idle after a brief pause — treated same as Idle
                ai.State = AiState.Idle;
                break;

            case AiState.Dead:
                HandleDead(ai);
                break;
        }
    }

    private async Task HandleIdleAsync(
        AiPlayerData ai,
        IQuestGraphRepository questRepo,
        CancellationToken ct)
    {
        var quests = await questRepo.GetAvailableQuestsAsync(Guid.Empty, ai.PrimaryFaction, ct);
        if (quests.Count == 0)
        {
            _logger.LogDebug("AI {Name} is idle — no quests available for faction {Faction}.",
                ai.Name, ai.PrimaryFaction);
            return;
        }

        var quest = quests[_rng.Next(quests.Count)];
        ai.CurrentQuestId = quest.QuestId;

        // Travel: 30–120 seconds
        ai.TravelTicksRemaining = _rng.Next(30 * TicksPerSecond, 120 * TicksPerSecond + 1);
        ai.State = AiState.Traveling;

        _logger.LogDebug("AI {Name} picked quest '{QuestId}' and is now Traveling ({Ticks} ticks).",
            ai.Name, quest.QuestId, ai.TravelTicksRemaining);
    }

    private void HandleTraveling(AiPlayerData ai)
    {
        ai.TravelTicksRemaining--;

        if (ai.TravelTicksRemaining <= 0)
        {
            // Quest: 60–300 seconds
            ai.QuestTicksRemaining = _rng.Next(60 * TicksPerSecond, 300 * TicksPerSecond + 1);
            ai.State = AiState.Questing;

            _logger.LogDebug("AI {Name} arrived and is now Questing '{QuestId}' ({Ticks} ticks).",
                ai.Name, ai.CurrentQuestId, ai.QuestTicksRemaining);
        }
    }

    private async Task HandleQuestingAsync(
        AiPlayerData ai,
        IQuestGraphRepository questRepo,
        CancellationToken ct)
    {
        ai.QuestTicksRemaining--;

        if (ai.QuestTicksRemaining > 0) return;

        // Quest complete — pick a random outcome
        if (ai.CurrentQuestId is not null)
        {
            var questNode = await questRepo.GetQuestAsync(ai.CurrentQuestId, ct);

            if (questNode is not null && questNode.PossibleOutcomes.Length > 0)
            {
                var outcome = questNode.PossibleOutcomes[_rng.Next(questNode.PossibleOutcomes.Length)];

                // Apply reputation reward
                ai.Reputation[questNode.FactionId] =
                    ai.Reputation.GetValueOrDefault(questNode.FactionId) + questNode.ReputationReward;

                _logger.LogDebug("AI {Name} completed quest '{QuestId}' with outcome '{Outcome}', +{Rep} rep with {Faction}.",
                    ai.Name, ai.CurrentQuestId, outcome, questNode.ReputationReward, questNode.FactionId);
            }
        }

        ai.TotalQuestsCompleted++;
        ai.CurrentQuestId = null;
        ai.State = AiState.Idle;
    }

    private void HandleDead(AiPlayerData ai)
    {
        ai.DeadTicksRemaining--;

        if (ai.DeadTicksRemaining <= 0)
        {
            ai.State = AiState.Idle;
            ai.CurrentQuestId = null;
            _logger.LogDebug("AI {Name} respawned.", ai.Name);
        }
    }

    /// <summary>Kills an AI player and schedules respawn after 60 seconds.</summary>
    public void KillAiPlayer(Guid id)
    {
        var ai = _aiPlayers.FirstOrDefault(a => a.Id == id);
        if (ai is null) return;

        ai.State = AiState.Dead;
        ai.CurrentQuestId = null;
        ai.DeadTicksRemaining = 60 * TicksPerSecond;
    }
}
