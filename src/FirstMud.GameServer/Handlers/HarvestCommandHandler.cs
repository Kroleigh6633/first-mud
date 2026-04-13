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
    IQuestGraphRepository questGraphRepository,
    QuestProgressTracker questProgressTracker,
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
                "forest" or "denseForest"
                    => Random.Shared.Next(4) == 0 ? ResourceType.Herbs : ResourceType.Wood,
                "mountain" or "snowMountain"
                    => Random.Shared.Next(3) == 0 ? ResourceType.Metal : ResourceType.Stone,
                "water"
                    => Random.Shared.Next(3) == 0 ? ResourceType.Herbs : ResourceType.Sand,
                "sand" or "desert"
                    => Random.Shared.Next(4) == 0 ? ResourceType.Metal : ResourceType.Sand,
                "swamp"
                    => Random.Shared.Next(5) == 0 ? ResourceType.Wood : ResourceType.Herbs,
                "grassland" or "plains" => ResourceType.Herbs,
                "path"                  => ResourceType.Stone,
                "wyrd"
                    => Random.Shared.Next(3) == 0 ? ResourceType.Metal : ResourceType.Herbs,
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

        // Compute danger level for this position so we can pick the right specific item
        int dangerLevel = nearbyZone is not null
            ? nearbyZone.DangerLevel
            : BiomeService.GetWildernessDanger(player.Position.X, player.Position.Y, biome);

        var itemName = (biome, harvestType, dangerLevel) switch
        {
            // Mountains
            ("mountain" or "snowMountain", ResourceType.Metal, <= 3) => "Copper Ore",
            ("mountain" or "snowMountain", ResourceType.Metal, <= 5) => "Iron Ore",
            ("mountain" or "snowMountain", ResourceType.Metal, <= 7) => "Silver Ore",
            ("mountain" or "snowMountain", ResourceType.Metal, _)    => "Mithril Ore",
            ("mountain" or "snowMountain", ResourceType.Stone, <= 4) => "Stone",
            ("mountain" or "snowMountain", ResourceType.Stone, _)    => "Granite Block",
            ("mountain" or "snowMountain", ResourceType.Herbs, <= 4) => "Sage",
            ("mountain" or "snowMountain", ResourceType.Herbs, _)    => "Fire Moss",
            // Forests
            ("forest" or "denseForest", ResourceType.Wood, <= 3) => "Oak Wood",
            ("forest" or "denseForest", ResourceType.Wood, <= 6) => "Thornwood",
            ("forest" or "denseForest", ResourceType.Wood, _)    => "Ashwood",
            ("forest" or "denseForest", ResourceType.Herbs, <= 3) => "Sage",
            ("forest" or "denseForest", ResourceType.Herbs, <= 5) => "Lavender",
            ("forest" or "denseForest", ResourceType.Herbs, _)   => "Nightshade",
            // Plains
            ("grassland" or "plains", ResourceType.Herbs, <= 3) => "Sage",
            ("grassland" or "plains", ResourceType.Herbs, <= 5) => "Mint",
            ("grassland" or "plains", ResourceType.Herbs, _)    => "Lavender",
            ("grassland" or "plains", ResourceType.Metal, <= 4) => "Tin Nugget",
            ("grassland" or "plains", ResourceType.Metal, _)    => "Copper Ore",
            // Swamp
            ("swamp", ResourceType.Herbs, <= 3) => "Sage",
            ("swamp", ResourceType.Herbs, <= 6) => "Bogweed",
            ("swamp", ResourceType.Herbs, _)    => "Marsh Gas Crystal",
            ("swamp", ResourceType.Wood, _)     => "Bogwood",
            ("swamp", ResourceType.Metal, _)    => "Bog Iron",
            // Coast / water
            ("water", ResourceType.Sand, _)  => "Sand",
            ("water", ResourceType.Stone, _) => "Coral Fragment",
            ("water", ResourceType.Herbs, <= 3) => "Mint",
            ("water", ResourceType.Herbs, _) => "Sea Kelp",
            // Desert
            ("sand" or "desert", ResourceType.Sand, _)        => "Sand",
            ("sand" or "desert", ResourceType.Metal, <= 5)    => "Iron Ore",
            ("sand" or "desert", ResourceType.Metal, _)       => "Obsidian Shard",
            ("sand" or "desert", ResourceType.Herbs, <= 3)    => "Thornroot",
            ("sand" or "desert", ResourceType.Herbs, _)       => "Sand Thistle",
            // Wyrd
            ("wyrd", ResourceType.Metal, _)  => "Dravenite Dust",
            ("wyrd", ResourceType.Stone, _)  => "Wyrdstone",
            ("wyrd", ResourceType.Herbs, _)  => "Thornroot",
            // Path
            ("path", ResourceType.Stone, _) => "Gravel",
            // Fallback: never return the generic enum name "Metal"
            (_, ResourceType.Metal, _) => "Copper Ore",
            _ => harvestType.ToString(),
        };

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
                $"A resource gathered from the wilds: {itemName.ToLowerInvariant()}.",
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
            "mountain"                => "the mountain slopes",
            "snowMountain"            => "the snow-capped peaks",
            "water"                   => "the shoreline",
            "sand" or "desert"        => "the arid wastes",
            "swamp"                   => "the boggy marsh",
            "grassland" or "plains"   => "the open meadow",
            "path"                    => "the roadside gravel",
            "wyrd"                    => "the wyrd-touched ground",
            _                         => "the wilderness",
        };
        var verb = harvestType switch
        {
            ResourceType.Wood                     => "cut",
            ResourceType.Stone or ResourceType.Metal => "collected",
            ResourceType.Sand                     => "scooped",
            ResourceType.Herbs                    => "gathered",
            _                                     => "harvested",
        };
        var message = $"You {verb} {actual} {itemName} from {biomeLabel}.";
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

        // ── Quest harvest-progress tracking ─────────────────────────────────
        // Mirror the CombatHelpers.TrackQuestKillsAsync pattern: for each
        // active gather/deliver quest whose keyword matches the item we just
        // harvested, record the yield and broadcast QuestHarvestProgress so
        // the client's auto-quest "fulfilling" phase can observe completion.
        await TrackQuestHarvestAsync(cmd.PlayerId, itemName, actual, ct);

        player.GainExperience(5);
        await playerRepository.UpdateAsync(player, ct);
        await notificationService.SendMessageAsync(cmd.PlayerId, "system", "You gained 5 experience from harvesting.", ct);
        await combatHelpers.BroadcastLevelUpEventsAsync(cmd.PlayerId, player, ct);
        player.ClearDomainEvents();

        return new CommandResult(true, message);
    }

    /// <summary>
    /// Records gather-quest progress after a successful harvest and broadcasts
    /// <c>QuestHarvestProgress</c> events — payload shape mirrors
    /// <c>QuestKillProgress</c> so the client can treat them uniformly.
    /// </summary>
    private async Task TrackQuestHarvestAsync(
        Guid playerId,
        string harvestedItemName,
        int amount,
        CancellationToken ct)
    {
        if (amount <= 0) return;

        var inProgressQuests = await questGraphRepository.GetAvailableQuestsAsync(playerId, null, ct);
        var gatherQuests = inProgressQuests
            .Where(q => q.IsTaken && IsGatherQuest(q.Title))
            .ToList();
        if (gatherQuests.Count == 0) return;

        // Use the player's actual inventory so the progress count reflects
        // reality, not a decoupled counter. This keeps the auto-complete
        // handler (which also inspects inventory) in sync with the client.
        var currentItems = await itemRepository.GetByOwnerAsync(playerId, ct);

        foreach (var quest in gatherQuests)
        {
            var keyword = QuestAutoCompleteService.ExtractItemKeyword(quest.Description);
            if (keyword is null) continue;

            var matching = QuestAutoCompleteService.FindMatchingItemsForKeyword(currentItems, keyword);
            // Only broadcast if the just-harvested item actually matches this quest
            if (!matching.Any(i => i.Name.Equals(harvestedItemName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var heldCount = matching.Sum(i => i.IsStackable ? i.Quantity : 1);
            var required  = QuestAutoCompleteService.ParseItemCount(quest.Description);

            // Keep the shared counter fresh for any server-side consumers
            questProgressTracker.RecordItemGathered(playerId, quest.QuestId, amount);

            await hubContext.Clients
                .Group(playerId.ToString())
                .SendAsync("QuestHarvestProgress", new
                {
                    QuestId  = quest.QuestId,
                    Gathered = heldCount,
                    Required = required,
                }, ct);
        }
    }

    private static bool IsGatherQuest(string title) =>
        title.Contains("Gather",   StringComparison.OrdinalIgnoreCase)
     || title.Contains("Collect",  StringComparison.OrdinalIgnoreCase)
     || title.Contains("Retrieve", StringComparison.OrdinalIgnoreCase)
     || title.Contains("Bring",    StringComparison.OrdinalIgnoreCase)
     || title.Contains("Deliver",  StringComparison.OrdinalIgnoreCase);
}
