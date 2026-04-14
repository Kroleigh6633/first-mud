using FirstMud.Application.Models;
using FirstMud.Application.Services;

namespace FirstMud.DesignTools.Tools.PlaybookRunner;

/// <summary>
/// Cell-evaluator for <c>kind:"crafting"</c> playbooks. For each axis cell, it
/// simulates N crafting attempts by invoking <see cref="CraftingService.RollOutcome"/>
/// (the shared source of truth) after computing whether the player's supplied
/// quantity would clear the ±5% tolerance check, given the UI display-rounding
/// of per-player seeded quantities.
///
/// Axes it recognises:
///   craftingSkill     — used as a soft scaler on the "effective rounding" (higher
///                       skill means the UI shows finer hints; current game = flat 5).
///                       Kept as an axis for future tuning; see CraftingHoldouts.
///   recipeDifficulty  — not currently a gate on the probability roll post-skill-check,
///                       but shifts base ingredient quantity (difficulty N => base = 3 + 3*N).
///
/// The evaluator averages across <c>SeedSpread</c> distinct player seeds per cell so
/// the result isn't dominated by one unlucky seed.
/// </summary>
public sealed class CraftingEvaluator
{
    public sealed record CraftingCellSummary(
        IReadOnlyDictionary<string, int> AxisValues,
        IReadOnlyDictionary<string, double> OutcomePercentages,
        int TotalRolls,
        bool QuantitiesMatchedSample,
        IReadOnlyList<string> Divergences);

    public sealed record CraftingRunResult(
        string PlaybookId,
        int Seed,
        int Rolls,
        IReadOnlyList<CraftingCellSummary> Cells,
        IReadOnlyList<CraftingCellSummary> Divergences);

    public CraftingRunResult Execute(Playbook pb, int? seedOverride = null)
    {
        var seed = seedOverride ?? pb.Seed;
        var cells = new List<CraftingCellSummary>();

        foreach (var combo in PlaybookEngine.Cartesian(pb.Axes))
            cells.Add(RunCell(pb, combo, seed));

        var divergent = cells.Where(c => c.Divergences.Count > 0).ToList();
        return new CraftingRunResult(pb.Id, seed, pb.Rolls, cells, divergent);
    }

    private CraftingCellSummary RunCell(
        Playbook pb, IReadOnlyDictionary<string, int> axisValues, int masterSeed)
    {
        int baseQty = pb.Crafting.BaseQuantity;
        int displayRounding = Math.Max(1, pb.Crafting.DisplayRounding);
        int seedSpread = Math.Max(1, pb.Crafting.SeedSpread);

        // recipeDifficulty tier shifts base ingredient quantity. Tier 1 => 6, tier 3 => 12,
        // tier 5 => 18, tier 8 => 27. Larger quantities mean larger tolerances
        // (±5% of 27 ≈ 1.4 ≈ 1), so rounding error is relatively smaller at higher tiers.
        if (axisValues.TryGetValue("recipeDifficulty", out var rd))
            baseQty = 3 + 3 * Math.Max(1, rd);

        // Mix cell salt for determinism across cells.
        int cellSalt = 0;
        foreach (var kv in axisValues) cellSalt = unchecked(cellSalt * 31 + kv.Key.GetHashCode() * 17 + kv.Value * 7);

        var tally = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["Success"] = 0, ["NearMiss"] = 0, ["UnexpectedResult"] = 0,
            ["ComponentLoss"] = 0, ["Discovery"] = 0,
        };

        int total = 0;
        bool sampleMatched = false;
        int rollsPerSeed = Math.Max(1, pb.Rolls / seedSpread);

