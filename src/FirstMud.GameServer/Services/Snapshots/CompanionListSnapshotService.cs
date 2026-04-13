using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Handlers;

namespace FirstMud.GameServer.Services.Snapshots;

/// <summary>
/// Broadcasts the CompanionList snapshot. Thin wrapper over CompanionDtoHelpers so the
/// orchestrator layer has a single, uniform BroadcastAsync(playerId) entry point.
/// </summary>
public class CompanionListSnapshotService(
    ICompanionRepository companionRepository,
    GameNotificationService notificationService)
{
    public async Task<bool> BroadcastAsync(Guid playerId, CancellationToken ct)
    {
        var companions = await companionRepository.GetByOwnerAsync(playerId, ct);
        var payload = companions
            .Where(c => !c.IsPermanentlyGone)
            .OrderBy(c => c.IsActive ? 0 : 1)
            .ThenByDescending(c => c.CurrentLayer)
            .Select(CompanionDtoHelpers.ToDto)
            .ToList();

        await notificationService.SendEventAsync(playerId, "CompanionList", payload, ct);
        return true;
    }
}
