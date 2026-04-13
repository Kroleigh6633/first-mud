namespace FirstMud.Domain.ValueObjects;

/// <summary>
/// Returned by <see cref="Entities.Player.RecordTileDiscovery"/> when a notable
/// exploration threshold is crossed.  Callers use this to broadcast the reward
/// message to the player without digging into domain events.
/// </summary>
public sealed record ExplorationMilestone(int TilesDiscovered, string Label, int XpAwarded);
