using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IBaseAssetRepository
{
    Task<IReadOnlyList<BaseAsset>> GetByOwnerAsync(Guid ownerId, CancellationToken ct = default);
    Task AddAsync(BaseAsset asset, CancellationToken ct = default);
    Task UpdateAsync(BaseAsset asset, CancellationToken ct = default);
    Task UpdateManyAsync(IEnumerable<BaseAsset> assets, CancellationToken ct = default);
}
