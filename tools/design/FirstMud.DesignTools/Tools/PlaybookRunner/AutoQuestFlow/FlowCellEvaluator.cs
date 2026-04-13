using FirstMud.Application.Content;

namespace FirstMud.DesignTools.Tools.PlaybookRunner.AutoQuestFlow;

/// <summary>
/// <see cref="PlaybookEngine"/> cell-evaluator for <c>kind: "flow"</c> playbooks.
/// Iterates the axis grid, runs <see cref="AutoQuestSimulationService"/> per cell,
/// rolls (to smooth ordering variance), and tallies completion / stall metrics.
///
/// Metrics surface:
///   completionRate = completed / accepted   (percent)
///   stallRate      = stalled   / accepted   (percent)
///   avgMinutesPerQuest  (wall-clock virtual minutes per accepted quest)
/// </summary>
public sealed class FlowCellEvaluator
{
    private readonly IContentProvider _content;
    public FlowCellEvaluator(IContentProvider content) { _content = content; }

    public PlaybookEngine.PlaybookRunResult Execute(Playbook playbook, int? seedOverride)
    {
        var seed = seedOverride ?? playbook.Seed;
        var cells = new List<PlaybookEngine.CellResult>();

        foreach (var combo in PlaybookEngine.CartesianAny(playbook.Axes))
        {
            var summary = RunCell(playbook, combo);
            // Flow playbooks repurpose Summary.WinRate to hold completion rate
            // so the existing JSON log shape keeps working. ActualBand /
            // ExpectedBand are left blank — flow cells diverge when metrics
            // fall outside expectedMetrics ranges, not when bands shift.
            var expected = LookupExpectedMetric(playbook, combo);
            var divergent = IsDivergent(summary, expected);
            // Coerce the object-valued combo into an int dict for the shared
            // CellResult type; string-valued axes are hashed into the key but
            // their display value is carried on the summary narrative below.
            var intCombo = ComboToIntKeyed(combo);
            cells.Add(new PlaybookEngine.CellResult(
                intCombo,
                summary.AsEncounterSummary(),
                ActualBand: summary.ActualLabel,
                ExpectedBand: expected?.Label ?? "",
                Divergent: divergent));
        }

        var divergences = cells.Where(c => c.Divergent).ToList();
        return new PlaybookEngine.PlaybookRunResult(playbook.Id, seed, playbook.Rolls, cells, divergences);
    }

    public sealed record FlowCellSummary(
        double CompletionRate,
        double StallRate,
        double AvgMinutesPerQuest,
        int Accepted,
        int Completed,
        int Stalled,
        int TimedOut,
        Dictionary<string, int> StallsByReason,
        List<string> NeverCompleted,
        string ActualLabel)
    {
        public EncounterSim.EncounterSimCommand.Summary AsEncounterSummary()
            => new(
                Rolls:          Accepted,
                Wins:           Completed,
                Losses:         Stalled,
                Timeouts:       TimedOut,
                WinRate:        CompletionRate,
                LossRate:       StallRate,
                AvgRounds:      AvgMinutesPerQuest,
                AvgPlayerHpPct: StallRate,
                MinPlayerHpPct: 0,
                DmgMin:         0, DmgP25: 0, DmgMedian: 0, DmgP75: 0, DmgMax: 0,
                MvpName:        null,
                Difficulty:     ActualLabel);
    }

