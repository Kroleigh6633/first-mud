using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services.Snapshots;

/// <summary>
/// Builds and broadcasts the full StorageView snapshot. Mirrors OpenStorageCommandHandler's
/// payload so existing client subscribers receive identical data.
/// </summary>
public class StorageSnapshotService(
    IHomesteadRepository homesteadRepository,
    IItemRepository itemRepository,
    ICompanionRepository companionRepository,
    GameNotificationService notificationService)
{
    public async Task<bool> BroadcastAsync(Guid playerId, CancellationToken ct)
    {
        var homestead = await homesteadRepository.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null) return false;

        var companions = await companionRepository.GetByOwnerAsync(playerId, ct);
        var guardBondLevels = companions
            .Where(c => c.AssignedDuty == HomesteadDuty.Guard && !c.IsPermanentlyGone)
            .Select(c => c.CurrentLayer);
        var effectiveSlots = homestead.EffectiveStorageSlots(guardBondLevels);

        var storageEntries = await homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
        var itemIds = storageEntries.Select(s => s.ItemId).ToList();
        var items = await itemRepository.GetByIdsAsync(itemIds, ct);

        var payload = new
        {
            HomesteadId = homestead.Id,
            homestead.Name,
            StorageSlots = effectiveSlots,
            UsedSlots = storageEntries.Count,
            Items = items.Select(i => new
            {
                Id = i.Id.ToString(),
                i.Name,
                i.Description,
                Category = i.Category.ToString(),
                Workmanship = i.Workmanship.Value,
                i.Quantity,
                i.IsStackable,
                i.IsUnstable,
                MaxImbueSlots = i.MaxImbueSlots,
                Imbues = i.Imbues.Select(imbue => new
                {
                    Type = imbue.Type.ToString(),
                    imbue.Power
                }).ToList()
            }).ToList()
        };

        await notificationService.SendEventAsync(playerId, "StorageView", payload, ct);
        return true;
    }
}
