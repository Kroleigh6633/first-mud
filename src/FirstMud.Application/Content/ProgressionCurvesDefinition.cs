using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Tunable progression scaling coefficients consumed by the live game's
/// <c>CraftingService.CalculateWorkmanship</c> / <c>Workmanship.Combine</c>
/// (skill divisor) and <c>Companion</c> (per-type layer threshold table).
///
/// Loaded from <c>content/progression-curves.json</c> via
/// <see cref="IContentProvider.ProgressionCurves"/>. The file is OPTIONAL —
/// when absent, <see cref="ContentProvider"/> falls back to the historical
/// constants so partial-content roots and existing fixtures continue to work
/// (mirrors the <c>combat-curves.json</c> pattern).
/// </summary>
/// <param name="Workmanship">Workmanship-related curves (currently just skillDivisor).</param>
/// <param name="CompanionLayerThresholds">CompanionType → 6-element layer-unlock threshold table.</param>
/// <param name="CompanionRubberBand">XP-multiplier curve that lets lagging companions catch up to an over-levelled player.</param>
public sealed record ProgressionCurvesDefinition(
    WorkmanshipCurve Workmanship,
    IReadOnlyDictionary<CompanionType, IReadOnlyList<int>> CompanionLayerThresholds,
    CompanionRubberBandCurve CompanionRubberBand);

/// <summary>
/// Workmanship coefficients. <c>skillBonus = craftingSkill / SkillDivisor</c>.
/// Halving the divisor (20 → 10) doubles the skill contribution to crafted
/// gear quality — see progression-sim Pass-1 report.
/// </summary>
public sealed record WorkmanshipCurve(int SkillDivisor);

/// <summary>
/// Companion XP rubber-band (catch-up) scaling. When a player out-levels a
/// companion's effective bracket (<c>layer × PlayerLevelWeight</c>), usage
/// points are multiplied by
/// <c>1 + max(0, playerLevel - layer*PlayerLevelWeight) * PerGapBonus</c>,
/// clamped at <see cref="MaxMultiplier"/>. Near-match → ~1.0 (no bonus).
/// </summary>
public sealed record CompanionRubberBandCurve(
    int PlayerLevelWeight,
    double PerGapBonus,
    double MaxMultiplier);
