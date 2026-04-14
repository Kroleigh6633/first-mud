using System.Text;
using FirstMud.Application.Content;
using FirstMud.Domain.Enums;
using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.ProgressionSim;

/// <summary>
/// CLI for the progression simulator.
///
///   progression-sim --hours N [--seed S] [--starting-zone ID] [--player-archetype fire|water|earth|balanced]
///                   [--playstyle balanced|combat-heavy|craft-heavy|enchanting-focused]
///                   [--report-pass N]    // writes docs/design/sim-reports/progression-pass-N.md
///                   [--all-playstyles]   // run every playstyle for a comparison table
/// </summary>
public static class ProgressionSimCommand
{
    public static readonly string[] AllPlaystyles = new[]
    {
        "balanced", "combat-heavy", "craft-heavy", "enchanting-focused"
    };

    public static int Run(string[] args)
    {
        var hours      = Args.IntValue(args, "--hours") ?? 24;
        var seed       = Args.IntValue(args, "--seed") ?? 42;
        var startZone  = Args.Value(args, "--starting-zone") ?? "aeldran-3-portmere";
        var archStr    = Args.Value(args, "--player-archetype") ?? "balanced";
        var playstyle  = Args.Value(args, "--playstyle") ?? "balanced";
        var reportPass = Args.IntValue(args, "--report-pass");
        var runAll     = Args.Has(args, "--all-playstyles");

        var archetype = archStr.ToLowerInvariant() switch
        {
            "fire"  => MagicElement.Fire,
            "water" => MagicElement.Water,
            "earth" => MagicElement.Earth,
            _       => MagicElement.Aether,
        };

        ConsolePretty.Header($"progression-sim: --hours {hours} --seed {seed} --playstyle {(runAll ? "ALL" : playstyle)}");

        IContentProvider content;
        try { content = new ContentProvider(RepoRoot.ContentDir()); }
        catch (Exception ex) { ConsolePretty.Error($"ContentProvider: {ex.Message}"); return 2; }

        var playstyles = runAll ? AllPlaystyles : new[] { playstyle };
        var results = new List<ProgressionSimulator.SimResult>();
        foreach (var ps in playstyles)
        {
            var sim = new ProgressionSimulator(content, seed, ps, archetype, startZone);
            var result = sim.Run(hours);
            results.Add(result);
            PrintTable(result);
        }

        // JSON log — use the first result (single run) or a multi-bundle
        var log = new SimLog("progression-sim");
        var jsonPath = log.WriteJson(new
        {
            hours, seed, archetype = archetype.ToString(), startZone,
            playstyles,
            runs = results.Select(r => new
            {
                r.Playstyle,
                hourByHour = r.HourByHour,
                companions = r.Companions,
                finalMaterials = r.FinalMaterials,
                equippedGear = r.EquippedGear,
                r.TotalMaterialsEarned,
                r.TotalMaterialsConsumed,
                actionLogCount = r.ActionLog.Count,
            })
        });
        ConsolePretty.Good($"JSON: {Path.GetFileName(jsonPath)}");

        // Daily markdown append
        var mdLine = string.Join(" | ", results.Select(r =>
            $"{r.Playstyle}: L{r.HourByHour[^1].Level} d{r.HourByHour[^1].ReliableDangerTier}"));
        log.AppendMarkdown($"progression-sim {hours}h",
            $"seed={seed}, playstyles=[{string.Join(",", playstyles)}]. Final: {mdLine}. JSON: `{Path.GetFileName(jsonPath)}`");

        // Full report
        if (reportPass is int pass)
        {
            var reportPath = WriteReport(pass, hours, seed, archetype, startZone, results, content);
            ConsolePretty.Good($"Report: {reportPath}");
        }

        return 0;
    }

