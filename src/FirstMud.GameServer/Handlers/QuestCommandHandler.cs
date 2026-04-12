using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace FirstMud.GameServer.Handlers;

public class AcceptQuestCommandHandler(
    IPlayerRepository playerRepository,
    IQuestGraphRepository questGraphRepository,
    IHubContext<GameHub> hubContext) : ICommandHandler<AcceptQuestCommand>
{
    // Zone centre coordinates matching ZoneGridLayout.cs (also in biome.ts)
    private static readonly Dictionary<FactionId, (int x, int y, string desc)> FactionWaypoints = new()
    {
        [FactionId.HouseCaervorn]    = (8,  3,  "Caervorn Highlands"),
        [FactionId.ThornwoodCovens]  = (13, 5,  "The Thornwood"),
        [FactionId.EmeraldCompact]   = (24, 13, "Portmere (Compact)"),
        [FactionId.Gravenguard]      = (28, 9,  "Gravenmarsh area"),
        [FactionId.Fairgean]         = (32, 15, "The Drowned Coast"),
        [FactionId.AshenCourt]       = (34, 4,  "The Ashen Reach"),
        [FactionId.Golvari]          = (36, 18, "The Maw Borderlands"),
    };

    /// <summary>
    /// Determines a target waypoint from the quest description and faction.
    /// Gather/kill quests near dangerous zones use the danger zone; delivery quests
    /// use the destination faction; all others fall back to the quest faction HQ.
    /// </summary>
    private static (int x, int y, string desc) ResolveWaypoint(string title, string description, FactionId factionId)
    {
        var lower = (title + " " + description).ToLowerInvariant();

        // Delivery quest: target destination based on description keyword
        if (lower.Contains("deliver") || lower.Contains("package") || lower.Contains("message"))
        {
            // Try to find a zone name mentioned in the description
            foreach (var (fid, waypoint) in FactionWaypoints)
            {
                if (fid != factionId && lower.Contains(waypoint.desc.ToLowerInvariant()))
                    return waypoint;
            }
        }

        // Kill quest: target appropriate danger zone
        if (lower.Contains("kill") || lower.Contains("slay") || lower.Contains("defeat") || lower.Contains("bandit") || lower.Contains("ruin"))
        {
            if (lower.Contains("maw") || lower.Contains("borderland") || lower.Contains("wyrd"))
                return FactionWaypoints[FactionId.Golvari];
            if (lower.Contains("coast") || lower.Contains("drowned") || lower.Contains("shore"))
                return FactionWaypoints[FactionId.Fairgean];
            if (lower.Contains("thornwood") || lower.Contains("forest"))
                return FactionWaypoints[FactionId.ThornwoodCovens];
            if (lower.Contains("ashen") || lower.Contains("desert") || lower.Contains("reach"))
                return FactionWaypoints[FactionId.AshenCourt];
        }

        // Gather quest: point toward the resource zone
        if (lower.Contains("gather") || lower.Contains("collect") || lower.Contains("find") || lower.Contains("bring"))
        {
            if (lower.Contains("highland") || lower.Contains("stone") || lower.Contains("ore"))
                return FactionWaypoints[FactionId.HouseCaervorn];
            if (lower.Contains("herb") || lower.Contains("root") || lower.Contains("wood") || lower.Contains("forest"))
                return FactionWaypoints[FactionId.ThornwoodCovens];
        }

        // Default: faction HQ
        return FactionWaypoints.TryGetValue(factionId, out var fallback)
            ? fallback
            : (20, 10, "Starting Road");
    }

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

        // Broadcast a quest waypoint so the client can navigate
        var quest = await questGraphRepository.GetQuestAsync(cmd.QuestId, ct);
        if (quest is not null)
        {
            var (wpX, wpY, wpDesc) = ResolveWaypoint(quest.Title, quest.Description, quest.FactionId);
            var waypointPayload = new
            {
                QuestId    = cmd.QuestId,
                QuestTitle = quest.Title,
                TargetX    = wpX,
                TargetY    = wpY,
                Description = wpDesc,
            };

            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("QuestWaypoint", waypointPayload, ct);
        }

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
