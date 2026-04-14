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

    /// <summary>
    /// Called by <c>ContentProvider</c> on (re)load. Any parameter may be
    /// <c>null</c> to mean "keep the current value".
    /// </summary>
    public static void Publish(
        int? skillDivisor,
        IReadOnlyDictionary<CompanionType, IReadOnlyList<int>>? companionLayerThresholds,
        int? rubberBandPlayerLevelWeight = null,
        double? rubberBandPerGapBonus = null,
        double? rubberBandMaxMultiplier = null)
    {
        lock (_gate)
        {
            if (skillDivisor is int sd && sd > 0) _skillDivisor = sd;
            if (companionLayerThresholds is { Count: > 0 })
                _companionLayerThresholds = companionLayerThresholds;
            if (rubberBandPlayerLevelWeight is int w && w > 0) _rubberBandPlayerLevelWeight = w;
            if (rubberBandPerGapBonus is double b && b >= 0) _rubberBandPerGapBonus = b;
            if (rubberBandMaxMultiplier is double m && m >= 1) _rubberBandMaxMultiplier = m;
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
