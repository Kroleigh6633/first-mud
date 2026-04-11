using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class ItemRepository : IItemRepository
{
    private readonly GameDbContext _context;

    public ItemRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.Items
            .FirstOrDefaultAsync(i => i.Id == id, ct);
    }

    public async Task<IReadOnlyList<Item>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return await _context.Items
            .Where(i => idList.Contains(i.Id))
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<Item>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default)
    {
        return await _context.Items
            .Where(i => EF.Property<Guid?>(i, "OwnerId") == ownerId)
            .ToListAsync(ct);
    }

    public async Task AddAsync(Item item, CancellationToken ct = default)
    {
        await _context.Items.AddAsync(item, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Item item, CancellationToken ct = default)
    {
        _context.Items.Update(item);
        await _context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var item = await _context.Items.FindAsync([id], ct);
        if (item is not null)
        {
            _context.Items.Remove(item);
            await _context.SaveChangesAsync(ct);
        }
    }
}
