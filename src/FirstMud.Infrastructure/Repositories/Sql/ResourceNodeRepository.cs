using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class ResourceNodeRepository : IResourceNodeRepository
{
    private readonly GameDbContext _context;

    public ResourceNodeRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ResourceNode>> GetByZoneIdAsync(Guid zoneId, CancellationToken ct = default)
    {
        return await _context.ResourceNodes
            .Where(r => r.ZoneId == zoneId)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ResourceNode>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.ResourceNodes.ToListAsync(ct);
    }

    public async Task AddRangeAsync(IEnumerable<ResourceNode> nodes, CancellationToken ct = default)
    {
        await _context.ResourceNodes.AddRangeAsync(nodes, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(ResourceNode node, CancellationToken ct = default)
    {
        _context.ResourceNodes.Update(node);
        await _context.SaveChangesAsync(ct);
    }
}
