using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Models;

public record QuestCompleteResult(
    bool Success,
    string Message,
    int ReputationGained,
    IReadOnlyList<QuestNode> UnlockedQuests,
    bool WyrdSettled
);
