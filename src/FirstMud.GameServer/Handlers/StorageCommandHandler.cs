using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class DepositCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    ICompanionRepository companionRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<DepositCommand>
{
    public async Task<CommandResult> HandleAsync(DepositCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.Position.X != -100 || player.Position.Y != -100)
            return new CommandResult(false, "You must be at your homestead to deposit items.");

        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "Homestead not found.");

        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "Item not found in your inventory.");

        // Count guard companions for storage capacity bonus
        var companions = await companionRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        var guardCount = companions.Count(c => c.AssignedDuty == HomesteadDuty.Guard && !c.IsPermanentlyGone);
        var effectiveSlots = homestead.EffectiveStorageSlots(guardCount);

        // Each row in storage = 1 slot (stack), regardless of quantity
        var storageEntries = await homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
        int usedSlots = storageEntries.Count;

        // For stackable items: try to merge into an existing matching stack in storage
        if (item.IsStackable)
        {
            var storageItemIds = storageEntries.Select(s => s.ItemId).ToList();
            var storageItemEntities = await itemRepository.GetByIdsAsync(storageItemIds, ct);
            var existingStack = storageItemEntities.FirstOrDefault(s =>
                s.Name == item.Name && s.Category == item.Category && s.IsStackable);

            if (existingStack is not null)
            {
                // Merge: add quantity to the existing stack and delete the deposited item
                existingStack.AddQuantity(item.Quantity);
                await itemRepository.UpdateAsync(existingStack, ct);
                await itemRepository.DeleteAsync(item.Id, ct);

                await notificationService.SendMessageAsync(
                    cmd.PlayerId, "system",
                    $"Merged {item.Quantity}x {item.Name} into existing storage stack (now {existingStack.Quantity}x).", ct);

                await hubContext.Clients
                    .Group(cmd.PlayerId.ToString())
                    .SendAsync("StorageUpdated", new { homesteadId = homestead.Id }, ct);

                return new CommandResult(true, $"Deposited {item.Name} (merged into existing stack).");
            }
        }

        // No existing stack to merge into — need a free slot
        if (usedSlots >= effectiveSlots)
            return new CommandResult(false, "Homestead storage is full.");

        item.SetOwner(null);
        await itemRepository.UpdateAsync(item, ct);

        var storageItem = HomesteadStorageItem.Create(homestead.Id, item.Id);
        await homesteadRepository.AddStorageItemAsync(storageItem, ct);

        await notificationService.SendMessageAsync(
            cmd.PlayerId, "system", $"Deposited {item.Name} into homestead storage.", ct);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("StorageUpdated", new { homesteadId = homestead.Id }, ct);

        return new CommandResult(true, $"Deposited {item.Name}.");
    }
}

public class WithdrawCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<WithdrawCommand>
{
    public async Task<CommandResult> HandleAsync(WithdrawCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.Position.X != -100 || player.Position.Y != -100)
            return new CommandResult(false, "You must be at your homestead to withdraw items.");

        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "Homestead not found.");

        var storageItems = await homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
        var storageEntry = storageItems.FirstOrDefault(s => s.ItemId == cmd.ItemId);
        if (storageEntry is null)
            return new CommandResult(false, "Item not found in storage.");

        var inventoryItems = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        if (!player.CanCarryMore(inventoryItems.Count))
            return new CommandResult(false, "Your inventory is full!");

        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null)
            return new CommandResult(false, "Item not found.");

        item.SetOwner(cmd.PlayerId);
        await itemRepository.UpdateAsync(item, ct);
        await homesteadRepository.RemoveStorageItemAsync(homestead.Id, cmd.ItemId, ct);

        await notificationService.SendMessageAsync(
            cmd.PlayerId, "system", $"Withdrew {item.Name} from homestead storage.", ct);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("StorageUpdated", new { homesteadId = homestead.Id }, ct);

        return new CommandResult(true, $"Withdrew {item.Name}.");
    }
}

public class OpenStorageCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    ICompanionRepository companionRepository,
    GameNotificationService notificationService) : ICommandHandler<OpenStorageCommand>
{
    public async Task<CommandResult> HandleAsync(OpenStorageCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (player.Position.X != -100 || player.Position.Y != -100)
            return new CommandResult(false, "You must be at your homestead to open storage.");

        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is null)
            return new CommandResult(false, "Homestead not found.");

        // Compute effective capacity including guard companion bonus
        var companions = await companionRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        var guardCount = companions.Count(c => c.AssignedDuty == HomesteadDuty.Guard && !c.IsPermanentlyGone);
        var effectiveSlots = homestead.EffectiveStorageSlots(guardCount);

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

        await notificationService.SendEventAsync(cmd.PlayerId, "StorageView", payload, ct);

        return new CommandResult(true, "Storage opened.", payload);
    }
}
