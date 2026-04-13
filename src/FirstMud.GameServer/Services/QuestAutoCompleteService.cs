using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Events;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Hubs;
using Microsoft.AspNetCore.SignalR;
using System.Text.RegularExpressions;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Shared service that checks whether a player's in-progress quests have met
/// their objectives and, if so, completes them immediately — used both by
/// <c>InteractQuestCommandHandler</c> (manual E-press) and
/// <c>FarmingOrchestrator</c> (auto-farm turn-in).
/// </summary>
public class QuestAutoCompleteService(
    IQuestGraphRepository questGraphRepository,
    IItemRepository itemRepository,
    IPlayerRepository playerRepository,
    QuestService questService,
    QuestProgressTracker progressTracker,
    IHubContext<GameHub> hubContext)
{
    // -----------------------------------------------------------------------
    // Constants – kept in sync with InteractQuestCommandHandler
    // -----------------------------------------------------------------------

    private const int DefaultKillsRequired  = 3;
    private const int DefaultGatherRequired = 3;

    // -----------------------------------------------------------------------
    // Quest-type helpers (duplicated from InteractQuestCommandHandler;
    // that handler now delegates here to avoid a circular dependency)
    // -----------------------------------------------------------------------

    public static string GetQuestType(string title) => title switch
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

        _ => "explore"
    };

    public static int ParseKillCount(string description)
    {
        var match = Regex.Match(description, @"\b(\d+)\b");
        return match.Success && int.TryParse(match.Value, out var n) && n > 0 ? n : DefaultKillsRequired;
    }

    public static int ParseItemCount(string description)
    {
        var match = Regex.Match(description, @"\b(\d+)\b");
        return match.Success && int.TryParse(match.Value, out var n) && n > 0 ? n : DefaultGatherRequired;
    }

    public static string? ExtractItemKeyword(string description)
    {
        var lower = description.ToLowerInvariant();
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

    // -----------------------------------------------------------------------
    // Synonym map for fuzzy item matching
    // -----------------------------------------------------------------------

    private static readonly (string Keyword, string[] Synonyms)[] ItemSynonyms =
    [
        ("beast hides",  ["leather", "hide"]),
        ("hides",        ["leather", "hide"]),
        ("iron",         ["iron ore", "iron"]),
        ("herbs",        ["herb", "herbs"]),
        ("wood",         ["wood", "timber", "lumber"]),
        ("stone",        ["stone", "rock"]),
        ("bones",        ["bone"]),
        ("fangs",        ["fang"]),
        ("claws",        ["claw"]),
        ("scales",       ["scale"]),
        ("pelt",         ["pelt", "leather", "hide"]),
    ];

    /// <summary>
    /// Finds items in the player's inventory that match the given keyword.
    /// Tries exact contains match first, then common synonyms.
    /// Exposed internally so harvest/loot handlers can broadcast progress
    /// using the same matching rules the auto-completer applies.
    /// </summary>
    public static List<Domain.Entities.Item> FindMatchingItemsForKeyword(
        IReadOnlyList<Domain.Entities.Item> items,
        string keyword) => FindMatchingItems(items, keyword);

    private static List<Domain.Entities.Item> FindMatchingItems(
        IReadOnlyList<Domain.Entities.Item> items,
        string keyword)
    {
        // 1. Exact contains match
        var exact = items.Where(i => i.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
        if (exact.Count > 0) return exact;

        // 2. Synonym lookup
        var lowerKeyword = keyword.ToLowerInvariant();
        foreach (var (kw, synonyms) in ItemSynonyms)
        {
            if (!lowerKeyword.Contains(kw, StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var syn in synonyms)
            {
                var synMatches = items.Where(i => i.Name.Contains(syn, StringComparison.OrdinalIgnoreCase)).ToList();
                if (synMatches.Count > 0) return synMatches;
            }
        }

        // 3. Partial word match: check each word in the keyword against item names
        var words = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 4)
            .ToArray();
        foreach (var word in words)
        {
            var wordMatches = items.Where(i => i.Name.Contains(word, StringComparison.OrdinalIgnoreCase)).ToList();
            if (wordMatches.Count > 0) return wordMatches;
        }

        return [];
    }

    // -----------------------------------------------------------------------
    // Core: check + complete a single quest if objectives are met
    // Returns true if the quest was completed.
    // -----------------------------------------------------------------------

    /// <summary>
    /// Checks whether the quest objectives are met and, if so, completes the
    /// quest (awards rep/XP, broadcasts events).  Returns true on completion.
    /// <paramref name="autoFarmMode"/> changes the broadcast message prefix.
    /// </summary>
    public async Task<bool> TryCompleteQuestAsync(
        Guid playerId,
        string questId,
        bool autoFarmMode,
        CancellationToken ct)
    {
        var quest = await questGraphRepository.GetQuestAsync(questId, ct);
        if (quest is null) return false;

        var inProgress = await questGraphRepository.IsQuestInProgressAsync(playerId, questId, ct);
        if (!inProgress) return false;

        // ── Check objectives ─────────────────────────────────────────────
        var questType  = GetQuestType(quest.Title);
        string? failReason = null;

        switch (questType)
        {
            case "kill":
            {
                var required = ParseKillCount(quest.Description);
                var actual   = progressTracker.GetKills(playerId, questId);
                if (actual < required)
                    failReason = $"Needs {required - actual} more kills.";
                break;
            }

            case "gather":
            case "deliver":
            {
                var keyword  = ExtractItemKeyword(quest.Description);
                var required = ParseItemCount(quest.Description);
                var items    = await itemRepository.GetByOwnerAsync(playerId, ct);
                var matching = keyword is not null
                    ? FindMatchingItems(items, keyword)
                    : [];
                var heldCount = matching.Sum(i => i.IsStackable ? i.Quantity : 1);
                if (heldCount < required)
                {
                    // Diagnostic: tell the player what we're looking for and what we found
                    var inventorySummary = items
                        .Where(i => i.IsStackable ? i.Quantity > 0 : true)
                        .OrderByDescending(i => i.IsStackable ? i.Quantity : 1)
                        .Take(8)
                        .Select(i => i.IsStackable ? $"{i.Name} x{i.Quantity}" : i.Name);
                    failReason = keyword is not null
                        ? $"Need {required} '{keyword}' but found: {string.Join(", ", inventorySummary)}"
                        : $"Only {heldCount}/{required} items held.";
                }
                break;
            }

            // explore — no check needed
        }

        if (failReason is not null)
            return false;

        // ── Consume gathered/delivered items ─────────────────────────────
        if (questType is "gather" or "deliver")
        {
            var keyword  = ExtractItemKeyword(quest.Description);
            var required = ParseItemCount(quest.Description);

            if (keyword is not null)
            {
                var items    = await itemRepository.GetByOwnerAsync(playerId, ct);
                var matching = FindMatchingItems(items, keyword);

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

        // ── Complete the quest ────────────────────────────────────────────
        var firstOutcome = quest.PossibleOutcomes.FirstOrDefault() ?? "Success";
        var result = await questService.CompleteQuestAsync(playerId, questId, firstOutcome, ct);
        if (!result.Success) return false;

        // ── Clear in-memory progress ──────────────────────────────────────
        progressTracker.Clear(playerId, questId);

        // ── Award XP ─────────────────────────────────────────────────────
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        var questXp = result.ReputationGained * 2;

        if (questXp > 0 && player is not null)
        {
            player.GainExperience(questXp);
            await playerRepository.UpdateAsync(player, ct);
        }

        // ── Broadcast narrative ───────────────────────────────────────────
        var prefix  = autoFarmMode ? "Auto-farm" : "Quest";
        var repText = result.ReputationGained > 0 ? $" +{result.ReputationGained} rep," : string.Empty;
        var xpText  = questXp > 0 ? $" +{questXp} XP" : string.Empty;

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("GameMessage", new
            {
                timestamp = DateTime.UtcNow.ToString("O"),
                category  = "quest",
                text      = $"{prefix}: completed '{quest.Title}'!{repText}{xpText}.",
            }, ct);

        // ── QuestCompleted event ─────────────────────────────────────────
        var questCompletedPayload = new
        {
            PlayerId        = playerId,
            QuestId         = questId,
            result.Message,
            result.ReputationGained,
            UnlockedQuests  = result.UnlockedQuests.Select(q => q.QuestId).ToList(),
            result.WyrdSettled,
        };

        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("QuestCompleted", questCompletedPayload, ct);

        // ── Reputation tiers ──────────────────────────────────────────────
        if (player is not null)
        {
            var factionTiers = player.Reputations.ToDictionary(
                r => r.FactionId.ToString(),
                r => r.Score.Tier.ToString());

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("ReputationChanged", new { PlayerId = playerId, FactionTiers = factionTiers }, ct);

            // Domain events (level-up, portal-unlock, etc.)
            foreach (var domainEvent in player.DomainEvents)
            {
                switch (domainEvent)
                {
                    case PortalUnlockedEvent portalEvent:
                        await hubContext.Clients
                            .Group(playerId.ToString())
                            .SendAsync("PortalUnlocked", new
                            {
                                PlayerId = portalEvent.PlayerId,
                                WorldId  = portalEvent.WorldId.ToString(),
                                Message  = $"Portal to {portalEvent.WorldId} unlocked!",
                            }, ct);
                        break;

                    case PlayerLeveledUpEvent levelEvent:
                        await hubContext.Clients
                            .Group(playerId.ToString())
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

        // ── Refresh quest log ─────────────────────────────────────────────
        await hubContext.Clients
            .Group(playerId.ToString())
            .SendAsync("CommandReceived", new { command = "getquests" }, ct);

        return true;
    }

    // -----------------------------------------------------------------------
    // Auto-farm batch check — call after each combat victory or move step
    // -----------------------------------------------------------------------

    /// <summary>
    /// Checks all in-progress quests for the player and auto-turns-in any whose
    /// objectives are met.  Returns the number of quests completed.
    /// </summary>
    public async Task<int> TryAutoCompleteQuestsAsync(Guid playerId, CancellationToken ct)
    {
        // GetAvailableQuestsAsync returns both available and in-progress quests;
        // filter to taken (in-progress) ones.
        var allQuests   = await questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
        var inProgress  = allQuests.Where(q => q.IsTaken).ToList();

        if (inProgress.Count == 0) return 0;

        int completed = 0;
        foreach (var quest in inProgress)
        {
            var wasCompleted = await TryCompleteQuestAsync(playerId, quest.QuestId, autoFarmMode: true, ct);
            if (wasCompleted) completed++;
        }

        return completed;
    }
}
