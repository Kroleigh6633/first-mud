using FirstMud.Domain.Entities;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Utility for finding the nearest zone to a map coordinate.
/// Extracted to eliminate the repeated zone-proximity loop that appeared
/// in MoveCommand, HarvestCommand, and AutoFarm handlers.
/// </summary>
public static class ZoneProximity
{
    /// <summary>
    /// Returns the first zone whose grid position is within ±1 of (x, y), or null.
    /// </summary>
    public static Zone? FindNearby(IEnumerable<Zone> zones, int x, int y)
    {
        foreach (var z in zones)
        {
            var known = ZoneGridLayout.GetKnownPosition(z.WorldId, z.ZoneId);
            var (zx, zy) = known ?? ZoneGridLayout.GetPosition(z.Id);
            if (Math.Abs(zx - x) <= 1 && Math.Abs(zy - y) <= 1)
                return z;
        }
        return null;
    }
}
