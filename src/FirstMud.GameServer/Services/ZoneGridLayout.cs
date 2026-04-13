using FirstMud.Application.Content;
using FirstMud.Domain.Enums;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Maps zones to deterministic (X, Y) positions within a fixed grid so the
/// world map is stable across server restarts.
///
/// Hand-crafted positions now live in <c>content/zones.json</c> and are
/// loaded via <see cref="IContentProvider"/>. This type is a thin static
/// facade so existing static call sites keep working; see
/// <see cref="ContentAccessor"/> for the DI bridge.
/// </summary>
public static class ZoneGridLayout
{
    public const int GridWidth = 40;
    public const int GridHeight = 20;

    /// <summary>
    /// The seeded starting zone's grid position. Resolved from zones.json at
    /// first use (the zone flagged <c>isStartingZone: true</c>, or the zone
    /// (Aeldran, 7) as a fallback).
    /// </summary>
    public static (int X, int Y) StartingRoad
    {
        get
        {
            var provider = ContentAccessor.Current;
            var start = provider.AllZones().FirstOrDefault(z => z.IsStartingZone);
            if (start is not null) return (start.Layout.X, start.Layout.Y);
            var fallback = provider.GetZoneLayoutPosition(WorldId.Aeldran, 7);
            return fallback ?? (20, 10);
        }
    }

    /// <summary>
    /// Returns the layout coordinate for a seeded zone identified by
    /// (WorldId, ZoneNumber), or null if no hand-crafted position exists.
    /// </summary>
    public static (int X, int Y)? GetKnownPosition(WorldId world, int zoneNumber)
        => ContentAccessor.Current.GetZoneLayoutPosition(world, zoneNumber);

    /// <summary>
    /// Deterministic fallback using a stable hash of the zone GUID for
    /// zones that don't have a hand-crafted position.
    /// </summary>
    public static (int X, int Y) GetPosition(Guid zoneId)
    {
        var bytes = zoneId.ToByteArray();

        var hash = 0u;
        for (var i = 0; i < bytes.Length; i++)
            hash = hash * 31u + bytes[i];

        var x = (int)(hash % (uint)GridWidth);
        var y = (int)((hash / (uint)GridWidth) % (uint)GridHeight);

        return (x, y);
    }
}
