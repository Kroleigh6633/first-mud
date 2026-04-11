using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IItemRepository
{
    Task<Item?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<Item>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
    Task<IReadOnlyList<Item>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task AddAsync(Item item, CancellationToken ct = default);
    Task UpdateAsync(Item item, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
