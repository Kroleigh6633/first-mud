using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace FirstMud.GameServer.Handlers;

public class AcceptQuestCommandHandler(
    IPlayerRepository playerRepository,
    IQuestGraphRepository questGraphRepository,
    IContentProvider content,
    IHubContext<GameHub> hubContext) : ICommandHandler<AcceptQuestCommand>
{
    // Faction waypoints are now authored in content/factions.json; the old
    // hardcoded FactionWaypoints dictionary lived here and has been removed.

    private (int x, int y, string desc) GetFactionWaypoint(FactionId factionId)
    {
        var def = content.GetFaction(factionId);
        if (def?.Waypoint is null)
            return (20, 10, "Starting Road");
        return (def.Waypoint.X, def.Waypoint.Y, def.HqDisplayName ?? def.DisplayName);
    }

    /// <summary>
    /// Determines a target waypoint from the quest description and faction.
    /// Gather/kill quests near dangerous zones use the danger zone; delivery quests
    /// use the destination faction; all others fall back to the quest faction HQ.
    /// </summary>
    private (int x, int y, string desc) ResolveWaypoint(string title, string description, FactionId factionId)
    {
        var lower = (title + " " + description).ToLowerInvariant();

        // Delivery quest: target destination based on description keyword
        if (lower.Contains("deliver") || lower.Contains("package") || lower.Contains("message"))
        {
            foreach (var def in content.AllFactions())
            {
                if (def.Id == factionId || def.Waypoint is null) continue;
                var descText = def.HqDisplayName ?? def.DisplayName;
                if (lower.Contains(descText.ToLowerInvariant()))
                    return (def.Waypoint.X, def.Waypoint.Y, descText);
            }
        }

        // Kill quest: target appropriate danger zone
        if (lower.Contains("kill") || lower.Contains("slay") || lower.Contains("defeat") || lower.Contains("bandit") || lower.Contains("ruin"))
        {
            if (lower.Contains("maw") || lower.Contains("borderland") || lower.Contains("wyrd"))
                return GetFactionWaypoint(FactionId.Golvari);
            if (lower.Contains("coast") || lower.Contains("drowned") || lower.Contains("shore"))
                return GetFactionWaypoint(FactionId.Fairgean);
            if (lower.Contains("thornwood") || lower.Contains("forest"))
                return GetFactionWaypoint(FactionId.ThornwoodCovens);
            if (lower.Contains("ashen") || lower.Contains("desert") || lower.Contains("reach"))
                return GetFactionWaypoint(FactionId.AshenCourt);
        }

        // Gather quest: point toward the resource zone
        if (lower.Contains("gather") || lower.Contains("collect") || lower.Contains("find") || lower.Contains("bring"))
        {
            if (lower.Contains("highland") || lower.Contains("stone") || lower.Contains("ore"))
                return GetFactionWaypoint(FactionId.HouseCaervorn);
            if (lower.Contains("herb") || lower.Contains("root") || lower.Contains("wood") || lower.Contains("forest"))
                return GetFactionWaypoint(FactionId.ThornwoodCovens);
        }

        // Default: faction HQ
        return GetFactionWaypoint(factionId);
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

        // Load quest details so the title can be included in the broadcast payload.
        var quest = await questGraphRepository.GetQuestAsync(cmd.QuestId, ct);

        var questPayload = new { cmd.PlayerId, QuestId = cmd.QuestId, Title = quest?.Title ?? cmd.QuestId };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("QuestAccepted", questPayload, ct);

        // Broadcast a quest waypoint so the client can navigate
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
    IContentProvider contentProvider,
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

            // Quest gold reward — base + per-rep-point bonus (content-tunable).
            var goldCurve = contentProvider.TradeCurves.Gold;
            var goldReward = (int)Math.Round(goldCurve.QuestRewardBase
                + goldCurve.QuestRewardPerReputationPoint * result.ReputationGained);
            if (goldReward > 0)
            {
                player.AddGold(goldReward);
                await playerRepository.UpdateAsync(player, ct);
                await hubContext.Clients
                    .Group(cmd.PlayerId.ToString())
                    .SendAsync("GameMessage", new
                    {
                        timestamp = DateTime.UtcNow.ToString("O"),
                        category = "quest",
                        text = $"You receive {goldReward} gold."
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

/// <summary>
/// Handles the player pressing Interact [E] at (or near) a quest waypoint.
/// Determines the quest type from the title, checks requirements, and either
/// completes the quest immediately or tells the player what they still need.
///
/// Quest types detected from title keywords:
///   kill     — Defeat / Slay / Hunt
///   gather   — Gather / Collect / Retrieve
///   explore  — Investigate / Explore / Uncover  (also the default)
///   deliver  — Deliver / Escort / Bring
/// </summary>
public class InteractQuestCommandHandler(
    IItemRepository itemRepository,
    IQuestGraphRepository questGraphRepository,
    QuestProgressTracker progressTracker,
    QuestAutoCompleteService questAutoCompleteService,
    IHubContext<GameHub> hubContext,
    ILogger<InteractQuestCommandHandler> logger) : ICommandHandler<InteractQuestCommand>
{
    // -----------------------------------------------------------------------
    // Lore text fragments for explore completions
    // -----------------------------------------------------------------------

    private static readonly string[] ExploreNarratives =
    [
        "You survey the area carefully. Signs of old conflict lie scattered across the ground.",
        "The air is thick with old weave here. You make notes of what you find.",
        "You search the surroundings and piece together what happened. The truth is unsettling.",
        "Ancient marks in the earth tell a story. You have learned what you came to learn.",
        "A faint hum lingers. You have seen enough — the knowledge is yours now.",
    ];

    public async Task<CommandResult> HandleAsync(InteractQuestCommand cmd, CancellationToken ct)
    {
        // ── 1. Validate quest is in-progress for this player ──────────────
        logger.LogInformation(
            "InteractQuest: player={PlayerId} questId={QuestId}",
            cmd.PlayerId, cmd.QuestId);

        var quest = await questGraphRepository.GetQuestAsync(cmd.QuestId, ct);
        if (quest is null)
        {
            logger.LogWarning(
                "InteractQuest: quest not found in Neo4j. player={PlayerId} questId={QuestId}",
                cmd.PlayerId, cmd.QuestId);
            return new CommandResult(false, "Quest not found.");
        }

        // Use a player-scoped IN_PROGRESS check rather than quest.IsTaken,
        // which only reflects AI-taken state in GetQuestAsync.
        var inProgress = await questGraphRepository.IsQuestInProgressAsync(cmd.PlayerId, cmd.QuestId, ct);
        logger.LogInformation(
            "InteractQuest: inProgress={InProgress} for player={PlayerId} questId={QuestId} questTitle={Title}",
            inProgress, cmd.PlayerId, cmd.QuestId, quest.Title);

        if (!inProgress)
            return new CommandResult(false, "You have not accepted this quest.");

        // ── 2. Determine quest type ────────────────────────────────────────
        var questType = GetQuestType(quest.Title);

        // ── 3. Type-specific requirement check ────────────────────────────
        string? failReason = null;

        switch (questType)
        {
            case "kill":
            {
                var required = ParseKillCount(quest.Description);
                var actual   = progressTracker.GetKills(cmd.PlayerId, cmd.QuestId);
                if (actual < required)
                {
                    failReason = $"You need to defeat {required - actual} more enemies in this area before you can complete this quest.";
                }
                break;
            }

            case "gather":
            case "deliver":
            {
                // Extract a resource keyword from the description to search the inventory.
                var keyword  = ExtractItemKeyword(quest.Description);
                var required = ParseItemCount(quest.Description);
                var items    = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);

                // Count items whose names contain the keyword (case-insensitive).
                var matching = keyword is not null
                    ? items.Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList()
                    : [];

                var heldCount = matching.Sum(i => i.IsStackable ? i.Quantity : 1);

                if (heldCount < required)
                {
                    var tip = questType == "gather"
                        ? "Try harvesting [E] or fighting nearby enemies."
                        : "Find the required item first, then return here.";
                    failReason = keyword is not null
                        ? $"You need {required - heldCount} more {keyword} to complete this quest. {tip}"
                        : $"You are missing the required item. {tip}";
                }
                break;
            }

            // "explore" — arriving is sufficient; no extra check needed.
        }

        // ── 4. Cannot complete yet ─────────────────────────────────────────
        if (failReason is not null)
        {
            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category  = "quest",
                    text      = failReason,
                }, ct);

            return new CommandResult(false, failReason);
        }

        // ── 5. Broadcast immersive completion text ─────────────────────────
        var completionNarrative = BuildCompletionNarrative(questType, quest.Title, quest.Description);
        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category  = "quest",
                text      = completionNarrative,
            }, ct);

        // ── 6-9. Delegate shared completion (consume items, award rep/XP, broadcast) ──
        var completed = await questAutoCompleteService.TryCompleteQuestAsync(
            cmd.PlayerId, cmd.QuestId, autoFarmMode: false, ct);

        if (!completed)
            return new CommandResult(false, "Quest could not be completed at this time.");

        return new CommandResult(true, $"Quest '{quest.Title}' completed.");
    }

    // -----------------------------------------------------------------------
    // Helpers — delegate to QuestAutoCompleteService for shared logic
    // -----------------------------------------------------------------------

    private static string GetQuestType(string title)
        => QuestAutoCompleteService.GetQuestType(title);

    private static int ParseKillCount(string description)
        => QuestAutoCompleteService.ParseKillCount(description);

    private static int ParseItemCount(string description)
        => QuestAutoCompleteService.ParseItemCount(description);

    private static string? ExtractItemKeyword(string description)
        => QuestAutoCompleteService.ExtractItemKeyword(description);

    private static string BuildCompletionNarrative(string questType, string title, string description)
    {
        return questType switch
        {
            "explore" =>
                ExploreNarratives[Random.Shared.Next(ExploreNarratives.Length)],

            "kill" =>
                "The last of them falls. The area is quiet now. Your work here is done.",

            "gather" =>
                "You present the gathered materials. They are accepted with a curt nod. 'This will do,' the voice says.",

            "deliver" =>
                "The package changes hands. 'Well timed, Rider,' the recipient says. 'We were beginning to wonder.'",

            _ =>
                $"You complete {title}. Well done.",
        };
    }
}
