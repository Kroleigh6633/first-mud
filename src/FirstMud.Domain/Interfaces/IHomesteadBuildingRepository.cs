using FirstMud.Domain.Entities;

namespace FirstMud.Domain.Interfaces;

public interface IHomesteadBuildingRepository
{
    Task<IReadOnlyList<HomesteadBuilding>> GetByHomesteadIdAsync(Guid homesteadId, CancellationToken ct = default);
    Task<HomesteadBuilding?> GetByIdAsync(Guid buildingId, CancellationToken ct = default);
    Task AddAsync(HomesteadBuilding building, CancellationToken ct = default);
    Task UpdateAsync(HomesteadBuilding building, CancellationToken ct = default);
    Task<IReadOnlyList<HomesteadBuilding>> GetUnderConstructionAsync(CancellationToken ct = default);
}
