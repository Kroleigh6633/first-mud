using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Handlers;

namespace FirstMud.GameServer.Services.Snapshots;

/// <summary>
/// Builds and broadcasts the CityView snapshot. Delegates building DTO construction to
/// CityViewBuilder so the payload shape stays aligned with ViewCityCommandHandler.
/// </summary>
public class CitySnapshotService(
    IHomesteadRepository homesteadRepository,
    IHomesteadBuildingRepository buildingRepository,
    ICompanionRepository companionRepository,
    GameNotificationService notificationService)
{
    public async Task<bool> BroadcastAsync(Guid playerId, CancellationToken ct)
    {
        var homestead = await homesteadRepository.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null) return false;

        var buildings = await buildingRepository.GetByHomesteadIdAsync(homestead.Id, ct);
        var allCompanions = await companionRepository.GetByOwnerAsync(playerId, ct);
        var buildingDtos = await CityViewBuilder.BuildDtosAsync(buildings, allCompanions, companionRepository, ct);

        var payload = new
        {
            homesteadId      = homestead.Id,
            homesteadName    = homestead.Name,
            buildingCount    = buildings.Count,
            constructedCount = buildings.Count(b => b.IsConstructed),
            buildings        = buildingDtos,
        };

        await notificationService.SendEventAsync(playerId, "CityView", payload, ct);
        return true;
    }
}
