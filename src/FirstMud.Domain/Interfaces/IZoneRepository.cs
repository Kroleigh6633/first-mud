using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Domain.Interfaces;

public interface IZoneRepository
{
    Task<Zone?> GetAsync(WorldId worldId, int zoneId, CancellationToken ct = default);
    Task<IReadOnlyList<Zone>> GetByWorldAsync(WorldId worldId, CancellationToken ct = default);
    Task AddAsync(Zone zone, CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<Zone> zones, CancellationToken ct = default);
    Task<bool> ExistsAsync(WorldId worldId, int zoneId, CancellationToken ct = default);
}
