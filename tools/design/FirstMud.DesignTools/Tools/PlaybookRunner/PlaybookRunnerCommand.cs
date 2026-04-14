using System.Text;
using System.Text.Json;
using FirstMud.Application.Content;
using FirstMud.DesignTools.Shared;

namespace FirstMud.DesignTools.Tools.PlaybookRunner;

/// <summary>
/// CLI for the balance playbook harness.
///
/// Usage:
///   playbook-runner --playbook &lt;id-or-path&gt; [--seed S] [--compare-to &lt;json-log-path&gt;]
///                   [--playbooks-dir &lt;dir&gt;]
///
/// Loads the named playbook (by id under <c>tools/design/playbooks/</c>, or by
/// explicit path), runs every cell of its axis grid through the combat simulator,
/// classifies each cell into a viability band, and reports divergences from
/// the playbook's expected band.
/// </summary>
public static class PlaybookRunnerCommand
{
    public static int Run(string[] args)
    {
        if (Args.Has(args, "--help") || Args.Has(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        var idOrPath = Args.Value(args, "--playbook");
        if (string.IsNullOrWhiteSpace(idOrPath))
        {
            ConsolePretty.Error("--playbook <id-or-path> is required.");
            PrintHelp();
            return 1;
        }

        int? seedOverride = Args.IntValue(args, "--seed");
        var compareToPath = Args.Value(args, "--compare-to");
        var playbooksDir  = Args.Value(args, "--playbooks-dir") ?? DefaultPlaybooksDir();

        Playbook playbook;
        string playbookPath;
        try
        {
            playbookPath = ResolvePlaybookPath(idOrPath, playbooksDir);
            playbook = LoadPlaybook(playbookPath);
        }
        catch (Exception ex)
        {
            ConsolePretty.Error($"Failed to load playbook: {ex.Message}");
            return 2;
        }

        IContentProvider content;
        try { content = new ContentProvider(RepoRoot.ContentDir()); }
        catch (Exception ex) { ConsolePretty.Error($"Could not load content: {ex.Message}"); return 2; }

        ConsolePretty.Header($"playbook-runner: {playbook.Id} — {playbook.DisplayName}");
        ConsolePretty.Info(playbook.Description);
        ConsolePretty.Info($"kind={playbook.Kind}  rolls/cell={playbook.Rolls}  seed={seedOverride ?? playbook.Seed}  axes=[{string.Join(", ", playbook.Axes.Select(a => a.Name + ":" + a.Values.Length))}]");

        if (string.Equals(playbook.Kind, "crafting", StringComparison.OrdinalIgnoreCase))
            return RunCrafting(playbook, seedOverride);

        var engine = new PlaybookEngine(content);
        var result = engine.Execute(playbook, seedOverride);

        // Table
        ConsolePretty.Info("");
        ConsolePretty.Info(RenderTable(result));
        ConsolePretty.Info("");

        if (result.Divergences.Count > 0)
        {
            ConsolePretty.Warn($"{result.Divergences.Count} cell(s) diverged from expectation:");
            foreach (var c in result.Divergences)
                ConsolePretty.Warn($"  {Describe(c)}  expected={c.ExpectedBand}  actual={c.ActualBand}  winRate={c.Summary.WinRate:P1}");
        }
        else
        {
            ConsolePretty.Good("All cells on-band.");
        }

        // Optional diff against prior log
        if (!string.IsNullOrWhiteSpace(compareToPath))
        {
            try
            {
                var diff = CompareToPriorRun(compareToPath!, result);
                ConsolePretty.Info("");
                ConsolePretty.Info(diff);
            }
            catch (Exception ex)
            {
                ConsolePretty.Error($"--compare-to failed: {ex.Message}");
            }
        }

        // Write outputs
        var log = new SimLog("playbook-runner");
        var payload = new
        {
            playbookId = playbook.Id,
            displayName = playbook.DisplayName,
            seed = result.Seed,
            rolls = result.Rolls,
            cells = result.Cells.Select(c => new
            {
                axis = (object?)c.AxisDisplay ?? c.AxisValues,
                axisInt = c.AxisValues,
                winRate = c.Summary.WinRate,
                avgRounds = c.Summary.AvgRounds,
                avgHpPct = c.Summary.AvgPlayerHpPct,
                actual = c.ActualBand,
                expected = c.ExpectedBand,
                divergent = c.Divergent,
            }),
            divergences = result.Divergences.Select(c => new { axis = (object?)c.AxisDisplay ?? c.AxisValues, expected = c.ExpectedBand, actual = c.ActualBand, winRate = c.Summary.WinRate }),
        };
        var jsonPath = log.WriteJson(payload);
        var md = $"Playbook **{playbook.Id}** — {playbook.DisplayName}. " +
                 $"Cells={result.Cells.Count}, divergent={result.Divergences.Count}. " +
                 $"Seed={result.Seed}, rolls/cell={result.Rolls}. " +
                 $"JSON: `{Path.GetFileName(jsonPath)}`\n\n" +
                 "```\n" + RenderTable(result) + "\n```";
        log.AppendMarkdown($"playbook {playbook.Id}", md);

        return result.Divergences.Count == 0 ? 0 : 3;
    }

    private static int RunCrafting(Playbook playbook, int? seedOverride)
    {
        var evaluator = new CraftingEvaluator();
        var result = evaluator.Execute(playbook, seedOverride);

        ConsolePretty.Info("");
        ConsolePretty.Info(CraftingEvaluator.RenderTable(result));
        ConsolePretty.Info("");

        if (result.Divergences.Count > 0)
        {
            ConsolePretty.Warn($"{result.Divergences.Count} cell(s) diverged from expected distribution:");
            foreach (var c in result.Divergences)
                ConsolePretty.Warn($"  {{{string.Join(", ", c.AxisValues.Select(kv => kv.Key + "=" + kv.Value))}}}  {string.Join("; ", c.Divergences)}");
        }
        else
        {
            ConsolePretty.Good("All cells within expected outcome bands.");
        }

        var log = new SimLog("playbook-runner");
        var payload = new
        {
            playbookId = playbook.Id,
            kind = "crafting",
            displayName = playbook.DisplayName,
            seed = result.Seed,
            rolls = result.Rolls,
            cells = result.Cells.Select(c => new
            {
                axis = c.AxisValues,
                outcomes = c.OutcomePercentages,
                totalRolls = c.TotalRolls,
                quantitiesMatchedSample = c.QuantitiesMatchedSample,
                divergences = c.Divergences,
            }),
        };
        var jsonPath = log.WriteJson(payload);
        var md = $"Playbook **{playbook.Id}** (crafting) — cells={result.Cells.Count} divergent={result.Divergences.Count}. " +
                 $"Seed={result.Seed} rolls/cell={result.Rolls}. JSON: `{Path.GetFileName(jsonPath)}`\n\n" +
                 "```\n" + CraftingEvaluator.RenderTable(result) + "\n```";
        log.AppendMarkdown($"playbook {playbook.Id}", md);

        return result.Divergences.Count == 0 ? 0 : 3;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static string DefaultPlaybooksDir()
        => Path.Combine(RepoRoot.Find(), "tools", "design", "playbooks");

    public static string ResolvePlaybookPath(string idOrPath, string playbooksDir)
    {
        if (File.Exists(idOrPath)) return idOrPath;
        var byId = Path.Combine(playbooksDir, idOrPath + ".json");
        if (File.Exists(byId)) return byId;
        throw new FileNotFoundException($"Could not find playbook '{idOrPath}' (looked at '{idOrPath}' and '{byId}').");
    }

    public static Playbook LoadPlaybook(string path)
    {
        var json = File.ReadAllText(path);
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        var pb = JsonSerializer.Deserialize<Playbook>(json, opts)
                 ?? throw new InvalidOperationException($"Playbook at '{path}' deserialized to null.");
        if (string.IsNullOrWhiteSpace(pb.Id))
            throw new InvalidOperationException($"Playbook at '{path}' has no id.");
        if (pb.Axes.Count == 0)
            throw new InvalidOperationException($"Playbook '{pb.Id}' has no axes.");
        var kind = string.IsNullOrWhiteSpace(pb.Kind) ? "combat" : pb.Kind.ToLowerInvariant();
        if (kind == "combat" && pb.ToleranceBands.Count == 0)
            throw new InvalidOperationException($"Playbook '{pb.Id}' (kind=combat) has no toleranceBands.");
        if (kind == "crafting" && pb.ExpectedDistribution.Cells.Count == 0)
            throw new InvalidOperationException($"Playbook '{pb.Id}' (kind=crafting) has no expectedDistribution.cells.");
        return pb;
    }

    public static string RenderTable(PlaybookEngine.PlaybookRunResult result)
    {
        var sb = new StringBuilder();
        if (result.Cells.Count == 0) return "(no cells)";

        var axisNames = result.Cells[0].AxisValues.Keys.ToArray();
        // Column width tuned for zone-id strings (e.g. "aeldran-1-caervorn-highlands").
        // Numeric axes still fit fine at 30 chars padding.
        sb.Append(string.Join(" | ", axisNames.Select(n => n.PadRight(30))));
        sb.AppendLine(" | winRate | rounds | actual    | expected  | !");

        foreach (var c in result.Cells)
        {
            // Prefer AxisDisplay (carries original string form of string-valued
            // axes) over AxisValues (ints, hashed for string axes). Keeps the
            // table readable for flow playbooks like auto-quest-completion that
            // axis over zone-id strings.
            foreach (var n in axisNames)
            {
                var display = c.AxisDisplay is not null && c.AxisDisplay.TryGetValue(n, out var ds)
                    ? ds
                    : c.AxisValues[n].ToString();
                sb.Append(display.PadRight(30) + " | ");
            }
            sb.Append($"{c.Summary.WinRate,7:P1} | ");
            sb.Append($"{c.Summary.AvgRounds,6:F1} | ");
            sb.Append($"{c.ActualBand.PadRight(9)} | ");
            sb.Append($"{(string.IsNullOrEmpty(c.ExpectedBand) ? "(none)" : c.ExpectedBand).PadRight(9)} | ");
            sb.AppendLine(c.Divergent ? "DIVERGE" : "");
        }
        return sb.ToString();
    }

    private static string Describe(IReadOnlyDictionary<string, int> axis)
        => "{" + string.Join(", ", axis.Select(kv => $"{kv.Key}={kv.Value}")) + "}";

    private static string Describe(PlaybookEngine.CellResult c)
    {
        if (c.AxisDisplay is null) return Describe(c.AxisValues);
        return "{" + string.Join(", ", c.AxisValues.Keys.Select(k =>
        {
            var v = c.AxisDisplay.TryGetValue(k, out var ds) ? ds : c.AxisValues[k].ToString();
            return $"{k}={v}";
        })) + "}";
    }

    public static string CompareToPriorRun(string priorJsonPath, PlaybookEngine.PlaybookRunResult current)
    {
        if (!File.Exists(priorJsonPath))
            throw new FileNotFoundException($"Prior run not found: {priorJsonPath}");

        using var doc = JsonDocument.Parse(File.ReadAllText(priorJsonPath));
        var priorCells = doc.RootElement.GetProperty("cells");

        var priorByKey = new Dictionary<string, (string actual, double winRate)>();
        foreach (var cell in priorCells.EnumerateArray())
        {
            var axis = cell.GetProperty("axis");
            var key = AxisKey(axis);
            var actual = cell.GetProperty("actual").GetString() ?? "";
            var wr = cell.GetProperty("winRate").GetDouble();
            priorByKey[key] = (actual, wr);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Compare to: {Path.GetFileName(priorJsonPath)}");
        int changed = 0;
        foreach (var c in current.Cells)
        {
            var key = AxisKey(c.AxisValues);
            if (!priorByKey.TryGetValue(key, out var prior)) continue;
            var bandShift = prior.actual != c.ActualBand;
            var wrDelta = c.Summary.WinRate - prior.winRate;
            if (bandShift || Math.Abs(wrDelta) >= 0.05)
            {
                changed++;
                sb.AppendLine(
                    $"  {Describe(c.AxisValues)}  {prior.actual} ({prior.winRate:P1}) -> {c.ActualBand} ({c.Summary.WinRate:P1})  Δ={wrDelta:+0.0%;-0.0%;0.0%}"
                    + (bandShift ? "  [BAND SHIFT]" : ""));
            }
        }
        if (changed == 0) sb.AppendLine("  (no cells changed band or >5% win-rate delta)");
        return sb.ToString();
    }

    private static string AxisKey(JsonElement axis)
    {
        var parts = new List<string>();
        foreach (var prop in axis.EnumerateObject())
        {
            var v = prop.Value.ValueKind switch
            {
                JsonValueKind.Number => prop.Value.GetInt32().ToString(),
                JsonValueKind.String => prop.Value.GetString() ?? "",
                _ => prop.Value.ToString(),
            };
            parts.Add($"{prop.Name}={v}");
        }
        parts.Sort(StringComparer.Ordinal);
        return string.Join(",", parts);
    }

    private static string AxisKey(IReadOnlyDictionary<string, int> axis)
    {
        var parts = axis.Select(kv => $"{kv.Key}={kv.Value}").ToList();
        parts.Sort(StringComparer.Ordinal);
        return string.Join(",", parts);
    }

    private static void PrintHelp()
    {
        Console.WriteLine("Usage: FirstMud.DesignTools playbook-runner --playbook <id-or-path> [--seed S] [--compare-to <json>] [--playbooks-dir <dir>]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --playbook <id>           Playbook id (resolved under tools/design/playbooks/) or explicit JSON path.");
        Console.WriteLine("  --seed <int>              Override playbook seed.");
        Console.WriteLine("  --compare-to <json>       Prior run JSON log; report cells whose band shifted or win-rate changed >=5%.");
        Console.WriteLine("  --playbooks-dir <dir>     Override default lookup directory.");
        Console.WriteLine("  -h, --help                Show this help.");
        Console.WriteLine();
        Console.WriteLine("Exit codes: 0=all on-band, 1=arg error, 2=load error, 3=one or more cells diverged.");
    }
}
