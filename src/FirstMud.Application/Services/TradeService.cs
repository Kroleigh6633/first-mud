using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public sealed record TradeVendorStockLine(
    string ItemName,
    ItemCategory Category,
    int Quantity,
    int Workmanship,
    int BuyPrice,
    int SellPrice);

public sealed record TradeVendorSnapshot(
    string NpcId,
    string DisplayName,
    int ZoneNumber,
    WorldId World,
    IReadOnlyList<TradeVendorStockLine> Stock,
    IReadOnlyList<ItemCategory> BuysCategories);

public sealed record TradeResult(
    bool Success,
    string Message,
    int? GoldBefore = null,
    int? GoldAfter = null,
    int? Price = null,
    int? QuantityTraded = null);

/// <summary>
/// Stage-1 trade service. Owns vendor inventory queries and buy/sell
/// transactions. Pricing is a simple <c>baseValue * multiplier</c> spread
/// with a workmanship bump; stock for now is in-memory and resets on
/// server restart (dynamic/persistent stock is stage 2).
/// </summary>
/// <summary>
/// Singleton holder for live vendor stock so stock survives across scoped
/// <see cref="TradeService"/> instances. Stage-1 stock resets on restart.
/// </summary>
public sealed class TradeStockStore
{
    internal readonly Dictionary<string, Dictionary<(string Name, ItemCategory Cat), int>> LiveStock =
        new(StringComparer.Ordinal);
    internal readonly object Lock = new();
}

public sealed class TradeService
{
    private readonly IContentProvider _content;
    private readonly IPlayerRepository _players;
    private readonly IItemRepository _items;
    private readonly TradeStockStore _store;
    private readonly ILogger<TradeService>? _logger;

    public TradeService(
        IContentProvider content,
        IPlayerRepository players,
        IItemRepository items,
        TradeStockStore store,
        ILogger<TradeService>? logger = null)
    {
        _content = content;
        _players = players;
        _items = items;
        _store = store;
        _logger = logger;
    }

    // ─── Pricing ──────────────────────────────────────────────────────────

    /// <summary>
    /// Computes the vendor buy-from-player price (what the player receives).
    /// </summary>
    public int ComputeSellPrice(string itemName, ItemCategory category, int workmanship)
    {
        var baseValue = ResolveBaseValue(itemName, category, workmanship);
        var curves = _content.TradeCurves;
        var price = (int)Math.Floor(baseValue * curves.SellMultiplier);
        return Math.Max(curves.MinSellPrice, price);
    }

    /// <summary>
    /// Computes the vendor sell-to-player price (what the player pays).
    /// </summary>
    public int ComputeBuyPrice(string itemName, ItemCategory category, int workmanship)
    {
        var baseValue = ResolveBaseValue(itemName, category, workmanship);
        var curves = _content.TradeCurves;
        var price = (int)Math.Ceiling(baseValue * curves.BuyMultiplier);
        return Math.Max(curves.MinBuyPrice, price);
    }

    private int ResolveBaseValue(string itemName, ItemCategory category, int workmanship)
    {
        var values = _content.ItemValues;
        int baseValue;
        if (values.BaseValuesByName.TryGetValue(itemName, out var explicitValue))
            baseValue = explicitValue;
        else if (values.DefaultsByCategory.TryGetValue(category, out var catValue))
            baseValue = catValue;
        else
            baseValue = 5;

        // Workmanship bumps value linearly from W1 baseline.
        var bump = (workmanship - 1) * values.WorkmanshipMultiplier * baseValue;
        return Math.Max(0, baseValue + (int)Math.Floor(bump));
    }

