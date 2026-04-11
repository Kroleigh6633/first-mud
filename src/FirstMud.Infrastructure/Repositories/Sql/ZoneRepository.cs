using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class ZoneRepository : IZoneRepository
{
    private readonly GameDbContext _context;

    public ZoneRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<Zone?> GetAsync(WorldId worldId, int zoneId, CancellationToken ct = default)
    {
        return await _context.Zones
            .FirstOrDefaultAsync(z => z.WorldId == worldId && z.ZoneId == zoneId, ct);
    }

    public async Task<IReadOnlyList<Zone>> GetByWorldAsync(WorldId worldId, CancellationToken ct = default)
    {
        return await _context.Zones
            .Where(z => z.WorldId == worldId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Zone zone, CancellationToken ct = default)
    {
        await _context.Zones.AddAsync(zone, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<Zone> zones, CancellationToken ct = default)
    {
        await _context.Zones.AddRangeAsync(zones, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> ExistsAsync(WorldId worldId, int zoneId, CancellationToken ct = default)
    {
        return await _context.Zones
            .AnyAsync(z => z.WorldId == worldId && z.ZoneId == zoneId, ct);
    }
}
