using FirstMud.Application;
using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Enums;
using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.EncounterSim;

/// <summary>
/// Runs N deterministic simulations of a combat encounter against one or more
/// monsters (by content id) with a chosen party. Reuses the live combat math
/// via <see cref="CombatSimulationService"/> (a deterministic parallel of
/// <see cref="CombatService"/>'s per-round resolution) seeded from
/// <c>--seed</c>, or <see cref="Random.Shared"/>-derived when absent.
///
/// CLI:
///   encounter-sim --monster &lt;id&gt; [--monster &lt;id&gt; ...]
///                 --party &lt;layer&gt;:&lt;element&gt;[,...]
///                 --rolls N
///                 [--player-level L] [--player-element E]
///                 [--seed S]
/// </summary>
public static class EncounterSimCommand
{
    public static int Run(string[] args)
    {
        var monsterIds = CollectRepeated(args, "--monster");
        var partyCsv   = Args.Value(args, "--party") ?? "";
        var rolls      = Args.IntValue(args, "--rolls") ?? 1000;
        var playerLvl  = Args.IntValue(args, "--player-level") ?? 5;
        var playerElem = ParseElement(Args.Value(args, "--player-element") ?? "Aether");
        int? seed      = Args.IntValue(args, "--seed");

        ConsolePretty.Header(
            $"encounter-sim: monsters=[{string.Join(", ", monsterIds)}] party=[{partyCsv}] rolls={rolls} pl={playerLvl}");

        if (monsterIds.Count == 0)
        {
            ConsolePretty.Error("--monster <id> is required (repeatable).");
            return 1;
        }

        IContentProvider content;
        try
        {
            content = new ContentProvider(RepoRoot.ContentDir());
        }
        catch (Exception ex)
        {
            ConsolePretty.Error($"Could not load ContentProvider: {ex.Message}");
            return 2;
        }

        var monsters = new List<MonsterTemplate>();
        foreach (var id in monsterIds)
        {
            var def = content.GetMonster(id);
            if (def is null)
            {
                ConsolePretty.Error($"Unknown monster id '{id}'.");
                return 3;
            }
            monsters.Add(new MonsterTemplate(def.Name, def.Hp, def.Speed, def.Level, def.Element, def.Abilities));
        }

        var party = ParseParty(partyCsv);

        var masterSeed = seed ?? Random.Shared.Next();
        var results = new List<CombatSimulationService.SimulationResult>(rolls);

        for (int i = 0; i < rolls; i++)
        {
            var rng = new Random(unchecked(masterSeed * 1_000_003 + i));
            var service = new CombatSimulationService(rng);
            var encounter = service.BuildEncounter(playerElem, playerLvl, party, monsters);
            results.Add(service.Run(encounter));
        }

        var summary = Summarize(results);

        ConsolePretty.Info("");
        ConsolePretty.Info($"Rolls           : {rolls}");
        ConsolePretty.Info($"Master seed     : {masterSeed}" + (seed is null ? " (derived)" : " (fixed)"));
        ConsolePretty.Good($"Wins            : {summary.Wins} ({summary.WinRate:P1})");
        ConsolePretty.Warn($"Losses          : {summary.Losses} ({summary.LossRate:P1})");
        ConsolePretty.Warn($"Timeouts        : {summary.Timeouts}");
        ConsolePretty.Info($"Avg rounds      : {summary.AvgRounds:F1}");
        ConsolePretty.Info($"Player HP% end  : avg {summary.AvgPlayerHpPct:P0}  (min {summary.MinPlayerHpPct:P0})");
        ConsolePretty.Info($"Damage taken    : min {summary.DmgMin}  p25 {summary.DmgP25}  med {summary.DmgMedian}  p75 {summary.DmgP75}  max {summary.DmgMax}");
        ConsolePretty.Info($"MVP companion   : {summary.MvpName ?? "(none)"}");
        ConsolePretty.Beat ($"Difficulty      : {summary.Difficulty}  (see tools/design/README.md)");
        ConsolePretty.Info("");

        var log = new SimLog("encounter-sim");
        var jsonPath = log.WriteJson(new
        {
            monsters = monsterIds,
            party = partyCsv,
            rolls,
            playerLevel = playerLvl,
            playerElement = playerElem.ToString(),
            seed = masterSeed,
            seedFixed = seed.HasValue,
            summary
        });
        var mdBody = $"Monsters: `{string.Join(", ", monsterIds)}`. Party `{partyCsv}`. " +
                     $"Rolls={rolls}, pl={playerLvl}, seed={masterSeed}{(seed is null ? "" : " (fixed)")}.\n\n" +
                     $"- Win rate: **{summary.WinRate:P1}** → **{summary.Difficulty}**\n" +
                     $"- Avg rounds: {summary.AvgRounds:F1}\n" +
                     $"- Damage taken p25/med/p75: {summary.DmgP25}/{summary.DmgMedian}/{summary.DmgP75}\n" +
                     $"- MVP companion: {summary.MvpName ?? "(none)"}\n" +
                     $"- JSON: `{Path.GetFileName(jsonPath)}`";
        log.AppendMarkdown($"vs {string.Join(",", monsterIds)} x{rolls}", mdBody);

        return 0;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static List<string> CollectRepeated(string[] args, string flag)
    {
        var list = new List<string>();
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == flag && i + 1 < args.Length)
            {
                list.Add(args[i + 1]);
                i++;
            }
            else if (args[i].StartsWith(flag + "=", StringComparison.Ordinal))
            {
                list.Add(args[i][(flag.Length + 1)..]);
            }
        }
        return list;
    }

    private static MagicElement ParseElement(string s)
        => Enum.TryParse<MagicElement>(s, ignoreCase: true, out var e) ? e : MagicElement.Aether;

    private static List<CombatSimulationService.PartyMember> ParseParty(string csv)
    {
        var list = new List<CombatSimulationService.PartyMember>();
        if (string.IsNullOrWhiteSpace(csv)) return list;

        foreach (var raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = raw.Trim();
            var colon = part.IndexOf(':');
            if (colon < 0) continue;
            if (!int.TryParse(part[..colon].Trim(), out var layer)) continue;
            var elem = ParseElement(part[(colon + 1)..].Trim());

            // Default companion archetype: Wildfolk at level = max(1, layer*2).
            list.Add(new CombatSimulationService.PartyMember(
                CompanionType.Wildfolk, elem, layer, Math.Max(1, layer * 2)));
        }
        return list;
    }

    // ─── Summary stats ──────────────────────────────────────────────────────

    public sealed record Summary(
        int Rolls,
        int Wins,
        int Losses,
        int Timeouts,
        double WinRate,
        double LossRate,
        double AvgRounds,
        double AvgPlayerHpPct,
        double MinPlayerHpPct,
        int DmgMin, int DmgP25, int DmgMedian, int DmgP75, int DmgMax,
        string? MvpName,
        string Difficulty);

    public static Summary Summarize(IReadOnlyList<CombatSimulationService.SimulationResult> results)
    {
        int wins = results.Count(r => r.Outcome == CombatSimulationService.Outcome.Victory);
        int losses = results.Count(r => r.Outcome == CombatSimulationService.Outcome.Defeat);
        int timeouts = results.Count(r => r.Outcome == CombatSimulationService.Outcome.Timeout);
        double winRate = results.Count == 0 ? 0 : (double)wins / results.Count;

        double avgRounds = results.Count == 0 ? 0 : results.Average(r => r.Rounds);

        var winOnly = results.Where(r => r.Outcome == CombatSimulationService.Outcome.Victory).ToList();
        double avgHpPct = winOnly.Count == 0 ? 0 : winOnly.Average(r => (double)r.PlayerHpRemaining / Math.Max(1, r.PlayerMaxHp));
        double minHpPct = winOnly.Count == 0 ? 0 : winOnly.Min(r => (double)r.PlayerHpRemaining / Math.Max(1, r.PlayerMaxHp));

        var dmg = results.Select(r => r.DamageTakenByPlayer).OrderBy(x => x).ToList();
        int P(double q) => dmg.Count == 0 ? 0 : dmg[Math.Clamp((int)(q * (dmg.Count - 1)), 0, dmg.Count - 1)];

        var totals = new Dictionary<string, int>();
        foreach (var r in winOnly)
            foreach (var kv in r.DamageByCompanion)
                totals[kv.Key] = totals.GetValueOrDefault(kv.Key) + kv.Value;
        var mvp = totals.Count == 0 ? null : totals.OrderByDescending(kv => kv.Value).First().Key;

        string difficulty = winRate switch
        {
            > 0.95 => "trivial",
            > 0.85 => "easy",
            > 0.60 => "balanced",
            > 0.30 => "hard",
            _      => "punishing",
        };

        return new Summary(
            results.Count, wins, losses, timeouts,
            winRate, results.Count == 0 ? 0 : (double)losses / results.Count,
            avgRounds, avgHpPct, minHpPct,
            dmg.Count == 0 ? 0 : dmg[0],
            P(0.25), P(0.5), P(0.75),
            dmg.Count == 0 ? 0 : dmg[^1],
            mvp,
            difficulty);
    }
}
