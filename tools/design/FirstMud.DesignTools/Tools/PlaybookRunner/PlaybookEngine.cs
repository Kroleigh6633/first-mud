using FirstMud.Application;
using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Enums;

namespace FirstMud.DesignTools.Tools.PlaybookRunner;

/// <summary>
/// Executes a <see cref="Playbook"/> against <see cref="CombatSimulationService"/>.
/// Iterates the cartesian product of playbook axes, builds a loadout per cell
/// from holdouts + axis values, runs N combat rolls, and classifies each cell
/// into a viability band per the playbook's tolerance ranges.
///
/// Axis-to-loadout mapping (pragmatic proxies until gear/imbue land in the sim):
///   gearTier       → playerLevel += gearTier * 2
///   imbueLevel     → playerLevel += imbueLevel (1 imbue ≈ 1 effective level)
///   playerLevel    → overrides holdout playerLevel
///   dangerLevel    → MonsterScaling.Apply(...) on the chosen monster template
///   companionCount → N wildfolk companions at companionLayer
///   companionLayer → layer for all companions
/// </summary>
public sealed class PlaybookEngine
{
    private static readonly string[] DefaultMonsterPerTier = new[]
    {
        "timber-wolf",   // tier 0
        "dire-bear",     // tier 2-ish hp/level proxy for low danger
        "frost-giant",   // tier 3
    };

    private readonly IContentProvider _content;
    public PlaybookEngine(IContentProvider content) { _content = content; }

    public sealed record CellResult(
        IReadOnlyDictionary<string, int> AxisValues,
        EncounterSim.EncounterSimCommand.Summary Summary,
        string ActualBand,
        string ExpectedBand,
        bool Divergent);

    public sealed record PlaybookRunResult(
        string PlaybookId,
        int Seed,
        int Rolls,
        IReadOnlyList<CellResult> Cells,
        IReadOnlyList<CellResult> Divergences);

    public PlaybookRunResult Execute(Playbook playbook, int? seedOverride = null)
    {
        var seed = seedOverride ?? playbook.Seed;
        var cells = new List<CellResult>();

        foreach (var (combo, idx) in CartesianWithIndices(playbook.Axes))
        {
            var cellSalt = DeterministicCellSalt(idx);
            var summary = RunCell(playbook, combo, seed, cellSalt);
            var actual  = Classify(summary.WinRate, playbook.ToleranceBands);
            var expected = LookupExpected(playbook, combo);
            cells.Add(new CellResult(combo, summary, actual, expected, actual != expected && expected.Length > 0));
        }

        var divergences = cells.Where(c => c.Divergent).ToList();
        return new PlaybookRunResult(playbook.Id, seed, playbook.Rolls, cells, divergences);
    }

    /// <summary>
    /// Deterministic per-cell salt from the axis-index tuple. No string hashing —
    /// <c>string.GetHashCode()</c> is randomized per-process in .NET 6+, which
    /// would make the same playbook+seed produce different results across runs.
    /// This folds the index at each axis into a stable integer using a
    /// Cantor-style positional mix.
    /// </summary>
    public static int DeterministicCellSalt(int[] cellIndexPerAxis)
    {
        int salt = 0;
        for (int a = 0; a < cellIndexPerAxis.Length; a++)
        {
            // Prime-weighted positional mix. All arithmetic is deterministic.
            salt = unchecked(salt * 1_000_003 + (cellIndexPerAxis[a] + 1) * (a + 1) * 257);
        }
        return salt;
    }

    // ─── Loadout construction ───────────────────────────────────────────────

