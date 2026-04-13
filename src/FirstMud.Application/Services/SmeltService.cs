using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public record SmeltResult(bool Success, string Message, IReadOnlyList<SmeltYield> Yields);
public record SmeltYield(string Name, int Quantity);

/// <summary>
/// Converts legacy "Metal" components into specific ores via a skill-weighted discovery roll.
/// Must be performed at the homestead (Position -100, -100).
/// Higher CraftingSkill shifts the distribution toward rarer results.
/// </summary>
public class SmeltService
{
    private readonly IPlayerRepository _players;
    private readonly IItemRepository _items;
    private readonly IHomesteadRepository _homesteads;
    private readonly ILogger<SmeltService> _logger;

    public SmeltService(
        IPlayerRepository players,
        IItemRepository items,
        IHomesteadRepository homesteads,
        ILogger<SmeltService> logger)
    {
        _players = players;
        _items = items;
        _homesteads = homesteads;
        _logger = logger;
    }

    /// <summary>
    /// Smelt <paramref name="amount"/> units of "Metal" from the player's inventory or storage
    /// into specific ores. Returns a result describing what was discovered.
    /// </summary>
    public async Task<SmeltResult> SmeltAsync(Guid playerId, int amount, CancellationToken ct = default)
    {
        if (amount <= 0)
            return new SmeltResult(false, "Amount must be at least 1.", []);

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            return new SmeltResult(false, "Player not found.", []);

        // Must be at homestead (workbench required)
        if (player.Position.X != -100 || player.Position.Y != -100)
            return new SmeltResult(false, "You need a workbench to smelt. Return to your homestead first.", []);

        // Locate Metal in player inventory
        var allItems = await _items.GetByOwnerAsync(playerId, ct);
        var metalStack = allItems.FirstOrDefault(i =>
            i.Category == ItemCategory.Component &&
            i.Name.Equals("Metal", StringComparison.OrdinalIgnoreCase));

        int available = metalStack?.Quantity ?? 0;

        // Also check homestead storage
        Item? storageMetalStack = null;
        if (available < amount)
        {
            var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
            if (homestead is not null)
            {
                var storageEntries = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
                var storageItemIds = storageEntries.Select(s => s.ItemId).ToList();
                var storageItems = await _items.GetByIdsAsync(storageItemIds, ct);
                storageMetalStack = storageItems.FirstOrDefault(i =>
                    i.Category == ItemCategory.Component &&
                    i.Name.Equals("Metal", StringComparison.OrdinalIgnoreCase));
                available += storageMetalStack?.Quantity ?? 0;
            }
        }

        if (available == 0)
            return new SmeltResult(false, "You have no Metal to smelt. Salvage some weapons or armor first.", []);

        // Clamp to what's actually available
        var toSmelt = Math.Min(amount, available);

        // Consume Metal: drain from inventory first, then storage
        var remaining = toSmelt;
        if (metalStack is not null)
        {
            var fromInv = Math.Min(metalStack.Quantity, remaining);
            if (metalStack.Quantity == fromInv)
                await _items.DeleteAsync(metalStack.Id, ct);
            else
            {
                metalStack.TryRemoveQuantity(fromInv, out _);
                await _items.UpdateAsync(metalStack, ct);
            }
            remaining -= fromInv;
        }

        if (remaining > 0 && storageMetalStack is not null)
        {
            var fromStorage = Math.Min(storageMetalStack.Quantity, remaining);
            if (storageMetalStack.Quantity == fromStorage)
                await _items.DeleteAsync(storageMetalStack.Id, ct);
            else
            {
                storageMetalStack.TryRemoveQuantity(fromStorage, out _);
                await _items.UpdateAsync(storageMetalStack, ct);
            }
        }

        // Roll discovery for each unit smelted
        var tally = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < toSmelt; i++)
        {
            var ore = RollOre(player.CraftingSkill);
            tally.TryGetValue(ore, out var current);
            tally[ore] = current + 1;
        }

