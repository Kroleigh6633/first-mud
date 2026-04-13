using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.Infrastructure.Repositories.Sql;

internal sealed class HomesteadBuildingRepository : IHomesteadBuildingRepository
{
    private readonly GameDbContext _context;

    public HomesteadBuildingRepository(GameDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<HomesteadBuilding>> GetByHomesteadIdAsync(Guid homesteadId, CancellationToken ct = default)
    {
        return await _context.HomesteadBuildings
            .Where(b => b.HomesteadId == homesteadId)
            .ToListAsync(ct);
    }

    public async Task<HomesteadBuilding?> GetByIdAsync(Guid buildingId, CancellationToken ct = default)
    {
        return await _context.HomesteadBuildings
            .FirstOrDefaultAsync(b => b.Id == buildingId, ct);
    }

    public async Task AddAsync(HomesteadBuilding building, CancellationToken ct = default)
    {
        await _context.HomesteadBuildings.AddAsync(building, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(HomesteadBuilding building, CancellationToken ct = default)
    {
        _context.HomesteadBuildings.Update(building);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<HomesteadBuilding>> GetUnderConstructionAsync(CancellationToken ct = default)
    {
        return await _context.HomesteadBuildings
            .Where(b => !b.IsConstructed)
            .ToListAsync(ct);
    }
}