    private EncounterSim.EncounterSimCommand.Summary RunCell(
        Playbook pb, IReadOnlyDictionary<string, int> axisValues, int masterSeed, int cellSalt)
    {
        int playerLevel = pb.Holdouts.PlayerLevel ?? 5;
        if (axisValues.TryGetValue("playerLevel", out var pl)) playerLevel = pl;
        playerLevel = Math.Max(1, playerLevel);

        // Gear and imbue are now handled DISTINCTLY via CombatContext, not as
        // flat level bonuses. This is the heart of Fix #2 from the pass #7
        // follow-ups: `gear-only` and `imbue-only` playbooks must not collapse
        // into the player-level curve.
        int gearTier   = axisValues.TryGetValue("gearTier",   out var gt) ? gt : (pb.Holdouts.GearTier   ?? 0);
        int imbueLevel = axisValues.TryGetValue("imbueLevel", out var il) ? il : (pb.Holdouts.ImbueLevel ?? 0);

        var playerElement = ParseElement(pb.Holdouts.PlayerElement ?? "Aether");

        // Danger level: axis or holdout, default 0 (unscaled).
        int dangerLevel = axisValues.TryGetValue("dangerLevel", out var dl) ? dl
                        : (pb.Holdouts.DangerLevel ?? 0);
        dangerLevel = Math.Clamp(dangerLevel, 0, 10);

        // Party composition
        var party = BuildParty(pb, axisValues);

        // Pack size: how many enemies. Either via axis, or a single monster.
        int packSize = axisValues.TryGetValue("packSize", out var ps) ? Math.Max(1, ps) : 1;

        // Monster selection: fixed via holdout, else pick by (danger tier proxy).
        var monsterId = pb.Holdouts.MonsterId ?? PickRepresentative(dangerLevel);
        var def = _content.GetMonster(monsterId)
            ?? throw new InvalidOperationException($"Unknown monster id '{monsterId}'.");
        var baseTemplate = new MonsterTemplate(def.Name, def.Hp, def.Speed, def.Level, def.Element, def.Abilities);
        var scaled = MonsterScaling.Apply(baseTemplate, dangerLevel, _content.CombatCurves.MonsterScaling, isBoss: false);
        var enemies = Enumerable.Repeat(scaled, packSize).ToArray();

        // partySize axis: override companion count (overrides holdouts)
        if (axisValues.TryGetValue("partySize", out var partySize))
        {
            // partySize = 1 means solo player, 2 = player + 1 companion, etc.
            int companionsWanted = Math.Max(0, partySize - 1);
            var layer = axisValues.TryGetValue("companionLayer", out var cl) ? cl : (pb.Holdouts.CompanionLayer ?? 1);
            party = Enumerable.Range(0, companionsWanted)
                .Select(i => new CombatSimulationService.PartyMember(
                    CompanionType.Wildfolk,
                    (MagicElement)(i % 5),
                    layer,
                    Math.Max(1, layer * 2)))
                .ToList();
        }

        var results = new List<CombatSimulationService.SimulationResult>(pb.Rolls);
        for (int i = 0; i < pb.Rolls; i++)
        {
            var rng = new Random(unchecked(masterSeed * 1_000_003 + cellSalt * 97 + i));
            var svc = new CombatSimulationService(rng);
            var ctx = new CombatSimulationService.CombatContext(
                playerLevel, playerElement, gearTier, imbueLevel, party, enemies);
            var enc = svc.BuildEncounter(ctx);
            results.Add(svc.Run(enc));
        }

        return EncounterSim.EncounterSimCommand.Summarize(results);
    }

    private static List<CombatSimulationService.PartyMember> BuildParty(
        Playbook pb, IReadOnlyDictionary<string, int> axisValues)
    {
        // Axis-driven companion party
        int? count = axisValues.TryGetValue("companionCount", out var cc) ? cc : pb.Holdouts.CompanionCount;
        int? layer = axisValues.TryGetValue("companionLayer", out var cl) ? cl : pb.Holdouts.CompanionLayer;

        if (count is int n && n > 0)
        {
            var L = layer ?? 1;
            var list = new List<CombatSimulationService.PartyMember>(n);
            for (int i = 0; i < n; i++)
            {
                list.Add(new CombatSimulationService.PartyMember(
                    CompanionType.Wildfolk,
                    (MagicElement)(i % 5),
                    L,
                    Math.Max(1, L * 2)));
            }
            return list;
        }

        // Fixed holdout companions, if any
        if (pb.Holdouts.Companions is { Count: > 0 } cs)
        {
            return cs.Select(c => new CombatSimulationService.PartyMember(
                ParseCompanionType(c.Type),
                ParseElement(c.Element),
                c.Layer,
                c.Level)).ToList();
        }

        return new List<CombatSimulationService.PartyMember>();
    }

