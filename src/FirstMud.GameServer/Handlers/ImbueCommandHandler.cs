// ─────────────────────────────────────────────────────────────────────────
// Legacy taper imbue path (keyword-driven).
//
// This handler drives ImbueService.ImbueAsync, which resolves the imbue type
// from the taper's display name (ResolveImbueType) using substring matches
// like "fire shaping", "wyrd taper", etc. Taper recipes are NOT content-
// driven and NOT validated against content/imbue-recipes.json.
//
// The NEW gem-based imbue flow lives in ImbueWithGemCommandHandler and uses
// IContentProvider + explicit recipe ids. The two flows are intentionally
// split: tapers are keyword-matched consumables, gems are structured content
// with per-recipe skill gates and upgrade semantics. Preserving this split
// avoids entangling the legacy taper catalog migration with the gem work.
// ─────────────────────────────────────────────────────────────────────────

using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class ImbueCommandHandler(
    ImbueService imbueService,
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService) : ICommandHandler<ImbueCommand>
{
    public async Task<CommandResult> HandleAsync(ImbueCommand cmd, CancellationToken ct)
    {
        var result = await imbueService.ImbueAsync(cmd.PlayerId, cmd.ItemId, cmd.TaperId, ct);

        var category = result.Success ? "loot" : "system";
        await notificationService.SendMessageAsync(cmd.PlayerId, category, result.Message, ct);

        if (!result.Success)
            return new CommandResult(false, result.Message);

        // Refresh inventory so the client sees the updated imbues
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is not null)
        {
            var items = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
            var inventoryPayload = new
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

            await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", inventoryPayload, ct);
        }

        var imbuePayload = new
        {
            ItemId   = cmd.ItemId.ToString(),
            TaperId  = cmd.TaperId.ToString(),
            result.Success,
            result.Message,
            Outcome  = result.Outcome.ToString()
        };

        await notificationService.SendEventAsync(cmd.PlayerId, "ImbueResult", imbuePayload, ct);

        return new CommandResult(true, result.Message, imbuePayload);
    }
}