        for (int s = 0; s < seedSpread; s++)
        {
            // Vary player seed across the spread; start from the cell's salted base.
            int playerSeed = unchecked(pb.Crafting.PlayerSeed + masterSeed + cellSalt + s * 101);
            int seeded = CraftingService.SeededQuantity(baseQty, playerSeed, 0);

            // Model the UI: displayed quantity is the seeded value rounded to nearest `displayRounding`.
            int displayed = (int)(Math.Round(seeded / (double)displayRounding) * displayRounding);
            displayed = Math.Max(1, displayed);

            // A reasonable player submits the displayed quantity (the UI's own hint).
            // Tolerance now scales with craftingSkill / recipeDifficulty (task #133):
            // treat recipeDifficulty as the required-crafting-skill proxy so a
            // high-skill crafter actually clears more cells than a low-skill one.
            int craftingSkillAxis = axisValues.TryGetValue("craftingSkill", out var csk) ? csk : 1;
            int requiredSkillAxis = axisValues.TryGetValue("recipeDifficulty", out var rdk) ? Math.Max(1, rdk) : 1;
            bool match = CraftingService.QuantityMatches(displayed, seeded, craftingSkillAxis, requiredSkillAxis);
            if (s == 0) sampleMatched = match;

            var rng = new Random(unchecked(masterSeed * 1_000_003 + cellSalt * 97 + s * 13));
            for (int i = 0; i < rollsPerSeed; i++)
            {
                var outcome = CraftingService.RollOutcome(rng, match);
                tally[outcome.ToString()]++;
                total++;
            }
        }

        var pct = tally.ToDictionary(
            kv => kv.Key,
            kv => total == 0 ? 0.0 : 100.0 * kv.Value / total,
            StringComparer.Ordinal);

        var divergences = CheckDivergences(pb, axisValues, pct);
        return new CraftingCellSummary(axisValues, pct, total, sampleMatched, divergences);
    }

    private static IReadOnlyList<string> CheckDivergences(
        Playbook pb,
        IReadOnlyDictionary<string, int> axisValues,
        IReadOnlyDictionary<string, double> actualPct)
    {
        var list = new List<string>();
        foreach (var cell in pb.ExpectedDistribution.Cells)
        {
            bool match = true;
            foreach (var kv in axisValues)
            {
                var v = cell.Axis(kv.Key);
                if (v is null || v.Value != kv.Value) { match = false; break; }
            }
            if (!match) continue;

            foreach (var (outcome, band) in cell.Expected())
            {
                var actual = actualPct.TryGetValue(outcome, out var p) ? p : 0.0;
                var lo = Math.Min(band[0], band[1]);
                var hi = Math.Max(band[0], band[1]);
                if (actual < lo - 0.01 || actual > hi + 0.01)
                    list.Add($"{outcome}={actual:F1}% outside [{lo},{hi}]");
            }
            break;
        }
        return list;
    }

    public static string RenderTable(CraftingRunResult result)
    {
        var sb = new System.Text.StringBuilder();
        if (result.Cells.Count == 0) return "(no cells)";
        var axisNames = result.Cells[0].AxisValues.Keys.ToArray();

        foreach (var n in axisNames) sb.Append(n.PadRight(10) + " | ");
        sb.AppendLine("Success | NearMiss | UnexpRes | CompLoss | Discov | match? | !");

        foreach (var c in result.Cells)
        {
            foreach (var n in axisNames) sb.Append(c.AxisValues[n].ToString().PadRight(10) + " | ");
            sb.Append($"{c.OutcomePercentages["Success"],6:F1}% | ");
            sb.Append($"{c.OutcomePercentages["NearMiss"],7:F1}% | ");
            sb.Append($"{c.OutcomePercentages["UnexpectedResult"],7:F1}% | ");
            sb.Append($"{c.OutcomePercentages["ComponentLoss"],7:F1}% | ");
            sb.Append($"{c.OutcomePercentages["Discovery"],5:F1}% | ");
            sb.Append($"{(c.QuantitiesMatchedSample ? "Y" : "N"),6} | ");
            sb.AppendLine(c.Divergences.Count > 0 ? "DIVERGE: " + string.Join("; ", c.Divergences) : "");
        }
        return sb.ToString();
    }
}
