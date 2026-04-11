using FirstMud.Application.Models;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

public class QuestService
{
    private readonly IQuestGraphRepository _questGraph;
    private readonly ReputationService _reputationService;
    private readonly IPlayerRepository _players;

    public QuestService(
        IQuestGraphRepository questGraph,
        ReputationService reputationService,
        IPlayerRepository players)
    {
        _questGraph = questGraph;
        _reputationService = reputationService;
        _players = players;
    }

    public async Task<IReadOnlyList<QuestNode>> GetAvailableQuestsAsync(
        Guid playerId,
        FactionId? faction,
        CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            throw new InvalidOperationException($"Player {playerId} not found.");

        return await _questGraph.GetAvailableQuestsAsync(playerId, faction, ct);
    }

    public async Task<QuestCompleteResult> CompleteQuestAsync(
        Guid playerId,
        string questId,
        string chosenOutcome,
        CancellationToken ct = default)
    {
        // 1. Verify quest is available for player
        bool isAvailable = await _questGraph.IsQuestAvailableAsync(playerId, questId, ct);
        if (!isAvailable)
            return new QuestCompleteResult(false, "Quest is not available for this player.", 0, [], false);

        var quest = await _questGraph.GetQuestAsync(questId, ct);
        if (quest is null)
            return new QuestCompleteResult(false, "Quest not found.", 0, [], false);

        // 2. Mark completed in Neo4j
        await _questGraph.MarkQuestCompletedAsync(playerId, questId, chosenOutcome, ct);

        // 3. Apply reputation reward
        await _reputationService.ApplyQuestReputationRewardsAsync(
            playerId,
            quest.FactionId,
            quest.ReputationReward,
            ct);

        // 4. If IsWyrdQuest: resolve tangle
        bool wyrdSettled = false;
        if (quest.IsWyrdQuest)
        {
            var player = await _players.GetByIdAsync(playerId, ct);
            if (player is not null)
            {
                player.ResolveWyrdTangle(10);
                await _players.UpdateAsync(player, ct);
                wyrdSettled = true;
            }
        }

        // 5. Get newly unlocked quests
        var unlockedQuests = await _questGraph.GetUnlockedByCompletionAsync(questId, chosenOutcome, ct);

        return new QuestCompleteResult(
            true,
            $"Quest '{quest.Title}' completed. Outcome: {chosenOutcome}.",
            quest.ReputationReward,
            unlockedQuests,
            wyrdSettled);
    }
}
