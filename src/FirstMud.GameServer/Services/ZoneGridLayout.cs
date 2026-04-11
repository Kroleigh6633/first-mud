using FirstMud.Domain.Enums;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Maps zones to deterministic (X, Y) positions within a fixed grid so the
/// world map is stable across server restarts.
/// </summary>
public static class ZoneGridLayout
{
    public const int GridWidth = 40;
    public const int GridHeight = 20;

    /// <summary>
    /// Hand-crafted layout of the seeded Aeldran zones. The player starts
    /// at <see cref="StartingRoad"/> so tile feedback is immediate.
    /// </summary>
    public static readonly (int X, int Y) StartingRoad = (20, 10);

    private static readonly Dictionary<(WorldId World, int ZoneNumber), (int X, int Y)> KnownPositions = new()
    {
        // Aeldran — laid out roughly geographically
        [(WorldId.Aeldran, 1)] = (8, 3),    // Caervorn Highlands  ^
        [(WorldId.Aeldran, 2)] = (13, 5),   // The Thornwood       #
        [(WorldId.Aeldran, 3)] = (24, 13),  // Portmere (Compact)  C
        [(WorldId.Aeldran, 4)] = (28, 9),   // Gravenmarsh         .
        [(WorldId.Aeldran, 5)] = (32, 15),  // The Drowned Coast   ~
        [(WorldId.Aeldran, 6)] = (34, 4),   // The Ashen Reach     *
        [(WorldId.Aeldran, 7)] = StartingRoad, // Starting Road    .
        [(WorldId.Aeldran, 8)] = (26, 7),   // Gravenhold          !
        [(WorldId.Aeldran, 9)] = (36, 18),  // The Maw Borderlands M
    };

    /// <summary>
    /// Returns the layout coordinate for a seeded zone identified by
    /// (WorldId, ZoneNumber), or null if no hand-crafted position exists.
    /// </summary>
    public static (int X, int Y)? GetKnownPosition(WorldId world, int zoneNumber)
    {
        return KnownPositions.TryGetValue((world, zoneNumber), out var pos) ? pos : null;
    }

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