    private static string PickRepresentative(int dangerLevel)
    {
        // Pick content monster by danger bucket; MonsterScaling handles amplitude.
        // Low danger → weak monster; high danger → already-tough monster scaled further.
        if (dangerLevel <= 2) return "timber-wolf";
        if (dangerLevel <= 5) return "dire-bear";
        return "frost-giant";
    }

    // ─── Classification ─────────────────────────────────────────────────────

    public static string Classify(double winRate, IReadOnlyDictionary<string, double[]> bands)
    {
        // winRate is 0..1; bands are expressed as percentages 0..100.
        var pct = winRate * 100.0;
        // Check in the canonical order so a win rate that touches two bands' edges
        // resolves to the higher-tier band (easy over balanced, etc).
        string[] order = { "trivial", "easy", "balanced", "hard", "punishing" };
        foreach (var name in order)
        {
            if (!bands.TryGetValue(name, out var rng) || rng.Length != 2) continue;
            var lo = Math.Min(rng[0], rng[1]);
            var hi = Math.Max(rng[0], rng[1]);
            if (pct >= lo && pct <= hi) return name;
        }
        return "unknown";
    }

    private static string LookupExpected(Playbook pb, IReadOnlyDictionary<string, int> axisValues)
    {
        foreach (var cell in pb.ExpectedViability.Cells)
        {
            bool match = true;
            foreach (var kv in axisValues)
            {
                var v = cell.Axis(kv.Key);
                if (v is null || v.Value != kv.Value) { match = false; break; }
            }
            if (match) return cell.Expected;
        }
        return "";
    }

    // ─── Cartesian product ─────────────────────────────────────────────────

    public static IEnumerable<IReadOnlyDictionary<string, int>> Cartesian(IReadOnlyList<Axis> axes)
    {
        foreach (var (dict, _) in CartesianWithIndices(axes)) yield return dict;
    }

    /// <summary>
    /// Same as <see cref="Cartesian"/> but also yields the cell-index tuple
    /// (position in each axis's values array). Callers use the index tuple
    /// to derive a deterministic per-cell salt independent of string hashing.
    /// </summary>
    public static IEnumerable<(IReadOnlyDictionary<string, int> values, int[] indices)> CartesianWithIndices(IReadOnlyList<Axis> axes)
    {
        if (axes.Count == 0) { yield return (new Dictionary<string, int>(), Array.Empty<int>()); yield break; }
        var idx = new int[axes.Count];
        while (true)
        {
            var dict = new Dictionary<string, int>(axes.Count);
            for (int a = 0; a < axes.Count; a++) dict[axes[a].Name] = axes[a].Values[idx[a]];
            yield return (dict, (int[])idx.Clone());
            int k = axes.Count - 1;
            while (k >= 0)
            {
                idx[k]++;
                if (idx[k] < axes[k].Values.Length) break;
                idx[k] = 0; k--;
            }
            if (k < 0) yield break;
        }
    }

    private static MagicElement ParseElement(string s)
        => Enum.TryParse<MagicElement>(s, ignoreCase: true, out var e) ? e : MagicElement.Aether;
    private static CompanionType ParseCompanionType(string s)
        => Enum.TryParse<CompanionType>(s, ignoreCase: true, out var t) ? t : CompanionType.Wildfolk;
}
