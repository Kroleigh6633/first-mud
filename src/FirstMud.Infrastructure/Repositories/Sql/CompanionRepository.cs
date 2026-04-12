using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class CompanionRepository : ICompanionRepository
{
    private readonly GameDbContext _context;

    public CompanionRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<Companion?> GetByIdAsync(Guid companionId, CancellationToken cancellationToken = default)
    {
        return await _context.Companions
            .FirstOrDefaultAsync(c => c.Id == companionId, cancellationToken);
    }

    public async Task<IReadOnlyList<Companion>> GetByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        return await _context.Companions
            .Where(c => c.OwnerId == ownerId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Companion>> GetAllOnDutyAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Companions
            .Where(c => c.AssignedDuty != null && c.AssignedDuty != HomesteadDuty.None && !c.IsPermanentlyGone)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Companion companion, CancellationToken cancellationToken = default)
    {
        await _context.Companions.AddAsync(companion, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Companion companion, CancellationToken cancellationToken = default)
    {
        _context.Companions.Update(companion);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateManyAsync(IEnumerable<Companion> companions, CancellationToken cancellationToken = default)
    {
        foreach (var companion in companions)
            _context.Companions.Update(companion);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
