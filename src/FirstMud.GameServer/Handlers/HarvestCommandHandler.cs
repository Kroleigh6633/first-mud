using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;

namespace FirstMud.GameServer.Handlers;

public class HarvestCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IZoneRepository zoneRepository,
    IResourceNodeRepository resourceNodeRepository,
    CombatHelpers combatHelpers,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext) : ICommandHandler<HarvestCommand>
{
    public async Task<CommandResult> HandleAsync(HarvestCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var zones = await zoneRepository.GetByWorldAsync(player.Position.World, ct);
        var nearbyZone = ZoneProximity.FindNearby(zones, player.Position.X, player.Position.Y);

        if (nearbyZone is null)
            return new CommandResult(false, "There is nothing to harvest here.");

        var nodes = await resourceNodeRepository.GetByZoneIdAsync(nearbyZone.Id, ct);
        if (nodes.Count == 0)
            return new CommandResult(false, "No resource nodes in this area.");

        var node = nodes.FirstOrDefault(n => n.RemainingYield > 0);
        if (node is null)
            return new CommandResult(false, "The resources here are depleted. Return later.");

        var currentItems = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        if (!player.CanCarryMore(currentItems.Count))
        {
            await notificationService.SendMessageAsync(cmd.PlayerId, "system", "Your inventory is full!", ct);
            return new CommandResult(false, "Inventory full.");
        }

        var harvestAmount = Random.Shared.Next(1, 4);
        var actual = node.Harvest(harvestAmount);
        await resourceNodeRepository.UpdateAsync(node, ct);

        var resourceName = node.ResourceType.ToString();
        var itemName = resourceName; // e.g. "Wood", "Stone", "Metal"

        // Check for an existing stack and merge, otherwise create new
        var existingStack = await itemRepository.GetByOwnerAndNameAsync(
            cmd.PlayerId, itemName, ItemCategory.Component, ct);

        Item resourceItem;
        if (existingStack is not null)
        {
            existingStack.AddQuantity(actual);
            await itemRepository.UpdateAsync(existingStack, ct);
            resourceItem = existingStack;
        }
        else
        {
            resourceItem = Item.Create(
                itemName,
                $"A resource gathered from the wilds: {resourceName.ToLowerInvariant()}.",
                ItemCategory.Component,
                Workmanship.Of(1),
                player.Position.World);
            resourceItem.SetOwner(cmd.PlayerId);
            if (actual > 1) resourceItem.AddQuantity(actual - 1);
            await itemRepository.AddAsync(resourceItem, ct);
        }

        var message = $"You harvested {actual} unit(s) of {resourceName}.";
        await notificationService.SendMessageAsync(cmd.PlayerId, "loot", message, ct);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("HarvestComplete", new
            {
                ItemId = resourceItem.Id,
                resourceItem.Name,
                Amount = actual,
                ResourceType = node.ResourceType.ToString()
            }, ct);

        player.GainExperience(5);
        await playerRepository.UpdateAsync(player, ct);
        await notificationService.SendMessageAsync(cmd.PlayerId, "system", "You gained 5 experience from harvesting.", ct);
        await combatHelpers.BroadcastLevelUpEventsAsync(cmd.PlayerId, player, ct);
        player.ClearDomainEvents();

        return new CommandResult(true, message);
    }
}
