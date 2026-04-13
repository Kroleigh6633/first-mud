using System.Text.RegularExpressions;
using FirstMud.Application.Content;

namespace FirstMud.DesignTools.Tools.PlaybookRunner.AutoQuestFlow;

/// <summary>
/// Headless simulation of the GameTerminal.tsx auto-quest loop, driving a mock
/// player through the same accept → navigate → interact → complete chain without
/// touching SignalR, Neo4j, or the React runtime.
///
/// Decision logic mirrors:
///   • client: GameTerminal.tsx autoQuest runner
///   • server: AcceptQuestCommandHandler.ResolveWaypoint (keyword routing)
///   • server: QuestAutoCompleteService (objective checks)
///
/// For each quest in the pool we classify the run outcome:
///   completed | stalled-no-start-zone | stalled-no-monsters |
///   stalled-no-item-source | stalled-unknown-item | timeout
///
/// This is DATA-level triage: a "completed" result means the content graph is
/// complete enough that an ideal auto-quest run would finish. Engine/handler
/// bugs that break the loop *at runtime* are tested separately with live repro.
/// </summary>
public sealed class AutoQuestSimulationService
{
    public enum Outcome
    {
        Completed,
        StalledNoStartZone,
        StalledNoMonsters,
        StalledUnknownItem,
        StalledNoItemSource,
        Timeout,
    }

    public sealed record QuestRun(string QuestId, string Title, string QuestType, Outcome Outcome, double Minutes);

    private readonly IContentProvider _content;

    // Minutes each loop beat costs in virtual time.
    private const double MinutesAccept   = 0.1;
    private const double MinutesNavigate = 2.0;
    private const double MinutesInteract = 0.5;
    private const double MinutesKillPer  = 0.7;   // per enemy
    private const double MinutesGatherPer = 0.4;  // per resource unit

    public AutoQuestSimulationService(IContentProvider content)
    {
        _content = content;
    }

    /// <summary>
    /// Filters the quest corpus to quests whose starting zone matches (or whose
    /// waypoint routes to) the given zone, then runs each through the simulation.
    /// </summary>
    public IReadOnlyList<QuestRun> RunZone(string startZoneId, int questPoolSize, double simulatedMinutes)
    {
        var pool = PickPool(startZoneId, questPoolSize);
        var runs = new List<QuestRun>(pool.Count);
        double clock = 0.0;

        foreach (var q in pool)
        {
            if (clock >= simulatedMinutes)
            {
                runs.Add(new QuestRun(q.QuestId, q.Title, GetQuestType(q.Title), Outcome.Timeout, simulatedMinutes - clock));
                continue;
            }

            var (outcome, minutes) = Simulate(q);
            clock += minutes;
            if (clock > simulatedMinutes) outcome = Outcome.Timeout;
            runs.Add(new QuestRun(q.QuestId, q.Title, GetQuestType(q.Title), outcome, minutes));
        }

        return runs;
    }

    /// <summary>Runs every quest in the corpus once, regardless of start zone.</summary>
    public IReadOnlyList<QuestRun> RunAll()
    {
        var runs = new List<QuestRun>();
        foreach (var q in _content.AllQuests())
        {
            var (outcome, minutes) = Simulate(q);
            runs.Add(new QuestRun(q.QuestId, q.Title, GetQuestType(q.Title), outcome, minutes));
        }
        return runs;
    }

    // ─── Pool selection (mirrors GameTerminal.tsx availableQuests picking) ───

    private IReadOnlyList<QuestDefinition> PickPool(string startZoneId, int poolSize)
    {
        var all = _content.AllQuests();
        // Zone-preferred: quests that start in this zone.
        var zoneMatch = all.Where(q => string.Equals(q.StartingZoneId, startZoneId, StringComparison.OrdinalIgnoreCase)).ToList();
        if (zoneMatch.Count >= poolSize) return zoneMatch.Take(poolSize).ToList();
        // Fall back to the remainder of the corpus, filling up to poolSize.
        var rest = all.Where(q => !zoneMatch.Contains(q)).Take(poolSize - zoneMatch.Count);
        return zoneMatch.Concat(rest).Take(poolSize).ToList();
    }

