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
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    QuestService questService,
    IQuestGraphRepository questGraphRepository,
    QuestProgressTracker progressTracker,
    IHubContext<GameHub> hubContext,
    ILogger<InteractQuestCommandHandler> logger) : ICommandHandler<InteractQuestCommand>
{
    // Number of kills required before a kill quest is completable.
    // Quest description may embed a number like "defeat 5 bandits"; we try to parse it.
    private const int DefaultKillsRequired = 3;

    // Number of matching inventory items required for gather/deliver quests.
    private const int DefaultGatherRequired = 3;

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

        var firstOutcome = quest.PossibleOutcomes.FirstOrDefault() ?? "Success";

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

        // ── 5. Consume gathered/delivered items from inventory ─────────────
        if (questType is "gather" or "deliver")
        {
            var keyword  = ExtractItemKeyword(quest.Description);
            var required = ParseItemCount(quest.Description);

            if (keyword is not null)
            {
                var items    = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
                var matching = items
                    .Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var toConsume = required;
                foreach (var item in matching)
                {
                    if (toConsume <= 0) break;
                    if (item.IsStackable && item.Quantity > 1)
                    {
                        var take = Math.Min(toConsume, item.Quantity);
                        if (item.TryRemoveQuantity(take, out var remaining))
                        {
                            if (remaining <= 0)
                                await itemRepository.DeleteAsync(item.Id, ct);
                            else
                                await itemRepository.UpdateAsync(item, ct);
                            toConsume -= take;
                        }
                    }
                    else
                    {
                        await itemRepository.DeleteAsync(item.Id, ct);
                        toConsume--;
                    }
                }
            }
        }

        // ── 6. Broadcast immersive completion text ─────────────────────────
        var completionNarrative = BuildCompletionNarrative(questType, quest.Title, quest.Description);
        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category  = "quest",
                text      = completionNarrative,
            }, ct);

        // ── 7. Complete the quest via QuestService (rep + XP + events) ─────
        var result = await questService.CompleteQuestAsync(cmd.PlayerId, cmd.QuestId, firstOutcome, ct);
        if (!result.Success)
            return new CommandResult(false, result.Message);

        // ── 8. Clear in-memory progress ────────────────────────────────────
        progressTracker.Clear(cmd.PlayerId, cmd.QuestId);

        // ── 9. Notify client ───────────────────────────────────────────────
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);

        var questXp = result.ReputationGained * 2;
        if (questXp > 0 && player is not null)
        {
            player.GainExperience(questXp);
            await playerRepository.UpdateAsync(player, ct);

            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("GameMessage", new
                {
                    timestamp = DateTime.UtcNow.ToString("O"),
                    category  = "quest",
                    text      = $"You gained {questXp} experience!",
                }, ct);
        }

        var questCompletedPayload = new
        {
            cmd.PlayerId,
            QuestId         = cmd.QuestId,
            result.Message,
            result.ReputationGained,
            UnlockedQuests  = result.UnlockedQuests.Select(q => q.QuestId).ToList(),
            result.WyrdSettled,
        };

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("QuestCompleted", questCompletedPayload, ct);

        if (player is not null)
        {
            var factionTiers = player.Reputations.ToDictionary(
                r => r.FactionId.ToString(),
                r => r.Score.Tier.ToString());

            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("ReputationChanged", new { cmd.PlayerId, FactionTiers = factionTiers }, ct);

            // Bubble any domain events (level-up, portal-unlock, etc.)
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
                                WorldId  = portalEvent.WorldId.ToString(),
                                Message  = $"Portal to {portalEvent.WorldId} unlocked!",
                            }, ct);
                        break;

                    case PlayerLeveledUpEvent levelEvent:
                        await hubContext.Clients
                            .Group(cmd.PlayerId.ToString())
                            .SendAsync("PlayerLeveledUp", new
                            {
                                PlayerId = levelEvent.PlayerId,
                                NewLevel = levelEvent.NewLevel,
                                Message  = $"You reached level {levelEvent.NewLevel}!",
                            }, ct);
                        break;
                }
            }

            player.ClearDomainEvents();
        }

        // Refresh the quest log so the completed quest is removed
        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CommandReceived", new { command = "getquests" }, ct);

        return new CommandResult(true, result.Message, questCompletedPayload);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static string GetQuestType(string title) => title switch
    {
        var t when t.Contains("Defeat", StringComparison.OrdinalIgnoreCase)
                || t.Contains("Slay",    StringComparison.OrdinalIgnoreCase)
                || t.Contains("Hunt",    StringComparison.OrdinalIgnoreCase)   => "kill",

        var t when t.Contains("Gather",   StringComparison.OrdinalIgnoreCase)
                || t.Contains("Collect",  StringComparison.OrdinalIgnoreCase)
                || t.Contains("Retrieve", StringComparison.OrdinalIgnoreCase)  => "gather",

        var t when t.Contains("Deliver",  StringComparison.OrdinalIgnoreCase)
                || t.Contains("Escort",   StringComparison.OrdinalIgnoreCase)
                || t.Contains("Bring",    StringComparison.OrdinalIgnoreCase)  => "deliver",

        _ => "explore"   // Investigate / Explore / Uncover / anything else
    };

    /// <summary>
    /// Tries to parse a number from the description, e.g. "defeat 5 bandits" → 5.
    /// Falls back to <see cref="DefaultKillsRequired"/>.
    /// </summary>
    private static int ParseKillCount(string description)
    {
        var match = Regex.Match(description, @"\b(\d+)\b");
        return match.Success && int.TryParse(match.Value, out var n) && n > 0 ? n : DefaultKillsRequired;
    }

    /// <summary>Same as ParseKillCount but for gather/deliver quantities.</summary>
    private static int ParseItemCount(string description)
    {
        var match = Regex.Match(description, @"\b(\d+)\b");
        return match.Success && int.TryParse(match.Value, out var n) && n > 0 ? n : DefaultGatherRequired;
    }

    /// <summary>
    /// Extracts a single relevant noun from the quest description for inventory matching.
    /// E.g. "Gather 3 units of thornweed" → "thornweed".
    /// Very simple: takes the last 'significant' word after known quantity phrases.
    /// </summary>
    private static string? ExtractItemKeyword(string description)
    {
        // Strip leading digits and common filler words to get a resource noun.
        var lower = description.ToLowerInvariant();

        // Patterns like "gather N units of X", "collect N X", "retrieve the X"
        var match = Regex.Match(lower,
            @"\b(?:gather|collect|retrieve|bring|deliver|find|obtain)\b[\w\s]*\bof\s+([a-z]+)",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        match = Regex.Match(lower,
            @"\b(?:gather|collect|retrieve|bring|deliver|find|obtain)\s+(?:\d+\s+)?(?:units?\s+of\s+)?([a-z]{4,})",
            RegexOptions.IgnoreCase);
        if (match.Success) return match.Groups[1].Value;

        return null;
    }

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
