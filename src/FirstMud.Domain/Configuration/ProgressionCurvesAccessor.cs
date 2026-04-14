using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Configuration;

/// <summary>
/// Static service-locator-style bridge that exposes the
/// <c>content/progression-curves.json</c> values to Domain-layer types
/// (<see cref="Entities.Companion"/>, <see cref="ValueObjects.Workmanship"/>)
/// without the Domain project taking a reference on
/// <c>FirstMud.Application</c>.
///
/// <para>
/// At runtime, <c>ContentProvider</c> calls <see cref="Publish"/> after each
/// reload so the live values flow into Domain. If nothing has been published
/// yet (e.g. unit tests that construct Domain entities directly without
/// spinning up a content root), the historical hardcoded defaults are
/// returned — preserving prior behaviour.
/// </para>
///
/// <para>
/// Mirrors the <see cref="FirstMud.Application.Content.ContentAccessor"/>
/// pattern but lives in Domain because <see cref="Entities.Companion"/> is a
/// Domain entity that needs the values without an upward reference.
/// </para>
/// </summary>
public static class ProgressionCurvesAccessor
{
    /// <summary>Historical default workmanship divisor (pre-tune).</summary>
    public const int DefaultSkillDivisor = 20;

    /// <summary>Historical default companion rubber-band coefficients (task #74).</summary>
    public const int DefaultRubberBandPlayerLevelWeight = 3;
    public const double DefaultRubberBandPerGapBonus = 0.15;
    public const double DefaultRubberBandMaxMultiplier = 3.0;

    /// <summary>Historical default crafting-skill-scaling coefficients (task #133).</summary>
    public const double DefaultCraftingBaseTolerance = 0.05;
    public const double DefaultCraftingTolerancePerSkillTier = 0.02;
    public const double DefaultCraftingMaxTolerance = 0.20;

    /// <summary>Historical default per-type companion layer thresholds (pre-tune).</summary>
    public static readonly IReadOnlyDictionary<CompanionType, IReadOnlyList<int>> DefaultCompanionLayerThresholds =
        new Dictionary<CompanionType, IReadOnlyList<int>>
        {
            [CompanionType.Wildfolk]         = new[] { 0, 200, 500, 1000, 2000, 4000 },
            [CompanionType.HiredHero]        = new[] { 0, 200, 600, 1200, 2500, 5000 },
            [CompanionType.CapturedMonster]  = new[] { 0, 150, 400, 900, 1800, 3600 },
            [CompanionType.BoundShade]       = new[] { 0, 300, 700, 1500, 3000, 6000 },
            [CompanionType.ArdweldConstruct] = new[] { 0, 500, 1500, 3000, 6000, 12000 },
        };

    private static int _skillDivisor = DefaultSkillDivisor;
    private static IReadOnlyDictionary<CompanionType, IReadOnlyList<int>> _companionLayerThresholds =
        DefaultCompanionLayerThresholds;
    private static int _rubberBandPlayerLevelWeight = DefaultRubberBandPlayerLevelWeight;
    private static double _rubberBandPerGapBonus = DefaultRubberBandPerGapBonus;
    private static double _rubberBandMaxMultiplier = DefaultRubberBandMaxMultiplier;
    private static double _craftingBaseTolerance = DefaultCraftingBaseTolerance;
    private static double _craftingTolerancePerSkillTier = DefaultCraftingTolerancePerSkillTier;
    private static double _craftingMaxTolerance = DefaultCraftingMaxTolerance;
    private static readonly object _gate = new();

    /// <summary>Currently-effective workmanship skill divisor.</summary>
    public static int SkillDivisor => _skillDivisor;

    /// <summary>Currently-effective companion layer thresholds.</summary>
    public static IReadOnlyDictionary<CompanionType, IReadOnlyList<int>> CompanionLayerThresholds =>
        _companionLayerThresholds;

    /// <summary>
    /// Returns the threshold list for <paramref name="type"/> if present,
    /// otherwise the historical default (so a partially-authored content
    /// file cannot crash live combat for an unmapped companion type).
    /// </summary>
    public static IReadOnlyList<int> ThresholdsFor(CompanionType type)
    {
        if (_companionLayerThresholds.TryGetValue(type, out var ts) && ts.Count >= 6)
            return ts;
        return DefaultCompanionLayerThresholds[type];
    }

    /// <summary>Currently-effective rubber-band weight for player-level gap.</summary>
    public static int RubberBandPlayerLevelWeight => _rubberBandPlayerLevelWeight;

    /// <summary>Currently-effective rubber-band bonus per gap-unit.</summary>
    public static double RubberBandPerGapBonus => _rubberBandPerGapBonus;

    /// <summary>Currently-effective rubber-band upper clamp.</summary>
    public static double RubberBandMaxMultiplier => _rubberBandMaxMultiplier;

    /// <summary>
    /// Returns the usage-point multiplier applied when a player at
    /// <paramref name="playerLevel"/> records usage on a companion at
    /// <paramref name="companionLayer"/>. The further the player's level
    /// outpaces the companion's effective bracket
    /// (<c>layer × PlayerLevelWeight</c>), the larger the multiplier —
    /// clamped to <see cref="RubberBandMaxMultiplier"/>. Never returns
    /// below 1.0 (a companion ahead of the player gets no penalty).
    /// </summary>
    public static double RubberBandMultiplier(int playerLevel, int companionLayer)
    {
        var gap = playerLevel - companionLayer * _rubberBandPlayerLevelWeight;
        var mult = 1.0 + Math.Max(0, gap * _rubberBandPerGapBonus);
        return Math.Min(mult, _rubberBandMaxMultiplier);
    }

