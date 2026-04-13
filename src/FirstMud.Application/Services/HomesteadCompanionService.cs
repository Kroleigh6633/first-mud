using FirstMud.Application.Events;
using FirstMud.Engine.Events;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

/// <summary>
/// Processes homestead companion duties (Harvester, Salvager, Guard, Crafter) once per tick.
/// Called every 60 seconds from GameLoopService.
/// </summary>
public class HomesteadCompanionService
{
    private readonly ICompanionRepository _companions;
    private readonly IHomesteadRepository _homesteads;
    private readonly IHomesteadBuildingRepository _buildings;
    private readonly IItemRepository _items;
    private readonly IGameEventPublisher _events;
    private readonly ILogger<HomesteadCompanionService> _logger;

    // Resource types available for Aeldran harvesting
    private static readonly ResourceType[] AeldranResources =
    [
        ResourceType.Wood, ResourceType.Stone, ResourceType.Herbs,
        ResourceType.Metal, ResourceType.Sand
    ];

    // Tier-1 herbs a Harvester may gather in lieu of the generic "Herbs" resource.
    // Sage is weighted heaviest (design: most-common tier-1).
    private static readonly string[] Tier1HerbPool =
    [
        "Sage", "Sage", "Sage", "Mint", "Mint", "Thornroot", "Lavender"
    ];

