using FirstMud.Application.Services;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class SmeltCommandHandler(
    SmeltService smeltService,
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<SmeltCommand>
{
    public async Task<CommandResult> HandleAsync(SmeltCommand cmd, CancellationToken ct)
    {
        var result = await smeltService.SmeltAsync(cmd.PlayerId, cmd.Amount, ct);

        var category = result.Success ? "loot" : "system";
        await notificationService.SendMessageAsync(cmd.PlayerId, category, result.Message, ct);

        if (!result.Success)
            return new CommandResult(false, result.Message);

        // Push updated storage so the client sees the new ore stacks
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        var homestead = player is not null
            ? await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct)
            : null;

        if (homestead is not null)
        {
            var storageEntries = await homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
            var itemIds = storageEntries.Select(s => s.ItemId).ToList();
            var items = await itemRepository.GetByIdsAsync(itemIds, ct);

            var storagePayload = new
            {
                HomesteadId = homestead.Id,
                homestead.Name,
                StorageSlots = homestead.StorageSlots,
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

            await notificationService.SendEventAsync(cmd.PlayerId, "StorageView", storagePayload, ct);
            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("StorageUpdated", new { homesteadId = homestead.Id }, ct);
        }

        // Also refresh inventory (Metal stack may have been partially consumed from inventory)
        if (player is not null)
        {
            var invItems = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
            var invPayload = new
            {
                PlayerId = player.Id,
                player.Name,
                ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
                CraftingSkill = player.CraftingSkill,
                SalvageSkill = player.SalvageSkill,
                AutoSalvageWeaponThreshold = player.AutoSalvageWeaponThreshold,
                AutoSalvageArmorThreshold = player.AutoSalvageArmorThreshold,
                Items = invItems.Select(i => new
                {
                    Id = i.Id.ToString(),
                    i.Name,
                    i.Description,
                    Workmanship = i.Workmanship.Value,
                    Category = i.Category.ToString(),
                    i.Quantity,
                    i.IsStackable,
                    i.IsLocked
                }).ToList()
            };
            await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", invPayload, ct);
        }

        var smeltPayload = new
        {
            Yields = result.Yields.Select(y => new { y.Name, y.Quantity }).ToList(),
            Message = result.Message
        };

        await notificationService.SendEventAsync(cmd.PlayerId, "SmeltComplete", smeltPayload, ct);

        return new CommandResult(true, result.Message, smeltPayload);
    }
}
