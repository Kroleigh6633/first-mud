using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface ICompanionRepository
{
    Task<Companion?> GetByIdAsync(Guid companionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Companion>> GetByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);
    Task AddAsync(Companion companion, CancellationToken cancellationToken = default);
    Task UpdateAsync(Companion companion, CancellationToken cancellationToken = default);
    Task UpdateManyAsync(IEnumerable<Companion> companions, CancellationToken cancellationToken = default);
}
