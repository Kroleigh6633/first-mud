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

        ResourceType harvestType;
        int harvestAmount;
        string biome;

        var node = nodes.FirstOrDefault(n => n.RemainingYield > 0);
        if (node is null)
        {
            // Fall back to wilderness biome harvesting when no seeded nodes are available
            biome = CombatHelpers.GetBiomeForPosition(nearbyZone, player.Position.X, player.Position.Y);
            harvestType = biome switch
            {
                "forest" or "denseForest" => ResourceType.Wood,
                "mountain" or "snowMountain" => ResourceType.Stone,
                "water" => Random.Shared.Next(2) == 0 ? ResourceType.Sand : ResourceType.Herbs,
                "sand" or "desert" => ResourceType.Sand,
                "swamp" => ResourceType.Herbs,
                "grassland" or "plains" => ResourceType.Herbs,
                "path" => ResourceType.Stone,
                _ => ResourceType.Wood,
            };
            harvestAmount = Random.Shared.Next(1, 3); // wilderness yields 1-2
        }
        else
        {
            biome = CombatHelpers.GetBiomeForPosition(nearbyZone, player.Position.X, player.Position.Y);
            harvestType = node.ResourceType;
            harvestAmount = Random.Shared.Next(1, 4); // named zone nodes yield 1-3
        }

        var currentItems = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        if (!player.CanCarryMore(currentItems.Count))
        {
            await notificationService.SendMessageAsync(cmd.PlayerId, "system", "Your inventory is full!", ct);
            return new CommandResult(false, "Inventory full.");
        }

        int actual;
        if (node is not null)
        {
            actual = node.Harvest(harvestAmount);
            await resourceNodeRepository.UpdateAsync(node, ct);
        }
        else
        {
            actual = harvestAmount;
        }

        var resourceName = harvestType.ToString();
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

        var biomeLabel = biome switch
        {
            "forest" or "denseForest" => "the dense forest",
            "mountain" or "snowMountain" => "the mountain slopes",
            "water" => "the shoreline",
            "sand" or "desert" => "the arid wastes",
            "swamp" => "the boggy marsh",
            "grassland" or "plains" => "the open meadow",
            "path" => "the roadside gravel",
            "wyrd" => "the wyrd-touched ground",
            _ => "the wilderness",
        };
        var verb = harvestType switch
        {
            ResourceType.Wood => "cut",
            ResourceType.Stone or ResourceType.Metal => "collected",
            ResourceType.Sand => "scooped",
            ResourceType.Herbs => "gathered",
            _ => "harvested",
        };
        var message = $"You {verb} {actual} {resourceName} from {biomeLabel}.";
        await notificationService.SendMessageAsync(cmd.PlayerId, "loot", message, ct);

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("HarvestComplete", new
            {
                ItemId = resourceItem.Id,
                resourceItem.Name,
                Amount = actual,
                ResourceType = harvestType.ToString()
            }, ct);

        player.GainExperience(5);
        await playerRepository.UpdateAsync(player, ct);
        await notificationService.SendMessageAsync(cmd.PlayerId, "system", "You gained 5 experience from harvesting.", ct);
        await combatHelpers.BroadcastLevelUpEventsAsync(cmd.PlayerId, player, ct);
        player.ClearDomainEvents();

        return new CommandResult(true, message);
    }
}
