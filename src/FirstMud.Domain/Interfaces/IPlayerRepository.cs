using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IPlayerRepository
{
    Task<Player?> GetByIdAsync(Guid playerId, CancellationToken cancellationToken = default);
    Task<Player?> GetByNameAsync(string name, CancellationToken cancellationToken = default);
    Task AddAsync(Player player, CancellationToken cancellationToken = default);
    Task UpdateAsync(Player player, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Player>> GetActivePlayersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stamps the player's LastSeenAt shadow property to UtcNow so they remain
    /// visible to the 24-hour active-player window used by background ticks.
    /// </summary>
    Task TouchLastSeenAsync(Guid playerId, CancellationToken cancellationToken = default);
}