    // ─── Vendor inventory ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the live vendor inventory snapshot (remaining stock + current
    /// prices) for the vendor whose shopkeeper NPC id is <paramref name="npcId"/>.
    /// Returns null if the vendor does not exist.
    /// </summary>
    public Task<TradeVendorSnapshot?> GetVendorInventoryAsync(string npcId, CancellationToken ct = default)
    {
        var vendor = _content.GetVendor(npcId);
        if (vendor is null) return Task.FromResult<TradeVendorSnapshot?>(null);

        var live = GetOrInitStock(vendor);
        var lines = new List<TradeVendorStockLine>(vendor.Stock.Count);
        foreach (var entry in vendor.Stock)
        {
            var qty = live.TryGetValue((entry.ItemName, entry.Category), out var q) ? q : 0;
            lines.Add(new TradeVendorStockLine(
                entry.ItemName,
                entry.Category,
                qty,
                entry.Workmanship,
                ComputeBuyPrice(entry.ItemName, entry.Category, entry.Workmanship),
                ComputeSellPrice(entry.ItemName, entry.Category, entry.Workmanship)));
        }

        return Task.FromResult<TradeVendorSnapshot?>(new TradeVendorSnapshot(
            vendor.NpcId, vendor.DisplayName, vendor.ZoneNumber, vendor.World, lines, vendor.BuysCategories));
    }

    // ─── Buy ──────────────────────────────────────────────────────────────

    public async Task<TradeResult> BuyItemAsync(
        Guid playerId, string npcId, string itemName, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) return new TradeResult(false, "Quantity must be positive.");

        var vendor = _content.GetVendor(npcId);
        if (vendor is null) return new TradeResult(false, $"No vendor '{npcId}' exists.");

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new TradeResult(false, "Player not found.");

        if (!IsPlayerAtVendor(player, vendor))
            return new TradeResult(false, $"You must be in {vendor.DisplayName}'s zone to trade.");

        var entry = FindStockEntry(vendor, itemName);
        if (entry is null)
            return new TradeResult(false, $"{vendor.DisplayName} does not stock '{itemName}'.");

        var live = GetOrInitStock(vendor);
        int remaining;
        lock (_store.Lock)
        {
            live.TryGetValue((entry.ItemName, entry.Category), out remaining);
        }
        if (remaining < quantity)
            return new TradeResult(false, $"{vendor.DisplayName} only has {remaining} of {entry.ItemName}.");

        var unitPrice = ComputeBuyPrice(entry.ItemName, entry.Category, entry.Workmanship);
        var total = unitPrice * quantity;
        var goldBefore = player.Gold;
        if (!player.TrySpendGold(total))
            return new TradeResult(false, $"You need {total} gold (you have {player.Gold}).");

        // Create / merge items
        var workmanship = Workmanship.Of(entry.Workmanship);
        var originWorld = vendor.World;
        var item = Item.Create(entry.ItemName, DescribeItem(entry.ItemName), entry.Category, workmanship, originWorld, slot: EquipmentSlot.None);
        item.SetOwner(playerId);
        if (item.IsStackable)
        {
            var existing = await _items.GetByOwnerAndNameAsync(playerId, entry.ItemName, entry.Category, ct);
            if (existing is not null)
            {
                existing.AddQuantity(quantity);
                await _items.UpdateAsync(existing, ct);
            }
            else
            {
                item.AddQuantity(quantity - 1);
                await _items.AddAsync(item, ct);
            }
        }
        else
        {
            // Non-stackable: create one item per quantity.
            await _items.AddAsync(item, ct);
            for (var i = 1; i < quantity; i++)
            {
                var extra = Item.Create(entry.ItemName, DescribeItem(entry.ItemName), entry.Category, workmanship, originWorld, slot: EquipmentSlot.None);
                extra.SetOwner(playerId);
                await _items.AddAsync(extra, ct);
            }
        }

        // Debit live stock + persist player gold
        lock (_store.Lock)
        {
            live[(entry.ItemName, entry.Category)] = remaining - quantity;
        }
        await _players.UpdateAsync(player, ct);

        _logger?.LogInformation(
            "Trade buy: player {PlayerId} bought {Qty}x {Item} from {Vendor} for {Total}g",
            playerId, quantity, entry.ItemName, vendor.NpcId, total);

