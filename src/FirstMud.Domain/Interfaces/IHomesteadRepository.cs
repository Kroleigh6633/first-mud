using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IHomesteadRepository
{
    Task<Homestead?> GetByPlayerIdAsync(Guid playerId, CancellationToken ct = default);
    Task AddAsync(Homestead homestead, CancellationToken ct = default);
    Task<IReadOnlyList<HomesteadStorageItem>> GetStorageItemsAsync(Guid homesteadId, CancellationToken ct = default);
    Task AddStorageItemAsync(HomesteadStorageItem storageItem, CancellationToken ct = default);
    Task RemoveStorageItemAsync(Guid homesteadId, Guid itemId, CancellationToken ct = default);
}
