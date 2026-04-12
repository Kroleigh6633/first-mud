using FirstMud.Application.Services;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class AcceptQuestCommandHandler(
    IPlayerRepository playerRepository,
    IQuestGraphRepository questGraphRepository,
    IHubContext<GameHub> hubContext) : ICommandHandler<AcceptQuestCommand>
{
    public async Task<CommandResult> HandleAsync(AcceptQuestCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var isAvailable = await questGraphRepository.IsQuestAvailableAsync(cmd.PlayerId, cmd.QuestId, ct);
        if (!isAvailable)
            return new CommandResult(false, "Quest is not available for this player.");

        await questGraphRepository.MarkQuestInProgressAsync(cmd.PlayerId, cmd.QuestId, takenByAi: false, ct: ct);

        var questPayload = new { cmd.PlayerId, QuestId = cmd.QuestId };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("QuestAccepted", questPayload, ct);

        return new CommandResult(true, $"Quest '{cmd.QuestId}' accepted.", questPayload);
    }
}

public class CompleteQuestCommandHandler(
    IPlayerRepository playerRepository,
    QuestService questService,
    IHubContext<GameHub> hubContext) : ICommandHandler<CompleteQuestCommand>
{
    public async Task<CommandResult> HandleAsync(CompleteQuestCommand cmd, CancellationToken ct)
    {
        var result = await questService.CompleteQuestAsync(cmd.PlayerId, cmd.QuestId, cmd.ChosenOutcome, ct);

        if (!result.Success)
            return new CommandResult(false, result.Message);

        var questCompletedPayload = new
        {
            cmd.PlayerId,
            QuestId = cmd.QuestId,
            result.Message,
            result.ReputationGained,
            UnlockedQuests = result.UnlockedQuests.Select(q => q.QuestId).ToList(),
            result.WyrdSettled
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("QuestCompleted", questCompletedPayload, ct);

        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is not null)
        {
            var questXp = result.ReputationGained * 2;
            if (questXp > 0)
            {
                player.GainExperience(questXp);
                await playerRepository.UpdateAsync(player, ct);

                await hubContext.Clients
                    .Group(cmd.PlayerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "quest",
                        text = $"You gained {questXp} experience!"
                    }, ct);
            }

            var factionTiers = player.Reputations.ToDictionary(
                r => r.FactionId.ToString(),
                r => r.Score.Tier.ToString());

            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("ReputationChanged", new { cmd.PlayerId, FactionTiers = factionTiers }, ct);

            foreach (var domainEvent in player.DomainEvents)
            {
                switch (domainEvent)
                {
                    case PortalUnlockedEvent portalEvent:
                        await hubContext.Clients
                            .Group(cmd.PlayerId.ToString())
                            .SendAsync("PortalUnlocked", new
                            {
                                PlayerId = portalEvent.PlayerId,
                                WorldId = portalEvent.WorldId.ToString(),
                                Message = $"Portal to {portalEvent.WorldId} unlocked!"
                            }, ct);
                        break;

                    case PlayerLeveledUpEvent levelEvent:
                        await hubContext.Clients
                            .Group(cmd.PlayerId.ToString())
                            .SendAsync("PlayerLeveledUp", new
                            {
                                PlayerId = levelEvent.PlayerId,
                                NewLevel = levelEvent.NewLevel,
                                Message = $"You reached level {levelEvent.NewLevel}!"
                            }, ct);
                        break;
                }
            }

            player.ClearDomainEvents();
        }

        return new CommandResult(true, result.Message, questCompletedPayload);
    }
}

public class GetAvailableQuestsCommandHandler(
    WorldStateService worldStateService,
    GameNotificationService notificationService) : ICommandHandler<GetAvailableQuestsCommand>
{
    public async Task<CommandResult> HandleAsync(GetAvailableQuestsCommand cmd, CancellationToken ct)
    {
        var quests = await worldStateService.GetAvailableQuestsAsync(cmd.PlayerId, ct);

        var questPayload = quests.Select(q => new
        {
            q.QuestId,
            q.Title,
            q.Description,
            FactionId = (int)q.FactionId,
            RequiredTier = (int)q.RequiredTier,
            q.ReputationReward,
            q.PossibleOutcomes,
            q.IsWyrdQuest,
            q.IsTaken,
        }).ToList();

        await notificationService.SendEventAsync(cmd.PlayerId, "AvailableQuests", questPayload, ct);

        return new CommandResult(true, $"Fetched {quests.Count} available quests.", questPayload);
    }
}