    // Tier-3 seed → herb mapping. A Greenhouse harvester consumes one seed and
    // produces the mapped tier-3 herb over a multi-tick cultivation window.
    private static readonly Dictionary<string, string> SeedToTier3Herb = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Moonbloom Seed"]       = "Moonbloom",
        ["Starflower Seed"]      = "Starflower",
        ["Wyrd Blossom Seed"]    = "Wyrd Blossom",
        ["Dragon's Breath Seed"] = "Dragon's Breath",
    };

    // Greenhouse cultivation: ticks required per tier-3 herb unit. Reduced by
    // companion layer so higher-layer companions cultivate faster.
    private const int GreenhouseBaseTicksPerHerb = 6;

    public HomesteadCompanionService(
        ICompanionRepository companions,
        IHomesteadRepository homesteads,
        IHomesteadBuildingRepository buildings,
        IItemRepository items,
        IGameEventPublisher events,
        ILogger<HomesteadCompanionService> logger)
    {
        _companions = companions;
        _homesteads = homesteads;
        _buildings = buildings;
        _items = items;
        _events = events;
        _logger = logger;
    }

    /// <summary>
    /// Processes all companions currently on homestead duty.
    /// Returns a list of broadcast messages keyed by player ID.
    /// </summary>
    public async Task<IReadOnlyList<HomesteadDutyResult>> ProcessAllDutiesAsync(CancellationToken ct = default)
    {
        var results = new List<HomesteadDutyResult>();

        var companions = await _companions.GetAllOnDutyAsync(ct);
        if (companions.Count == 0)
            return results;

        // Group by owner so we only load each homestead once
        var byOwner = companions.GroupBy(c => c.OwnerId);

        foreach (var group in byOwner)
        {
            var playerId = group.Key;
            var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
            if (homestead is null) continue;

            foreach (var companion in group)
            {
                if (!companion.AssignedDuty.HasValue || companion.AssignedDuty == HomesteadDuty.None)
                    continue;

                try
                {
                    var msg = companion.AssignedDuty.Value switch
                    {
                        HomesteadDuty.Harvester => await ProcessHarvesterAsync(companion, homestead, ct),
                        HomesteadDuty.Salvager  => await ProcessSalvagerAsync(companion, homestead, ct),
                        HomesteadDuty.Guard     => ProcessGuard(companion),
                        HomesteadDuty.Crafter   => await ProcessCrafterAsync(companion, homestead, ct),
                        _ => null
                    };

                    // Accumulate usage slowly (homestead duty counts at ~30% of combat rate)
                    companion.RecordUsage(3);
                    await _companions.UpdateAsync(companion, ct);

                    // Companion usage/layer progression changed — refresh Companion panel
                    await _events.PublishAsync(playerId,
                        new CompanionStateChangedEvent(companion.Id, "DutyTick"), ct);

                    if (msg is not null)
                        results.Add(new HomesteadDutyResult(playerId, companion.Name, companion.AssignedDuty.Value, msg));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing {Duty} duty for companion {CompanionId}", companion.AssignedDuty, companion.Id);
                }
            }
        }

        return results;
    }

    // ─── duty processors ─────────────────────────────────────────────────────

    private async Task<string?> ProcessHarvesterAsync(Companion companion, Homestead homestead, CancellationToken ct)
    {
        // If the companion is assigned to a Greenhouse, route to cultivation instead of generic gather.
        var greenhouseBuilding = await FindCompanionGreenhouseAsync(companion, homestead, ct);
        if (greenhouseBuilding is not null)
        {
            var cultivationMsg = await ProcessGreenhouseAsync(companion, homestead, greenhouseBuilding, ct);
            if (cultivationMsg is not null)
                return cultivationMsg;
            // No seed available — fall through to normal harvest so the tick is not wasted.
        }

        var aptitude = companion.GetAptitude(HomesteadDuty.Harvester);
        var yield = (int)(1 * aptitude * (companion.Level / 5.0 + 1));

        // Pick a random resource type weighted toward Aeldran defaults
        var resourceType = AeldranResources[Random.Shared.Next(AeldranResources.Length)];
        // Tier-1 herbs replace the generic "Herbs" resource name.
        var resourceName = resourceType == ResourceType.Herbs
            ? Tier1HerbPool[Random.Shared.Next(Tier1HerbPool.Length)]
            : resourceType.ToString();

        // Check storage capacity (guards increase effective slots)
        var guardBondLevels = await GetGuardBondLevelsAsync(homestead.PlayerId, ct);
        var effectiveSlots = homestead.EffectiveStorageSlots(guardBondLevels);
        var storageItems = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
        if (storageItems.Count >= effectiveSlots)
        {
            _logger.LogDebug("Homestead storage full for player {PlayerId}, skipping harvest.", homestead.PlayerId);
            return null;
        }

        // Find existing component stack in homestead storage with same name
        var existingStorageEntry = await _homesteads.GetStorageItemByNameAsync(homestead.Id, resourceName, ct);
        if (existingStorageEntry is not null)
        {
            var existing = await _items.GetByIdAsync(existingStorageEntry.ItemId, ct);
            if (existing is not null)
            {
                existing.AddQuantity(yield);
                await _items.UpdateAsync(existing, ct);
                await _events.PublishAsync(homestead.PlayerId,
                    new StorageChangedEvent(homestead.Id), ct);
                return $"Your companion {companion.Name} harvested {resourceName} x{yield} (stored in homestead).";
            }
        }

        // Create new stack
        var newItem = Item.Create(
            resourceName,
            $"A raw material gathered by {companion.Name}.",
            ItemCategory.Component,
            Workmanship.Of(1),
            WorldId.Aeldran);
        newItem.SetOwner(null);
        newItem.AddQuantity(yield - 1); // Create starts at 1
        await _items.AddAsync(newItem, ct);

        var storageEntry = HomesteadStorageItem.Create(homestead.Id, newItem.Id);
        await _homesteads.AddStorageItemAsync(storageEntry, ct);

        await _events.PublishAsync(homestead.PlayerId,
            new StorageChangedEvent(homestead.Id), ct);

        return $"Your companion {companion.Name} harvested {resourceName} x{yield} (stored in homestead).";
    }

    // ─── Greenhouse cultivation ─────────────────────────────────────────────
    //
    // Greenhouse buildings have duty=Harvester (per content/buildings.json) so
    // companions assigned to them are processed by ProcessHarvesterAsync. We
    // detect the Greenhouse assignment up-front and divert to cultivation:
    // consume one tier-3 seed from homestead storage, increment a per-tick
    // progress counter on the building, and once progress reaches the
    // layer-scaled threshold, yield one tier-3 herb.
    //
    // For simplicity the progress counter lives in the AssignedCompanion layer
    // concept: growth per tick scales with aptitude * layer, so higher-layer
    // companions finish a cultivation cycle in fewer ticks. We approximate
    // this without persisting extra state by rolling against a probability
    // each tick.

    private async Task<HomesteadBuilding?> FindCompanionGreenhouseAsync(
        Companion companion, Homestead homestead, CancellationToken ct)
    {
        var buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        return buildings.FirstOrDefault(b =>
            b.Type == BuildingType.Greenhouse
            && b.IsConstructed
            && b.HasCompanion(companion.Id));
    }

    private async Task<string?> ProcessGreenhouseAsync(
        Companion companion, Homestead homestead, HomesteadBuilding greenhouse, CancellationToken ct)
    {
        // Find any tier-3 seed in homestead storage.
        var storageItems = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
        Item? seedItem = null;
        string? seedName = null;
        foreach (var entry in storageItems)
        {
            var candidate = await _items.GetByIdAsync(entry.ItemId, ct);
            if (candidate is null) continue;
            if (SeedToTier3Herb.ContainsKey(candidate.Name) && candidate.Quantity > 0)
            {
                seedItem = candidate;
                seedName = candidate.Name;
                break;
            }
        }

        if (seedItem is null || seedName is null)
            return null; // No seeds — let caller fall back to generic harvest.

        var tier3Name = SeedToTier3Herb[seedName];

        // Cultivation chance per tick. Base = 1/6. +1/6 per companion layer,
        // capped at 4/6. Aptitude rounds to +1/6 chance per full star above 1.
        var aptitude = companion.GetAptitude(HomesteadDuty.Harvester);
        var layerBonus = Math.Max(0, companion.CurrentLayer);
        var aptitudeBonus = Math.Max(0, aptitude - 1);
        var successIn6 = Math.Min(5, 1 + layerBonus + aptitudeBonus);

        var roll = Random.Shared.Next(GreenhouseBaseTicksPerHerb);
        if (roll >= successIn6)
        {
            // Cultivation in progress but not ripe this tick.
            return $"Your companion {companion.Name} tends a {seedName} in the Greenhouse (cultivating...).";
        }

        // Consume one seed unit.
        if (seedItem.Quantity > 1)
        {
            seedItem.AddQuantity(-1);
            await _items.UpdateAsync(seedItem, ct);
        }
        else
        {
            // Remove the storage entry + item when the last seed is consumed.
            await _homesteads.RemoveStorageItemAsync(homestead.Id, seedItem.Id, ct);
            await _items.DeleteAsync(seedItem.Id, ct);
        }

        // Deposit tier-3 herb into homestead storage (stack if possible).
        var existingEntry = await _homesteads.GetStorageItemByNameAsync(homestead.Id, tier3Name, ct);
        if (existingEntry is not null)
        {
            var existing = await _items.GetByIdAsync(existingEntry.ItemId, ct);
            if (existing is not null)
            {
                existing.AddQuantity(1);
                await _items.UpdateAsync(existing, ct);
            }
        }
        else
        {
            var guardBondLevels = await GetGuardBondLevelsAsync(homestead.PlayerId, ct);
            var effectiveSlots = homestead.EffectiveStorageSlots(guardBondLevels);
            var currentItems = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
            if (currentItems.Count >= effectiveSlots)
            {
                _logger.LogDebug("Greenhouse cultivation succeeded but storage is full; tier-3 herb dropped for player {PlayerId}.", homestead.PlayerId);
                await _events.PublishAsync(homestead.PlayerId, new StorageChangedEvent(homestead.Id), ct);
                return $"Your companion {companion.Name} cultivated {tier3Name} but homestead storage was full — it spoiled.";
            }

            var newHerb = Item.Create(
                tier3Name,
                $"A tier-3 herb cultivated in the Greenhouse by {companion.Name}.",
                ItemCategory.Reagent,
                Workmanship.Of(4),
                WorldId.Aeldran);
            newHerb.SetOwner(null);
            await _items.AddAsync(newHerb, ct);
            var storageEntry = HomesteadStorageItem.Create(homestead.Id, newHerb.Id);
            await _homesteads.AddStorageItemAsync(storageEntry, ct);
        }

        await _events.PublishAsync(homestead.PlayerId, new StorageChangedEvent(homestead.Id), ct);
        return $"Your companion {companion.Name} cultivated {tier3Name} x1 in the Greenhouse (consumed 1 {seedName}).";
    }

    private async Task<string?> ProcessSalvagerAsync(Companion companion, Homestead homestead, CancellationToken ct)
    {
        var nextItemId = homestead.DequeueNextSalvage();
        if (nextItemId is null)
            return null; // Nothing queued

        await _homesteads.UpdateAsync(homestead, ct);

        var item = await _items.GetByIdAsync(nextItemId.Value, ct);
        if (item is null || item.OwnerId != homestead.PlayerId)
        {
            _logger.LogDebug("Queued salvage item {ItemId} not found or wrong owner, skipping.", nextItemId);
            return null;
        }

        // Companions salvage at reduced yield: aptitude_stars * 0.5
        var aptitude = companion.GetAptitude(HomesteadDuty.Salvager);
        var multiplier = aptitude * 0.5;

        var workValue = item.Workmanship.Value;
        var baseYield = item.Category switch
        {
            ItemCategory.Weapon => (material: "Iron Ore", qty: Math.Max(1, (int)(2 * multiplier))),
            ItemCategory.Armor  => (material: "Leather", qty: Math.Max(1, (int)(2 * multiplier))),
            _                   => (material: "Iron Ore", qty: 1)
        };

        var itemName = item.Name;
        var itemWorkStr = $"W{workValue}";

        // Check storage capacity
        var guardBondLevels = await GetGuardBondLevelsAsync(homestead.PlayerId, ct);
        var effectiveSlots = homestead.EffectiveStorageSlots(guardBondLevels);
        var storageItems = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);

        // Delete the original item
        await _items.DeleteAsync(nextItemId.Value, ct);

        if (storageItems.Count < effectiveSlots)
        {
            // Find or create resource stack in homestead storage
            var existingEntry = await _homesteads.GetStorageItemByNameAsync(homestead.Id, baseYield.material, ct);
            if (existingEntry is not null)
            {
                var existing = await _items.GetByIdAsync(existingEntry.ItemId, ct);
                if (existing is not null)
                {
                    existing.AddQuantity(baseYield.qty);
                    await _items.UpdateAsync(existing, ct);
                }
            }
            else
            {
                var newItem = Item.Create(
                    baseYield.material,
                    $"A salvaged material recovered by {companion.Name}.",
                    ItemCategory.Component,
                    Workmanship.Of(1),
                    item.OriginWorld);
                newItem.SetOwner(null);
                newItem.AddQuantity(baseYield.qty - 1);
                await _items.AddAsync(newItem, ct);

                var storageEntry = HomesteadStorageItem.Create(homestead.Id, newItem.Id);
                await _homesteads.AddStorageItemAsync(storageEntry, ct);
            }
        }

        // Salvager removed an item and added yield — storage contents shifted either way.
        await _events.PublishAsync(homestead.PlayerId,
            new StorageChangedEvent(homestead.Id), ct);

        // No salvage XP — companion does the work
        return $"Your companion {companion.Name} salvaged {itemName} {itemWorkStr} → {baseYield.material} x{baseYield.qty} (stored in homestead).";
    }

    private static string? ProcessGuard(Companion companion)
    {
        // Guard duty effect (storage bonus) is computed dynamically via GetGuardBondLevelsAsync.
        // No per-tick broadcast needed — assignment message was sent at assignment time.
        return null;
    }

    /// <summary>
    /// Crafter duty handler. Today the only automated craft is Forge smelting
    /// (Metal → specific ores). If the crafter companion is assigned to a
    /// constructed Forge, smelting runs; otherwise crafter duty is a no-op
    /// (other crafting buildings will be wired through here in future).
    /// </summary>
    private async Task<string?> ProcessCrafterAsync(Companion companion, Homestead homestead, CancellationToken ct)
    {
        var buildings = await _buildings.GetByHomesteadIdAsync(homestead.Id, ct);
        var forge = buildings.FirstOrDefault(b =>
            b.IsConstructed &&
            b.Type == BuildingType.Forge &&
            b.HasCompanion(companion.Id));

        if (forge is not null)
            return await ProcessSmelterAsync(companion, homestead, forge, ct);

        return null;
    }

    // ─── Smelter (Forge) duty ────────────────────────────────────────────────

    // Per-unit ore roll distribution. Mirrors SmeltService.RollOre but keyed on
    // the companion's layer (1–6) rather than the player's CraftingSkill.
    // Each layer above 1 shifts 2% from Iron into rarer results.
    public static string RollForgeOre(int companionLayer, Random rng)
    {
        var shifts = Math.Clamp(companionLayer - 1, 0, 10); // layer 1 = 0 shifts, layer 6 = 5 shifts (≤10 cap)
        var iron    = 50 - shifts * 2;        // 50% → 40% at layer 6
        var copper  = 25;
        var tin     = 15 + shifts;            // 15% → 20%
        var silver  =  8 + shifts / 2;        //  8% → 10%
        var mithril = 100 - iron - copper - tin - silver; // 2% → 5%

        var roll = rng.Next(100);
        if (roll < iron) return "Iron Ore";
        if (roll < iron + copper) return "Copper Nugget";
        if (roll < iron + copper + tin) return "Tin";
        if (roll < iron + copper + tin + silver) return "Silver Ore";
        return "Mithril Ore";
    }

    /// <summary>
    /// Throughput (units of Metal consumed per tick) given companion layer
    /// and forge tier. Higher layer + higher tier = more units processed.
    ///
    ///   base units     = max(1, layer / 2)    // layer 1-2 → 1; 3-4 → 2; 5-6 → 3
    ///   tier bonus     = (tier - 1) * layer / 5    // tier 2 l=5 → +1, tier 3 l=5 → +2
    ///
    /// A typical mid-game forge (tier 1, layer 3 companion): 1 unit/tick.
    /// Late-game (tier 3, layer 5 companion): 3 + 2 = 5 units/tick.
    /// </summary>
    public static int ForgeThroughput(int companionLayer, int forgeTier)
    {
        var baseUnits = Math.Max(1, companionLayer / 2);
        var tierBonus = Math.Max(0, (forgeTier - 1) * companionLayer / 5);
        return baseUnits + tierBonus;
    }

    private async Task<string?> ProcessSmelterAsync(
        Companion companion,
        Homestead homestead,
        HomesteadBuilding forge,
        CancellationToken ct)
    {
        // Locate "Metal" in homestead storage (produced by salvage / auto-salvage).
        var metalEntry = await _homesteads.GetStorageItemByNameAsync(homestead.Id, "Metal", ct);
        if (metalEntry is null) return null;

        var metal = await _items.GetByIdAsync(metalEntry.ItemId, ct);
        if (metal is null || metal.Quantity <= 0) return null;

        // Throughput: companion layer × forge tier.
        var throughput = ForgeThroughput(companion.CurrentLayer, forge.Tier);
        var toSmelt = Math.Min(throughput, metal.Quantity);
        if (toSmelt <= 0) return null;

        // Check storage capacity — we may need new ore stacks.
        var guardBondLevels = await GetGuardBondLevelsAsync(homestead.PlayerId, ct);
        var effectiveSlots = homestead.EffectiveStorageSlots(guardBondLevels);
        var storageItems = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);

        // Consume Metal first.
        if (metal.Quantity == toSmelt)
        {
            await _items.DeleteAsync(metal.Id, ct);
            await _homesteads.RemoveStorageItemAsync(homestead.Id, metal.Id, ct);
        }
        else
        {
            metal.TryRemoveQuantity(toSmelt, out _);
            await _items.UpdateAsync(metal, ct);
        }

        // Roll outputs.
        var tally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toSmelt; i++)
        {
            var ore = RollForgeOre(companion.CurrentLayer, Random.Shared);
            tally.TryGetValue(ore, out var current);
            tally[ore] = current + 1;
        }

        // Deposit outputs, respecting storage capacity for new stacks.
        // Existing stacks always absorb; new stacks only if a slot is free.
        var freeSlots = effectiveSlots - storageItems.Count + (metal.Quantity == toSmelt ? 1 : 0);
        foreach (var (oreName, qty) in tally)
        {
            var existingEntry = await _homesteads.GetStorageItemByNameAsync(homestead.Id, oreName, ct);
            if (existingEntry is not null)
            {
                var existing = await _items.GetByIdAsync(existingEntry.ItemId, ct);
                if (existing is not null)
                {
                    existing.AddQuantity(qty);
                    await _items.UpdateAsync(existing, ct);
                    continue;
                }
            }

            if (freeSlots <= 0)
            {
                _logger.LogDebug(
                    "Forge smelted {Qty}x {Ore} but homestead storage is full; discarded.",
                    qty, oreName);
                continue;
            }

            var newItem = Item.Create(
                oreName,
                $"An ore smelted at the forge by {companion.Name}.",
                ItemCategory.Component,
                Workmanship.Of(1),
                WorldId.Aeldran);
            newItem.SetOwner(null);
            if (qty > 1) newItem.AddQuantity(qty - 1);
            await _items.AddAsync(newItem, ct);

            var entry = HomesteadStorageItem.Create(homestead.Id, newItem.Id);
            await _homesteads.AddStorageItemAsync(entry, ct);
            freeSlots--;
        }

        await _events.PublishAsync(homestead.PlayerId,
            new StorageChangedEvent(homestead.Id), ct);

        var summary = string.Join(", ", tally.Select(kv => $"{kv.Key} x{kv.Value}"));
        return $"Your forge smelted: {summary} — by {companion.Name}-L{companion.CurrentLayer} (tier {forge.Tier}).";
    }

    private async Task<IEnumerable<int>> GetGuardBondLevelsAsync(Guid playerId, CancellationToken ct)
    {
        var companions = await _companions.GetByOwnerAsync(playerId, ct);
        return companions
            .Where(c => c.AssignedDuty == HomesteadDuty.Guard && !c.IsPermanentlyGone)
            .Select(c => c.CurrentLayer);
    }
}

public record HomesteadDutyResult(Guid PlayerId, string CompanionName, HomesteadDuty Duty, string Message);
