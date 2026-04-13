using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services.Snapshots;

/// <summary>
/// Builds and broadcasts the full Inventory snapshot. The payload shape mirrors the one
/// emitted by OpenInventoryCommandHandler and LockItemCommandHandler so existing client
/// subscribers require no changes.
/// </summary>
public class InventorySnapshotService(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService)
{
    public async Task<bool> BroadcastAsync(Guid playerId, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(playerId, ct);
        if (player is null) return false;

        var items = await itemRepository.GetByOwnerAsync(playerId, ct);

        var payload = new
        {
            PlayerId = player.Id,
            player.Name,
            ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
            CraftingSkill = player.CraftingSkill,
            SalvageSkill = player.SalvageSkill,
            AutoSalvageWeaponThreshold = player.AutoSalvageWeaponThreshold,
            AutoSalvageArmorThreshold = player.AutoSalvageArmorThreshold,
            EquippedItems = player.EquippedItems.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.ToString()),
            Items = items.Select(i => new
            {
                Id = i.Id.ToString(),
                i.Name,
                i.Description,
                Workmanship = i.Workmanship.Value,
                Category = i.Category.ToString(),
                Slot = i.Slot.ToString(),
                i.Quantity,
                i.IsStackable,
                i.IsLocked,
                i.IsUnstable,
                MaxImbueSlots = i.MaxImbueSlots,
                Imbues = i.Imbues.Select(imbue => new
                {
                    Type = imbue.Type.ToString(),
                    imbue.Power
                }).ToList()
            }).ToList()
        };

        await notificationService.SendEventAsync(playerId, "Inventory", payload, ct);
        return true;
    }
}