    /// <summary>Currently-effective crafting baseline tolerance (fraction of seeded qty).</summary>
    public static double CraftingBaseTolerance => _craftingBaseTolerance;

    /// <summary>Currently-effective additional tolerance per skill-tier above requirement.</summary>
    public static double CraftingTolerancePerSkillTier => _craftingTolerancePerSkillTier;

    /// <summary>Currently-effective upper clamp on the skill-scaled crafting tolerance.</summary>
    public static double CraftingMaxTolerance => _craftingMaxTolerance;

    /// <summary>
    /// Returns the skill-scaled ±tolerance (fraction of seeded quantity) for a
    /// crafting attempt. Formula:
    ///   skillRatio = playerSkill / max(1, requiredSkill)
    ///   tolerance  = clamp(base + max(0, skillRatio - 1) * perSkillTier, base, max)
    /// At <c>playerSkill == requiredSkill</c> → base (historical 5%).
    /// At <c>playerSkill == 2 × requiredSkill</c> → base + 1 × perSkillTier (7%).
    /// At <c>playerSkill == 4 × requiredSkill</c> → base + 3 × perSkillTier (11%).
    /// Clamped to <see cref="CraftingMaxTolerance"/> so legendary crafters still
    /// face real risk on the hardest recipes. Task #133.
    /// </summary>
    public static double CraftingTolerance(int playerSkill, int requiredSkill)
    {
        var required = Math.Max(1, requiredSkill);
        var skill = Math.Max(0, playerSkill);
        var skillRatio = skill / (double)required;
        var bonus = Math.Max(0.0, skillRatio - 1.0) * _craftingTolerancePerSkillTier;
        var tol = _craftingBaseTolerance + bonus;
        if (tol < _craftingBaseTolerance) tol = _craftingBaseTolerance;
        if (tol > _craftingMaxTolerance) tol = _craftingMaxTolerance;
        return tol;
    }

    /// <summary>
    /// Called by <c>ContentProvider</c> on (re)load. Any parameter may be
    /// <c>null</c> to mean "keep the current value".
    /// </summary>
    public static void Publish(
        int? skillDivisor,
        IReadOnlyDictionary<CompanionType, IReadOnlyList<int>>? companionLayerThresholds,
        int? rubberBandPlayerLevelWeight = null,
        double? rubberBandPerGapBonus = null,
        double? rubberBandMaxMultiplier = null,
        double? craftingBaseTolerance = null,
        double? craftingTolerancePerSkillTier = null,
        double? craftingMaxTolerance = null)
    {
        lock (_gate)
        {
            if (skillDivisor is int sd && sd > 0) _skillDivisor = sd;
            if (companionLayerThresholds is { Count: > 0 })
                _companionLayerThresholds = companionLayerThresholds;
            if (rubberBandPlayerLevelWeight is int w && w > 0) _rubberBandPlayerLevelWeight = w;
            if (rubberBandPerGapBonus is double b && b >= 0) _rubberBandPerGapBonus = b;
            if (rubberBandMaxMultiplier is double m && m >= 1) _rubberBandMaxMultiplier = m;
            if (craftingBaseTolerance is double cb && cb >= 0) _craftingBaseTolerance = cb;
            if (craftingTolerancePerSkillTier is double ct && ct >= 0) _craftingTolerancePerSkillTier = ct;
            if (craftingMaxTolerance is double cm && cm >= 0) _craftingMaxTolerance = cm;
        }
    }

    /// <summary>Test seam — restores hardcoded defaults.</summary>
    public static void ResetForTests()
    {
        lock (_gate)
        {
            _skillDivisor = DefaultSkillDivisor;
            _companionLayerThresholds = DefaultCompanionLayerThresholds;
            _rubberBandPlayerLevelWeight = DefaultRubberBandPlayerLevelWeight;
            _rubberBandPerGapBonus = DefaultRubberBandPerGapBonus;
            _rubberBandMaxMultiplier = DefaultRubberBandMaxMultiplier;
            _craftingBaseTolerance = DefaultCraftingBaseTolerance;
            _craftingTolerancePerSkillTier = DefaultCraftingTolerancePerSkillTier;
            _craftingMaxTolerance = DefaultCraftingMaxTolerance;
        }
    }

    /// <summary>Narrow test seam — restores ONLY the crafting-skill-scaling coefficients.</summary>
    public static void ResetCraftingSkillScalingForTests()
    {
        lock (_gate)
        {
            _craftingBaseTolerance = DefaultCraftingBaseTolerance;
            _craftingTolerancePerSkillTier = DefaultCraftingTolerancePerSkillTier;
            _craftingMaxTolerance = DefaultCraftingMaxTolerance;
        }
    }

    /// <summary>
    /// Narrow test seam — restores ONLY the rubber-band coefficients. Used by
    /// the rubber-band accessor tests so they don't clobber companion-layer
    /// threshold state that other test suites have published.
    /// </summary>
    public static void ResetRubberBandForTests()
    {
        lock (_gate)
        {
            _rubberBandPlayerLevelWeight = DefaultRubberBandPlayerLevelWeight;
            _rubberBandPerGapBonus = DefaultRubberBandPerGapBonus;
            _rubberBandMaxMultiplier = DefaultRubberBandMaxMultiplier;
        }
    }
}