    // ─── Per-quest simulation ────────────────────────────────────────────────

    public (Outcome outcome, double minutes) Simulate(QuestDefinition quest)
    {
        double minutes = MinutesAccept;

        // Stall: quest has no starting zone → client can't route.
        if (string.IsNullOrEmpty(quest.StartingZoneId))
            return (Outcome.StalledNoStartZone, minutes);

        minutes += MinutesNavigate;
        minutes += MinutesInteract;

        var type = GetQuestType(quest.Title);

        switch (type)
        {
            case "kill":
            {
                // Need monsters in a reachable zone. Check the corpus: any zone with
                // the quest's starting zone biome has combat-able monsters.
                var zone = _content.AllZones().FirstOrDefault(z => z.ZoneId == quest.StartingZoneId);
                var monstersAvailable = zone != null && AnyMonsterFitsDanger(zone.DangerLevel);
                if (!monstersAvailable)
                    return (Outcome.StalledNoMonsters, minutes);
                var required = ParseKillCount(quest.Description);
                minutes += required * MinutesKillPer;
                return (Outcome.Completed, minutes);
            }

            case "gather":
            case "deliver":
            {
                var keyword = ExtractItemKeyword(quest.Description);
                if (keyword is null)
                    return (Outcome.StalledUnknownItem, minutes);
                if (!ItemHasSource(keyword))
                    return (Outcome.StalledNoItemSource, minutes);
                var required = ParseItemCount(quest.Description);
                minutes += required * MinutesGatherPer;
                return (Outcome.Completed, minutes);
            }

            default: // "explore"
                return (Outcome.Completed, minutes);
        }
    }

    // ─── Quest-type helpers (mirror QuestAutoCompleteService exactly) ───────

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
        var m = Regex.Match(description, @"\b(\d+)\b");
        return m.Success && int.TryParse(m.Value, out var n) && n > 0 ? n : 3;
    }

    public static int ParseItemCount(string description)
    {
        var m = Regex.Match(description, @"\b(\d+)\b");
        return m.Success && int.TryParse(m.Value, out var n) && n > 0 ? n : 3;
    }

    public static string? ExtractItemKeyword(string description)
    {
        var lower = description.ToLowerInvariant();
        var m = Regex.Match(lower, @"\b(?:gather|collect|retrieve|bring|deliver|find|obtain)\b[\w\s]*\bof\s+([a-z]+)", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        m = Regex.Match(lower, @"\b(?:gather|collect|retrieve|bring|deliver|find|obtain)\s+(?:\d+\s+)?(?:units?\s+of\s+)?([a-z]{4,})", RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value;
        return null;
    }

    // ─── Content probes ──────────────────────────────────────────────────────

    private bool AnyMonsterFitsDanger(int dangerLevel)
    {
        // Monster pool is global; at worst we can always spawn a generic tier-0.
        // The question is whether the roster has >=1 monster at-or-below (danger+2).
        foreach (var m in _content.AllMonsters())
            if (m.Level <= dangerLevel + 2) return true;
        return false;
    }

    private bool ItemHasSource(string keyword)
    {
        var kw = keyword.ToLowerInvariant();

        // 1. Monster drops: name contains keyword or one of the synonyms.
        foreach (var m in _content.AllMonsters())
            if (m.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;

        // 2. Recipe outputs.
        foreach (var r in _content.AllRecipes())
            if (r.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return true;

        // 3. Loot drop pools — common-materials + biome tables.
        foreach (var pool in _content.LootTables.DropPools)
            foreach (var e in pool.Entries)
                if (e.ItemName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                    return true;

        // 4. Well-known synonyms: herbs→herb, hides→leather, etc.
        string[] generic = { "herb", "hide", "leather", "ore", "stone", "wood", "bone", "fang", "claw", "scale", "pelt", "iron" };
        foreach (var g in generic)
            if (kw.Contains(g)) return true;

        return false;
    }
}
