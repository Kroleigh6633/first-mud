namespace FirstMud.Application.Content;

/// <summary>
/// Tunable combat scaling coefficients consumed by both the live
/// <c>MonsterFactory</c> and the design-time <c>encounter-sim</c> tool.
///
/// Loaded from <c>content/combat-curves.json</c> via
/// <see cref="IContentProvider.CombatCurves"/>. Having one source of truth
/// guarantees that simulation results match live-game scaling — previously
/// the multipliers were inline constants in <c>MonsterFactory.ScaleMonster</c>
/// and the sim ran against unscaled base stats, producing misleading
/// "trivial" reports for fights that were actually punishing.
///
/// Conditional / per-monster-type scaling (variance, level overrides, the
/// boss flag itself) stays in C#; this record only houses clean numeric
/// curves.
/// </summary>
public sealed record CombatCurvesDefinition(
    MonsterScalingCurve MonsterScaling,
    PartyScalingCurve PartyScaling);

/// <summary>
/// Symmetry counterpart to <see cref="MonsterScalingCurve"/>. Enemies scale
/// with zone danger; the party needs a matching progression tier bonus or
/// the player hits a wall at mid-danger. Applied to player + companion
/// MaxHp and ability BasePower at combat start:
/// <c>factor = 1 + ScalingPerTier * (playerLevel + avgCompanionLayer)</c>.
/// </summary>
public sealed record PartyScalingCurve(
    double ScalingPerTier);

/// <summary>
/// Coefficients for <c>MonsterFactory.ScaleMonster</c>:
/// <list type="bullet">
///   <item><c>HP        = base * (1 + danger * <see cref="HpPerDanger"/>)</c></item>
///   <item><c>Power     = base * (1 + danger * <see cref="PowerPerDanger"/>)</c></item>
///   <item><c>Speed     = base + danger * <see cref="SpeedPerDanger"/></c></item>
///   <item>Boss bonuses: HP multiplied by <see cref="BossHpMultiplier"/>,
///         speed gets <see cref="BossSpeedBonus"/> added.</item>
/// </list>
/// </summary>
public sealed record MonsterScalingCurve(
    double HpPerDanger,
    double PowerPerDanger,
    double SpeedPerDanger,
    double BossHpMultiplier,
    int    BossSpeedBonus);
