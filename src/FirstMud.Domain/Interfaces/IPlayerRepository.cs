using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(Guid playerId, CancellationToken cancellationToken = default);
    Task<Player?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddAsync(Player player, CancellationToken cancellationToken = default);
    Task UpdateAsync(Player player, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetActivePlayersAsync(CancellationToken cancellationToken = default);
}
