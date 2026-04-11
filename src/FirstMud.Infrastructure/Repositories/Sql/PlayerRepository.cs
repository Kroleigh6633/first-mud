using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class PlayerRepository : IPlayerRepository
{
    private readonly GameDbContext _context;

    public PlayerRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<Player?> GetByIdAsync(Guid playerId, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .Include(p => p.Reputations)
            .FirstOrDefaultAsync(p => p.Id == playerId, cancellationToken);
    }

    public async Task<Player?> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        return await _context.Players
            .Include(p => p.Reputations)
            .FirstOrDefaultAsync(p => p.Name == name, cancellationToken);
    }

    public async Task AddAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _context.Players.AddAsync(player, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Player player, CancellationToken cancellationToken = default)
    {
        _context.Players.Update(player);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Player>> GetActivePlayersAsync(CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddHours(-24);
        return await _context.Players
            .Include(p => p.Reputations)
            .Where(p => EF.Property<DateTime>(p, "LastSeenAt") >= cutoff)
            .ToListAsync(cancellationToken);
    }
}
