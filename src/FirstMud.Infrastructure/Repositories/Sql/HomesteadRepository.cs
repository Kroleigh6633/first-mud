using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class HomesteadRepository : IHomesteadRepository
{
    private readonly GameDbContext _context;

    public HomesteadRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<Homestead?> GetByPlayerIdAsync(Guid playerId, CancellationToken ct = default)
    {
        return await _context.Homesteads
            .FirstOrDefaultAsync(h => h.PlayerId == playerId, ct);
    }

    public async Task AddAsync(Homestead homestead, CancellationToken ct = default)
    {
        await _context.Homesteads.AddAsync(homestead, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HomesteadStorageItem>> GetStorageItemsAsync(Guid homesteadId, CancellationToken ct = default)
    {
        return await _context.HomesteadStorageItems
            .Where(s => s.HomesteadId == homesteadId)
            .ToListAsync(ct);
    }

    public async Task AddStorageItemAsync(HomesteadStorageItem storageItem, CancellationToken ct = default)
    {
        await _context.HomesteadStorageItems.AddAsync(storageItem, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task RemoveStorageItemAsync(Guid homesteadId, Guid itemId, CancellationToken ct = default)
    {
        var row = await _context.HomesteadStorageItems
            .FirstOrDefaultAsync(s => s.HomesteadId == homesteadId && s.ItemId == itemId, ct);
        if (row is not null)
        {
            _context.HomesteadStorageItems.Remove(row);
            await _context.SaveChangesAsync(ct);
        }
    }

    public async Task UpdateAsync(Homestead homestead, CancellationToken ct = default)
    {
        _context.Homesteads.Update(homestead);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<HomesteadStorageItem?> GetStorageItemByNameAsync(Guid homesteadId, string name, CancellationToken ct = default)
    {
        // Join to Items table to find by name
        var storageItems = await _context.HomesteadStorageItems
            .Where(s => s.HomesteadId == homesteadId)
            .ToListAsync(ct);

        foreach (var si in storageItems)
        {
            var item = await _context.Items.FirstOrDefaultAsync(i => i.Id == si.ItemId, ct);
            if (item is not null && string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                return si;
        }
        return null;
    }
}
