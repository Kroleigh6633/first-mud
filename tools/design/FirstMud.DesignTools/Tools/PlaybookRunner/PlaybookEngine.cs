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

        foreach (var combo in Cartesian(playbook.Axes))
        {
            var summary = RunCell(playbook, combo, seed);
            var actual  = Classify(summary.WinRate, playbook.ToleranceBands);
            var expected = LookupExpected(playbook, combo);
            cells.Add(new CellResult(combo, summary, actual, expected, actual != expected && expected.Length > 0));
        }

        var divergences = cells.Where(c => c.Divergent).ToList();
        return new PlaybookRunResult(playbook.Id, seed, playbook.Rolls, cells, divergences);
    }

    // ─── Loadout construction ───────────────────────────────────────────────

    private EncounterSim.EncounterSimCommand.Summary RunCell(
        Playbook pb, IReadOnlyDictionary<string, int> axisValues, int masterSeed)
    {
        int playerLevel = pb.Holdouts.PlayerLevel ?? 5;
        if (axisValues.TryGetValue("playerLevel", out var pl)) playerLevel = pl;
        if (axisValues.TryGetValue("gearTier",    out var gt)) playerLevel += gt * 2;
        else if (pb.Holdouts.GearTier is int hgt) playerLevel += hgt * 2;
        if (axisValues.TryGetValue("imbueLevel",  out var il)) playerLevel += il;
        else if (pb.Holdouts.ImbueLevel is int hil) playerLevel += hil;
        playerLevel = Math.Max(1, playerLevel);

        var playerElement = ParseElement(pb.Holdouts.PlayerElement ?? "Aether");

        // Danger level: axis or holdout, default 0 (unscaled).
        int dangerLevel = axisValues.TryGetValue("dangerLevel", out var dl) ? dl
                        : (pb.Holdouts.DangerLevel ?? 0);
        dangerLevel = Math.Clamp(dangerLevel, 0, 10);

        // Party composition
        var party = BuildParty(pb, axisValues);

        // Monster selection: fixed via holdout, else pick by (danger tier proxy).
        var monsterId = pb.Holdouts.MonsterId ?? PickRepresentative(dangerLevel);
        var def = _content.GetMonster(monsterId)
            ?? throw new InvalidOperationException($"Unknown monster id '{monsterId}'.");
        var baseTemplate = new MonsterTemplate(def.Name, def.Hp, def.Speed, def.Level, def.Element, def.Abilities);
        var scaled = MonsterScaling.Apply(baseTemplate, dangerLevel, _content.CombatCurves.MonsterScaling, isBoss: false);

        // Mix cell index into the seed so every cell diverges deterministically.
        int cellSalt = 0;
        foreach (var kv in axisValues) cellSalt = unchecked(cellSalt * 31 + kv.Key.GetHashCode() * 17 + kv.Value * 7);

        var results = new List<CombatSimulationService.SimulationResult>(pb.Rolls);
        for (int i = 0; i < pb.Rolls; i++)
        {
            var rng = new Random(unchecked(masterSeed * 1_000_003 + cellSalt * 97 + i));
            var svc = new CombatSimulationService(rng);
            var enc = svc.BuildEncounter(playerElement, playerLevel, party, new[] { scaled });
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
        if (axes.Count == 0) { yield return new Dictionary<string, int>(); yield break; }
        var idx = new int[axes.Count];
        while (true)
        {
            var dict = new Dictionary<string, int>(axes.Count);
            for (int a = 0; a < axes.Count; a++) dict[axes[a].Name] = axes[a].Values[idx[a]];
            yield return dict;
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
