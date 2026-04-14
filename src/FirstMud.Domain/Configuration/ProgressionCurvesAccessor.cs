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

    /// <summary>
    /// Called by <c>ContentProvider</c> on (re)load. Either parameter may be
    /// <c>null</c> to mean "keep the current value".
    /// </summary>
    public static void Publish(
        int? skillDivisor,
        IReadOnlyDictionary<CompanionType, IReadOnlyList<int>>? companionLayerThresholds)
    {
        lock (_gate)
        {
            if (skillDivisor is int sd && sd > 0) _skillDivisor = sd;
            if (companionLayerThresholds is { Count: > 0 })
                _companionLayerThresholds = companionLayerThresholds;
        }
    }

    /// <summary>Test seam — restores hardcoded defaults.</summary>
    public static void ResetForTests()
    {
        lock (_gate)
        {
            _skillDivisor = DefaultSkillDivisor;
            _companionLayerThresholds = DefaultCompanionLayerThresholds;
        }
    }
}
