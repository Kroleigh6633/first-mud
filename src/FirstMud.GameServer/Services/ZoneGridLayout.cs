namespace FirstMud.GameServer.Services;

/// <summary>
/// Maps zone GUIDs to deterministic (X, Y) positions within a fixed grid.
/// The same ZoneId always produces the same position regardless of call order.
/// </summary>
public static class ZoneGridLayout
{
    public const int GridWidth = 40;
    public const int GridHeight = 20;

    /// <summary>
    /// Returns a deterministic (X, Y) grid coordinate for the given zone GUID.
    /// Uses a stable hash derived from the GUID bytes spread across the grid dimensions.
    /// </summary>
    public static (int X, int Y) GetPosition(Guid zoneId)
    {
        var bytes = zoneId.ToByteArray();

        // Combine bytes into a stable 32-bit hash without relying on GetHashCode()
        var hash = 0u;
        for (var i = 0; i < bytes.Length; i++)
            hash = hash * 31u + bytes[i];

        var x = (int)(hash % (uint)GridWidth);
        var y = (int)((hash / (uint)GridWidth) % (uint)GridHeight);

        return (x, y);
    }
}
