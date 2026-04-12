using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IResourceNodeRepository
{
    Task<IReadOnlyList<ResourceNode>> GetByZoneIdAsync(Guid zoneId, CancellationToken ct = default);
    Task<IReadOnlyList<ResourceNode>> GetAllAsync(CancellationToken ct = default);
    Task AddRangeAsync(IEnumerable<ResourceNode> nodes, CancellationToken ct = default);
    Task UpdateAsync(ResourceNode node, CancellationToken ct = default);
}