    private static void PrintTable(ProgressionSimulator.SimResult r)
    {
        ConsolePretty.Info("");
        ConsolePretty.Beat($"Playstyle: {r.Playstyle}");
        ConsolePretty.Info("Hour | Lvl | Craft | Salv | CompAvg | EnchMat | Gold | TopGear | Danger");
        ConsolePretty.Info("-----+-----+-------+------+---------+---------+------+---------+-------");
        // Print every hour up to 24, then every 2 hours, then every 4
        foreach (var h in r.HourByHour)
        {
            bool show = h.Hour <= 24 || (h.Hour <= 48 && h.Hour % 2 == 0) || h.Hour % 4 == 0 || h.Hour == r.HourByHour[^1].Hour;
            if (!show) continue;
            ConsolePretty.Info(
                $"{h.Hour,4} | {h.Level,3} | {h.CraftingSkill,5} | {h.SalvageSkill,4} | {h.AvgCompanionLayer,7:F2} | {h.EnchantingMats,7} | {h.Gold,4} | {h.TopGearWorkmanship,7} | d{h.ReliableDangerTier}");
        }
        ConsolePretty.Info("");
    }

    // ─── Report writer ──────────────────────────────────────────────────────

    public static string WriteReport(
        int pass, int hours, int seed, MagicElement archetype, string startZone,
        IReadOnlyList<ProgressionSimulator.SimResult> results,
        IContentProvider content)
    {
        var root = RepoRoot.Find();
        var dir = Path.Combine(root, "docs", "design", "sim-reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"progression-pass-{pass}.md");

        var sb = new StringBuilder();
        sb.AppendLine($"# Progression Sim — Pass {pass}");
        sb.AppendLine();
        sb.AppendLine($"- Hours: **{hours}**  Seed: **{seed}**  Archetype: **{archetype}**  Start zone: **{startZone}**");
        sb.AppendLine($"- Playstyles: {string.Join(", ", results.Select(r => $"`{r.Playstyle}`"))}");
        sb.AppendLine();

        // Summary table
        sb.AppendLine("## Summary (final hour)");
        sb.AppendLine();
        sb.AppendLine("| Playstyle | Lvl | Craft | Salv | AvgCompLayer | EnchMat | TopGear | ReliableDanger |");
        sb.AppendLine("|-----------|-----|-------|------|--------------|---------|---------|----------------|");
        foreach (var r in results)
        {
            var h = r.HourByHour[^1];
            sb.AppendLine($"| {r.Playstyle} | {h.Level} | {h.CraftingSkill} | {h.SalvageSkill} | {h.AvgCompanionLayer:F2} | {h.EnchantingMats} | {h.TopGearWorkmanship} | d{h.ReliableDangerTier} |");
        }
        sb.AppendLine();

        // Per-hour danger progression (balanced)
        var baseline = results.FirstOrDefault(r => r.Playstyle == "balanced") ?? results[0];
        sb.AppendLine("## Reliable-Danger curve (by hour, baseline playstyle)");
        sb.AppendLine();
        sb.AppendLine($"Playstyle: `{baseline.Playstyle}`");
        sb.AppendLine();
        sb.AppendLine("| Hour | Lvl | Danger |");
        sb.AppendLine("|------|-----|--------|");
        foreach (var h in baseline.HourByHour)
        {
            if (h.Hour == 1 || h.Hour % 5 == 0 || h.Hour == baseline.HourByHour[^1].Hour)
                sb.AppendLine($"| {h.Hour} | {h.Level} | d{h.ReliableDangerTier} |");
        }
        sb.AppendLine();

        // Bottleneck detection
        sb.AppendLine("## Bottleneck detection");
        sb.AppendLine();
        foreach (var r in results)
        {
            sb.AppendLine($"### `{r.Playstyle}`");
            sb.AppendLine();
            var bottlenecks = DetectBottlenecks(r);
            if (bottlenecks.Count == 0)
            {
                sb.AppendLine("- (none detected within sim horizon)");
            }
            else
            {
                foreach (var b in bottlenecks)
                    sb.AppendLine($"- **{b.Name}** — first choke at hour {b.Hour}: {b.Reason}");
            }
            sb.AppendLine();
        }

        // Proposed data tunes
        sb.AppendLine("## Proposed data tunes");
        sb.AppendLine();
        var tunes = ProposeTunes(results, content);
        foreach (var t in tunes) sb.AppendLine($"- {t}");
        sb.AppendLine();

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    public sealed record Bottleneck(string Name, int Hour, string Reason);

    public static List<Bottleneck> DetectBottlenecks(ProgressionSimulator.SimResult r)
    {
        var list = new List<Bottleneck>();

        // Enchanting materials: never hits 5 within sim
        var enchHour = r.HourByHour.FirstOrDefault(h => h.EnchantingMats >= 5);
        if (enchHour is null)
            list.Add(new Bottleneck("enchanting-materials", r.Hours,
                $"enchanting-mat pool never reached 5 in {r.Hours}h (final={r.HourByHour[^1].EnchantingMats}). Drop weights too thin."));

        // Workmanship-5+ gear: check when top gear first hits 5
        var gearHour = r.HourByHour.FirstOrDefault(h => h.TopGearWorkmanship >= 5);
        if (gearHour is null)
            list.Add(new Bottleneck("workmanship-5-gear", r.Hours,
                $"no equipped item reached workmanship 5 in {r.Hours}h (final={r.HourByHour[^1].TopGearWorkmanship}). CraftingSkill/20 bonus too slow."));

        // Companion layer 3+: when does avg first hit 3?
        var layerHour = r.HourByHour.FirstOrDefault(h => h.AvgCompanionLayer >= 3);
        if (layerHour is null)
            list.Add(new Bottleneck("companion-layer-3", r.Hours,
                $"avg companion layer never reached 3 in {r.Hours}h (final={r.HourByHour[^1].AvgCompanionLayer:F2}). Usage-per-combat accrues too slowly."));

        // Danger-5 ceiling
        var d5Hour = r.HourByHour.FirstOrDefault(h => h.ReliableDangerTier >= 5);
        if (d5Hour is null)
            list.Add(new Bottleneck("danger-5-ceiling", r.Hours,
                $"never reached reliable danger 5 in {r.Hours}h (final=d{r.HourByHour[^1].ReliableDangerTier}). CONFIRMS user's Pass-13 playtest claim."));

        return list;
    }

    public static List<string> ProposeTunes(
        IReadOnlyList<ProgressionSimulator.SimResult> results, IContentProvider content)
    {
        var tunes = new List<string>();
        bool enchStarved = results.Any(r => r.HourByHour[^1].EnchantingMats < 5);
        bool gearStarved = results.Any(r => r.HourByHour[^1].TopGearWorkmanship < 5);
        bool layerStarved = results.Any(r => r.HourByHour[^1].AvgCompanionLayer < 3);
        bool dangerCapped = results.Any(r => r.HourByHour[^1].ReliableDangerTier < 5);

        if (enchStarved)
            tunes.Add("`loot-tables.json`: bump `Dravenite Dust` and `Wyrd Shard` drop weight from `1` to `3` in `biome-forest`, `biome-wyrd`, and `biome-swamp` pools.");
        if (enchStarved)
            tunes.Add("`loot-tables.json`: raise `independentRolls[id=taper-drop].chancePercent` from `15` to `25` so enchanting-focused play produces tapers faster.");
        if (gearStarved)
            tunes.Add("`CraftingService.CalculateWorkmanship`: divisor `craftingSkill / 20` → `craftingSkill / 10` (doubles skill contribution to workmanship).");
        if (gearStarved)
            tunes.Add("`recipes.json`: raise `baseWorkmanshipMax` for tier-2+ recipes (Iron Helm/Greaves/Vambraces) from `6` to `8` so mid-skill crafts can hit workmanship 5-6.");
        if (layerStarved)
            tunes.Add("`Companion.LayerThresholds[Wildfolk]`: compress from `[0,200,500,1000,2000,4000]` to `[0,100,300,700,1400,2800]` — roughly 1.5x faster layer gain for the starter companion type.");
        if (dangerCapped)
            tunes.Add("`combat-curves.json`: reduce `monsterScaling.hpPerDanger` from `0.4` to `0.3` — current curve makes danger 5+ a 3x HP wall the player can't out-DPS until gear workmanship catches up.");
        if (dangerCapped)
            tunes.Add("`combat-curves.json`: reduce `monsterScaling.powerPerDanger` from `0.3` to `0.2` — enemy damage at danger 5+ outpaces player HP gain (50 + 5*lvl).");

        if (tunes.Count == 0)
            tunes.Add("No data tunes proposed — sim met all baseline thresholds in the horizon.");
        return tunes;
    }
}
