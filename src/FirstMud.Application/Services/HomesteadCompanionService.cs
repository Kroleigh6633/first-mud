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
    private readonly IItemRepository _items;
    private readonly ILogger<HomesteadCompanionService> _logger;

    // Resource types available for Aeldran harvesting
    private static readonly ResourceType[] AeldranResources =
    [
        ResourceType.Wood, ResourceType.Stone, ResourceType.Herbs,
        ResourceType.Metal, ResourceType.Sand
    ];

    public HomesteadCompanionService(
        ICompanionRepository companions,
        IHomesteadRepository homesteads,
        IItemRepository items,
        ILogger<HomesteadCompanionService> logger)
    {
        _companions = companions;
        _homesteads = homesteads;
        _items = items;
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
                        HomesteadDuty.Crafter   => ProcessCrafter(companion),
                        _ => null
                    };

                    // Accumulate usage slowly (homestead duty counts at ~30% of combat rate)
                    companion.RecordUsage(3);
                    await _companions.UpdateAsync(companion, ct);

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
        var aptitude = companion.GetAptitude(HomesteadDuty.Harvester);
        var yield = (int)(1 * aptitude * (companion.Level / 5.0 + 1));

        // Pick a random resource type weighted toward Aeldran defaults
        var resourceType = AeldranResources[Random.Shared.Next(AeldranResources.Length)];
        var resourceName = resourceType.ToString(); // "Wood", "Stone", etc.

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

        return $"Your companion {companion.Name} harvested {resourceName} x{yield} (stored in homestead).";
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

        // No salvage XP — companion does the work
        return $"Your companion {companion.Name} salvaged {itemName} {itemWorkStr} → {baseYield.material} x{baseYield.qty} (stored in homestead).";
    }

    private static string? ProcessGuard(Companion companion)
    {
        // Guard duty effect (storage bonus) is computed dynamically via GetGuardBondLevelsAsync.
        // No per-tick broadcast needed — assignment message was sent at assignment time.
        return null;
    }

    private static string? ProcessCrafter(Companion companion)
    {
        _craftLogOnce(companion);
        return null;
    }

    // Log crafter placeholder only occasionally so the log isn't spammed
    private static readonly HashSet<Guid> _craftLoggedIds = [];
    private static void _craftLogOnce(Companion c)
    {
        if (_craftLoggedIds.Add(c.Id))
        {
            // First time — nothing to log externally, just track
        }
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
