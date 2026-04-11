using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class BaseAssetRepository : IBaseAssetRepository
{
    private readonly GameDbContext _context;

    public BaseAssetRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<BaseAsset>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default)
    {
        return await _context.BaseAssets
            .Where(a => a.OwnerId == ownerId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(BaseAsset asset, CancellationToken ct = default)
    {
        await _context.BaseAssets.AddAsync(asset, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(BaseAsset asset, CancellationToken ct = default)
    {
        _context.BaseAssets.Update(asset);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateManyAsync(IEnumerable<BaseAsset> assets, CancellationToken ct = default)
    {
        foreach (var asset in assets)
            _context.BaseAssets.Update(asset);
        await _context.SaveChangesAsync(ct);
    }
}