        return new TradeResult(
            true,
            $"Bought {quantity}x {entry.ItemName} from {vendor.DisplayName} for {total} gold.",
            goldBefore, player.Gold, total, quantity);
    }

    // ─── Sell ─────────────────────────────────────────────────────────────

    public async Task<TradeResult> SellItemAsync(
        Guid playerId, string npcId, string itemName, int quantity, CancellationToken ct = default)
    {
        if (quantity <= 0) return new TradeResult(false, "Quantity must be positive.");

        var vendor = _content.GetVendor(npcId);
        if (vendor is null) return new TradeResult(false, $"No vendor '{npcId}' exists.");

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return new TradeResult(false, "Player not found.");

        if (!IsPlayerAtVendor(player, vendor))
            return new TradeResult(false, $"You must be in {vendor.DisplayName}'s zone to trade.");

        // Find matching items owned by player.
        var owned = await _items.GetByOwnerAsync(playerId, ct);
        var matches = owned
            .Where(i => i.IsSalvageable && !i.IsLocked &&
                        string.Equals(i.Name, itemName, StringComparison.OrdinalIgnoreCase) &&
                        !player.IsItemEquipped(i.Id))
            .ToList();

        if (matches.Count == 0)
            return new TradeResult(false, $"You have no tradeable '{itemName}'.");

        var first = matches[0];
        if (vendor.BuysCategories.Count > 0 && !vendor.BuysCategories.Contains(first.Category))
            return new TradeResult(false, $"{vendor.DisplayName} does not buy {first.Category} goods.");

        // Total available across matching items (respecting stacks).
        var available = matches.Sum(i => i.IsStackable ? i.Quantity : 1);
        if (available < quantity)
            return new TradeResult(false, $"You only have {available}x {itemName}.");

        // Price is computed per item using the FIRST match's workmanship (all should stack-share).
        var unitPrice = ComputeSellPrice(first.Name, first.Category, first.Workmanship.Value);
        var total = unitPrice * quantity;

        // Remove quantity across matches.
        var remaining = quantity;
        foreach (var item in matches)
        {
            if (remaining <= 0) break;
            if (item.IsStackable)
            {
                var take = Math.Min(remaining, item.Quantity);
                if (item.Quantity == take)
                {
                    await _items.DeleteAsync(item.Id, ct);
                }
                else
                {
                    item.TryRemoveQuantity(take, out _);
                    await _items.UpdateAsync(item, ct);
                }
                remaining -= take;
            }
            else
            {
                await _items.DeleteAsync(item.Id, ct);
                remaining -= 1;
            }
        }

        var goldBefore = player.Gold;
        player.AddGold(total);
        await _players.UpdateAsync(player, ct);

        _logger?.LogInformation(
            "Trade sell: player {PlayerId} sold {Qty}x {Item} to {Vendor} for {Total}g",
            playerId, quantity, itemName, vendor.NpcId, total);

        return new TradeResult(
            true,
            $"Sold {quantity}x {itemName} to {vendor.DisplayName} for {total} gold.",
            goldBefore, player.Gold, total, quantity);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static bool IsPlayerAtVendor(Player player, VendorDefinition vendor) =>
        player.Position.World == vendor.World && player.Position.ZoneId == vendor.ZoneNumber;

    private static VendorStockEntry? FindStockEntry(VendorDefinition vendor, string itemName) =>
        vendor.Stock.FirstOrDefault(s =>
            string.Equals(s.ItemName, itemName, StringComparison.OrdinalIgnoreCase));

    private Dictionary<(string, ItemCategory), int> GetOrInitStock(VendorDefinition vendor)
    {
        lock (_store.Lock)
        {
            if (!_store.LiveStock.TryGetValue(vendor.NpcId, out var dict))
            {
                dict = new Dictionary<(string, ItemCategory), int>();
                foreach (var entry in vendor.Stock)
                    dict[(entry.ItemName, entry.Category)] = entry.Quantity;
                _store.LiveStock[vendor.NpcId] = dict;
            }
            return dict;
        }
    }

    private static string DescribeItem(string itemName) => $"Purchased {itemName}.";
}