    private FlowCellSummary RunCell(Playbook pb, IReadOnlyDictionary<string, object> combo)
    {
        // Resolve axis values.
        var startZoneId = AxisString(combo, "startZoneId") ?? "aeldran-7-starting-road";
        var poolSize    = AxisInt(combo, "questPoolSize") ?? 3;

        var svc = new AutoQuestSimulationService(_content);
        var runs = svc.RunZone(startZoneId, poolSize, pb.SimulatedMinutes);

        int completed = runs.Count(r => r.Outcome == AutoQuestSimulationService.Outcome.Completed);
        int timedOut  = runs.Count(r => r.Outcome == AutoQuestSimulationService.Outcome.Timeout);
        int stalled   = runs.Count - completed - timedOut;
        int accepted  = runs.Count;
        var byReason  = runs
            .Where(r => r.Outcome != AutoQuestSimulationService.Outcome.Completed
                     && r.Outcome != AutoQuestSimulationService.Outcome.Timeout)
            .GroupBy(r => r.Outcome.ToString())
            .ToDictionary(g => g.Key, g => g.Count());
        var neverCompleted = runs
            .Where(r => r.Outcome != AutoQuestSimulationService.Outcome.Completed)
            .Select(r => $"{r.QuestId}:{r.Outcome}")
            .ToList();
        var avgMinutes = completed > 0
            ? runs.Where(r => r.Outcome == AutoQuestSimulationService.Outcome.Completed).Average(r => r.Minutes)
            : 0.0;
        double completionRate = accepted > 0 ? (double)completed / accepted : 0.0;
        double stallRate      = accepted > 0 ? (double)stalled   / accepted : 0.0;

        var label = completionRate switch
        {
            >= 0.95 => "healthy",
            >= 0.70 => "rough",
            >= 0.30 => "broken",
            _       => "blocked",
        };

        return new FlowCellSummary(
            completionRate, stallRate, avgMinutes,
            accepted, completed, stalled, timedOut,
            byReason, neverCompleted, label);
    }

    // ─── Expected-metric lookup ──────────────────────────────────────────────

    private sealed record ExpectedMetric(string Label, double[] CompletionRate, double[] StallRate);

    private static ExpectedMetric? LookupExpectedMetric(Playbook pb, IReadOnlyDictionary<string, object> combo)
    {
        if (pb.ExpectedMetrics is null) return null;
        foreach (var cell in pb.ExpectedMetrics.Cells)
        {
            bool match = true;
            foreach (var kv in combo)
            {
                var want = cell[kv.Key];
                if (kv.Value is int i)
                {
                    if (want.ValueKind != System.Text.Json.JsonValueKind.Number || !want.TryGetInt32(out var j) || i != j) { match = false; break; }
                }
                else if (kv.Value is string s)
                {
                    if (want.ValueKind != System.Text.Json.JsonValueKind.String || want.GetString() != s) { match = false; break; }
                }
            }
            if (!match) continue;
            return new ExpectedMetric(
                Label: cell.String("label") ?? "",
                CompletionRate: cell.Range("completionRate") ?? new[] { 0.0, 100.0 },
                StallRate: cell.Range("stallRate") ?? new[] { 0.0, 100.0 });
        }
        return null;
    }

    private static bool IsDivergent(FlowCellSummary s, ExpectedMetric? e)
    {
        if (e is null) return false;
        var pct = s.CompletionRate * 100.0;
        var stallPct = s.StallRate * 100.0;
        bool crOk = pct >= e.CompletionRate[0] && pct <= e.CompletionRate[1];
        bool srOk = stallPct >= e.StallRate[0] && stallPct <= e.StallRate[1];
        return !(crOk && srOk);
    }

    // ─── Combo helpers ───────────────────────────────────────────────────────

    private static string? AxisString(IReadOnlyDictionary<string, object> combo, string name)
        => combo.TryGetValue(name, out var v) && v is string s ? s : null;
    private static int? AxisInt(IReadOnlyDictionary<string, object> combo, string name)
        => combo.TryGetValue(name, out var v) && v is int i ? i : null;

    // CellResult's AxisValues is IReadOnlyDictionary<string,int>; string axes are
    // hashed down to a stable int so table rendering still works. Consumers that
    // need the original string use the JSON log (written separately) and
    // FlowReport.RenderTable.
    private static IReadOnlyDictionary<string, int> ComboToIntKeyed(IReadOnlyDictionary<string, object> combo)
    {
        var d = new Dictionary<string, int>(combo.Count);
        foreach (var kv in combo)
            d[kv.Key] = kv.Value is int i ? i : System.Math.Abs(kv.Value?.ToString()?.GetHashCode() ?? 0) % 100000;
        return d;
    }
}
