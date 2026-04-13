using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven faction template loaded from content/factions.json.
///
/// Replaces the scattered in-code faction metadata that previously lived in
/// QuestCommandHandler.FactionWaypoints and the dead TensionPairs table in
/// ReputationService. Per-faction reputation tier thresholds and reward
/// formulas remain in <see cref="FirstMud.Domain.ValueObjects.ReputationScore"/>
/// and Player.ApplyFactionTensions — those will follow in a separate
/// migration once the authoring surface is clearer.
/// </summary>
public sealed record FactionDefinition(
    FactionId Id,
    string DisplayName,
    string Description,
    string HqZoneId,
    string? HqDisplayName,
    IReadOnlyList<FactionId> HostileTo,
    int StartingReputation,
    bool Hidden,
    IReadOnlyList<string> WaypointZoneIds,
    FactionWaypoint? Waypoint);

/// <summary>
/// Optional hand-placed (x, y) anchor used by the quest system to resolve a
/// waypoint marker to a specific world-grid cell. When absent, callers should
/// fall back to <see cref="FactionDefinition.WaypointZoneIds"/> and the zones
/// content file (once <c>content/zones.json</c> is integrated in this branch).
/// </summary>
public sealed record FactionWaypoint(int X, int Y);
