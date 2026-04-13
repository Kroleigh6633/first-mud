using FirstMud.Application.Events;
using FirstMud.Engine.Events;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;
using FirstMud.GameServer.Services.Snapshots;

namespace FirstMud.GameServer.Handlers;

public class OpenInventoryCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService) : ICommandHandler<OpenInventoryCommand>
{
    public async Task<CommandResult> HandleAsync(OpenInventoryCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var items = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);

        var payload = new
        {
            PlayerId = player.Id,
            player.Name,
            ActiveCompanionIds = player.ActiveCompanionIds.Select(id => id.ToString()).ToList(),
            CraftingSkill = player.CraftingSkill,
            SalvageSkill = player.SalvageSkill,
            AutoSalvageWeaponThreshold = player.AutoSalvageWeaponThreshold,
            AutoSalvageArmorThreshold = player.AutoSalvageArmorThreshold,
            // Include the full equipment map so the client can restore equipped state
            // on every inventory open, without depending on EquipmentChanged being
            // re-sent.
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

        await notificationService.SendEventAsync(cmd.PlayerId, "Inventory", payload, ct);

        return new CommandResult(true, "Inventory opened.", payload);
    }
}

public class EquipCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService,
    IGameEventPublisher eventPublisher) : ICommandHandler<EquipCommand>
{
    public async Task<CommandResult> HandleAsync(EquipCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "Item not found in your inventory.");

        var previousId = player.Equip(item);
        await playerRepository.UpdateAsync(player, ct);

        // Send the full equipment state so the client always has a consistent view
        var equippedPayload = new
        {
            EquippedItems = player.EquippedItems.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.ToString()),
            ChangedItemId = item.Id.ToString(),
            ChangedItemName = item.DisplayName,
            Slot = item.Slot.ToString(),
            Category = item.Category.ToString(),
            ReplacedItemId = previousId?.ToString()
        };

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", $"You equipped {item.DisplayName}.", ct);
        await notificationService.SendEventAsync(cmd.PlayerId, "EquipmentChanged", equippedPayload, ct);

        // Inventory & Crafting panels derive availability from equipped state — refresh both.
        await eventPublisher.PublishAsync(cmd.PlayerId,
            new EquipmentChangedEvent(EquippedItemId: item.Id, UnequippedItemId: previousId), ct);

        return new CommandResult(true, $"Equipped {item.DisplayName}.", equippedPayload);
    }
}

public class LockItemCommandHandler(
    IItemRepository itemRepository,
    GameNotificationService notificationService,
    InventorySnapshotService inventorySnapshots) : ICommandHandler<LockItemCommand>
{
    public async Task<CommandResult> HandleAsync(LockItemCommand cmd, CancellationToken ct)
    {
        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "Item not found in your inventory.");

        item.ToggleLock();
        await itemRepository.UpdateAsync(item, ct);

        var lockState = item.IsLocked ? "locked" : "unlocked";
        await notificationService.SendMessageAsync(cmd.PlayerId, "system", $"{item.DisplayName} is now {lockState}.", ct);
        await inventorySnapshots.BroadcastAsync(cmd.PlayerId, ct);

        return new CommandResult(true, $"{item.Name} {lockState}.", new { ItemId = item.Id, item.IsLocked });
    }
}

public class UnequipCommandHandler(
    IPlayerRepository playerRepository,
    GameNotificationService notificationService,
    IGameEventPublisher eventPublisher) : ICommandHandler<UnequipCommand>
{
    public async Task<CommandResult> HandleAsync(UnequipCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        if (!Enum.TryParse<EquipmentSlot>(cmd.Slot, ignoreCase: true, out var slot) || slot == EquipmentSlot.None)
            return new CommandResult(false,
                $"Unknown equipment slot: {cmd.Slot}. Valid: MeleeWeapon, RangedWeapon, Focus, Head, Chest, Legs, Hands, Feet, Accessory.");

        var removedId = player.Unequip(slot);
        if (removedId is null)
            return new CommandResult(false, "No item equipped in that slot.");

        await playerRepository.UpdateAsync(player, ct);

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", $"You unequipped the {cmd.Slot} item.", ct);

        // Send the full equipment state so the client always has a consistent view
        var unequipPayload = new
        {
            EquippedItems = player.EquippedItems.ToDictionary(
                kv => kv.Key.ToString(),
                kv => kv.Value.ToString()),
            RemovedSlot = cmd.Slot
        };
        await notificationService.SendEventAsync(cmd.PlayerId, "EquipmentChanged", unequipPayload, ct);

        await eventPublisher.PublishAsync(cmd.PlayerId,
            new EquipmentChangedEvent(EquippedItemId: null, UnequippedItemId: removedId), ct);

        return new CommandResult(true, $"Unequipped {cmd.Slot} item.");
    }
}
