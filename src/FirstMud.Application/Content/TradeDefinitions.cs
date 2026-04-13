using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Per-item base-value lookup for trade pricing. Loaded from
/// <c>content/item-values.json</c>. Missing items fall back to the
/// category default times the workmanship multiplier.
/// </summary>
public sealed record ItemValuesDefinition(
    IReadOnlyDictionary<ItemCategory, int> DefaultsByCategory,
    double WorkmanshipMultiplier,
    IReadOnlyDictionary<string, int> BaseValuesByName);

/// <summary>
/// Trade coefficient table loaded from <c>content/trade-curves.json</c>.
/// Buy/sell multipliers control vendor spread; the gold block tunes
/// quest rewards and loot drops.
/// </summary>
public sealed record TradeCurvesDefinition(
    double BuyMultiplier,
    double SellMultiplier,
    int MinBuyPrice,
    int MinSellPrice,
    GoldCurveDefinition Gold);

public sealed record GoldCurveDefinition(
    double QuestRewardBase,
    double QuestRewardPerReputationPoint,
    int LootDropChancePercent,
    int LootDropBase,
    int LootDropPerDangerLevel);

/// <summary>
/// Vendor definition loaded from <c>content/vendors.json</c>. Each vendor
/// is bound to an NPC with role=shopkeeper; the vendor's zone is derived
/// from the NPC's homeZoneId and resolved to a numeric ZoneNumber at load
/// time so TradeService can match it to the player's Position.ZoneId.
/// </summary>
public sealed record VendorDefinition(
    string NpcId,
    string DisplayName,
    WorldId World,
    int ZoneNumber,
    IReadOnlyList<ItemCategory> BuysCategories,
    IReadOnlyList<VendorStockEntry> Stock);

public sealed record VendorStockEntry(
    string ItemName,
    ItemCategory Category,
    int Quantity,
    int Workmanship);
