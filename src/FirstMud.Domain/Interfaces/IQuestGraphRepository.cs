using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Interfaces;

public interface IQuestGraphRepository
{
    Task<QuestNode?> GetQuestAsync(string questId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestNode>> GetAvailableQuestsAsync(Guid playerId, FactionId? factionId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestNode>> GetQuestsByTierAsync(ReputationTier tier, FactionId factionId, CancellationToken cancellationToken = default);
    Task MarkQuestCompletedAsync(Guid playerId, string questId, string chosenOutcome, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<QuestNode>> GetUnlockedByCompletionAsync(string completedQuestId, string outcome, CancellationToken cancellationToken = default);
    Task<bool> IsQuestAvailableAsync(Guid playerId, string questId, CancellationToken cancellationToken = default);
    Task MarkQuestInProgressAsync(Guid playerId, string questId, bool takenByAi = false, CancellationToken ct = default);
}

public record QuestNode(
    string QuestId,
    string Title,
    string Description,
    FactionId FactionId,
    ReputationTier RequiredTier,
    WorldId RequiredWorld,
    int ReputationReward,
    string[] PossibleOutcomes,
    bool IsWyrdQuest,
    bool IsTaken);