        // Deposit results into homestead storage
        var homesteadForStorage = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        foreach (var (oreName, qty) in tally)
        {
            if (homesteadForStorage is not null)
            {
                // Try to merge into existing storage stack
                var storageEntries = await _homesteads.GetStorageItemsAsync(homesteadForStorage.Id, ct);
                var existingIds = storageEntries.Select(s => s.ItemId).ToList();
                var existingItems = await _items.GetByIdsAsync(existingIds, ct);
                var existingStack = existingItems.FirstOrDefault(s =>
                    s.Name.Equals(oreName, StringComparison.OrdinalIgnoreCase) &&
                    s.Category == ItemCategory.Component);

                if (existingStack is not null)
                {
                    existingStack.AddQuantity(qty);
                    await _items.UpdateAsync(existingStack, ct);
                }
                else
                {
                    var newItem = Item.Create(
                        oreName,
                        $"An ore extracted from smelted metal: {oreName.ToLowerInvariant()}.",
                        ItemCategory.Component,
                        Workmanship.Of(1),
                        player.Position.World);
                    newItem.SetOwner(null); // storage item — no owner
                    if (qty > 1) newItem.AddQuantity(qty - 1);
                    await _items.AddAsync(newItem, ct);

                    var storageEntry = HomesteadStorageItem.Create(homesteadForStorage.Id, newItem.Id);
                    await _homesteads.AddStorageItemAsync(storageEntry, ct);
                }
            }
            else
            {
                // Fallback: put directly in inventory
                var existing = await _items.GetByOwnerAndNameAsync(playerId, oreName, ItemCategory.Component, ct);
                if (existing is not null)
                {
                    existing.AddQuantity(qty);
                    await _items.UpdateAsync(existing, ct);
                }
                else
                {
                    var newItem = Item.Create(
                        oreName,
                        $"An ore extracted from smelted metal: {oreName.ToLowerInvariant()}.",
                        ItemCategory.Component,
                        Workmanship.Of(1),
                        player.Position.World);
                    newItem.SetOwner(playerId);
                    if (qty > 1) newItem.AddQuantity(qty - 1);
                    await _items.AddAsync(newItem, ct);
                }
            }
        }

        _logger.LogInformation(
            "Player {PlayerId} smelted {Amount} Metal → {Results}",
            playerId, toSmelt, string.Join(", ", tally.Select(kv => $"{kv.Value}x {kv.Key}")));

        var yields = tally.Select(kv => new SmeltYield(kv.Key, kv.Value)).ToList();
        var summary = string.Join(", ", yields.Select(y => $"{y.Name} x{y.Quantity}"));
        var msg = $"Smelted {toSmelt} Metal → {summary}";

        return new SmeltResult(true, msg, yields);
    }

    // ─── private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// Rolls what ore is discovered from one unit of Metal.
    /// Base table:
    ///   Iron Ore      50%
    ///   Copper Nugget 25%
    ///   Tin           15%
    ///   Silver Ore     8%
    ///   Mithril Ore    2%
    ///
    /// CraftingSkill shifts the distribution:
    ///   Each 5 points of skill moves 2% from the common pool toward rarer results.
    /// </summary>
    private static string RollOre(int craftingSkill)
    {
        // Skill bonus: caps at 10 shifts (skill 50+)
        var shifts = Math.Min(craftingSkill / 5, 10);

        // Base weights (in hundredths)
        var iron   = 50 - shifts * 2;   // 50% → 30% at max skill
        var copper = 25;                 // stays constant — skill shifts to rarer only
        var tin    = 15 + shifts;        // 15% → 25% at max skill
        var silver =  8 + shifts / 2;   //  8% → 13% at max skill
        // mithril gets the rest
        var mithril = 100 - iron - copper - tin - silver; // 2% → 7% at max skill

        var roll = Random.Shared.Next(100);
        if (roll < iron)      return "Iron Ore";
        if (roll < iron + copper) return "Copper Nugget";
        if (roll < iron + copper + tin)    return "Tin";
        if (roll < iron + copper + tin + silver) return "Silver Ore";
        return "Mithril Ore";
    }
}
