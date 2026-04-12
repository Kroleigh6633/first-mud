using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public record SalvageResult(bool Success, string Message, IReadOnlyList<SalvageYield> Yields);
public record SalvageYield(string Name, int Quantity);

public class SalvageService
{
    private readonly IPlayerRepository _players;
    private readonly IItemRepository _items;
    private readonly ILogger<SalvageService> _logger;

    // Component names that match ResourceType enum values and standard salvage materials
    private static readonly string[] WeaponMetalComponents = ["Metal"];
    private static readonly string[] WeaponWoodComponents  = ["Wood"];
    private static readonly string[] ArmorLeatherComponents = ["Leather"];
    private static readonly string[] ArmorMetalComponents  = ["Metal"];
    private static readonly string[] ConsumableComponents  = ["Stone", "Wood", "Metal", "Leather", "Sand", "Herbs"];

    public SalvageService(
        IPlayerRepository players,
        IItemRepository items,
        ILogger<SalvageService> logger)
    {
        _players = players;
        _items = items;
        _logger = logger;
    }

    /// <summary>
    /// Salvages a single item owned by <paramref name="playerId"/> into component materials.
    /// Deletes the original item and creates (or merges into existing) component stacks.
    /// Returns the salvage result with the list of yielded materials.
    /// </summary>
    public async Task<SalvageResult> SalvageAsync(Guid playerId, Guid itemId, CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            return new SalvageResult(false, "Player not found.", []);

        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.OwnerId != playerId)
            return new SalvageResult(false, "Item not found in your inventory.", []);

        if (item.Category == ItemCategory.Component)
            return new SalvageResult(false, $"{item.Name} is already a component and cannot be salvaged.", []);

        if (item.Category == ItemCategory.Reagent)
            return new SalvageResult(false, $"{item.Name} is a reagent and cannot be salvaged.", []);

        if (!item.IsSalvageable)
            return new SalvageResult(false, $"{item.Name} is broken and cannot be salvaged.", []);

        // Success chance: base 70% + 2% per SalvageSkill point (capped at 95%)
        var successChance = Math.Min(95, 70 + player.SalvageSkill * 2);
        if (Random.Shared.Next(100) >= successChance)
        {
            _logger.LogInformation("Salvage failed for player {PlayerId}, item {ItemName}", playerId, item.Name);
            await _items.DeleteAsync(itemId, ct);
            player.GainSalvageSkillXp(1);
            await _players.UpdateAsync(player, ct);
            return new SalvageResult(false, $"You attempted to salvage {item.Name} but failed. The item was lost.", []);
        }

        // Workmanship scales yield: W1–2 → low, W5–6 → mid, W9–10 → high
        var workValue = item.Workmanship.Value;
        var yieldBonus = workValue / 3; // 0–3 bonus units

        // SalvageSkill scales yield: every 10 skill points → +1 extra unit
        var skillBonus = player.SalvageSkill / 10;

        var yields = BuildYields(item.Category, yieldBonus + skillBonus);

        // Delete original item first
        await _items.DeleteAsync(itemId, ct);

        // Create or merge yielded component items
        foreach (var yield in yields)
        {
            var existing = await _items.GetByOwnerAndNameAsync(playerId, yield.Name, ItemCategory.Component, ct);
            if (existing is not null)
            {
                existing.AddQuantity(yield.Quantity);
                await _items.UpdateAsync(existing, ct);
            }
            else
            {
                var newItem = Item.Create(
                    yield.Name,
                    $"A salvaged material: {yield.Name.ToLowerInvariant()}.",
                    ItemCategory.Component,
                    Workmanship.Of(1),
                    item.OriginWorld);
                newItem.SetOwner(playerId);
                newItem.AddQuantity(yield.Quantity - 1); // Create starts at Quantity=1
                await _items.AddAsync(newItem, ct);
            }
        }

        // Award salvage XP
        player.GainSalvageSkillXp(1);
        await _players.UpdateAsync(player, ct);

        _logger.LogInformation(
            "Player {PlayerId} salvaged {ItemName} yielding {YieldCount} material type(s)",
            playerId, item.Name, yields.Count);

        return new SalvageResult(true, BuildSuccessMessage(item.Name, yields), yields);
    }

    /// <summary>
    /// Salvages ALL items of a given category owned by the player.
    /// Returns a summary result.
    /// </summary>
    public async Task<SalvageResult> SalvageAllAsync(Guid playerId, string category, CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            return new SalvageResult(false, "Player not found.", []);

        if (!Enum.TryParse<ItemCategory>(category, ignoreCase: true, out var parsedCategory))
            return new SalvageResult(false, $"Unknown category: {category}.", []);

        if (parsedCategory == ItemCategory.Component || parsedCategory == ItemCategory.Reagent)
            return new SalvageResult(false, $"Cannot bulk-salvage {category} items.", []);

        var allItems = await _items.GetByOwnerAsync(playerId, ct);
        var targets = allItems
            .Where(i => i.Category == parsedCategory && i.IsSalvageable)
            .ToList();

        if (targets.Count == 0)
            return new SalvageResult(false, $"You have no salvageable {category} items.", []);

        var totalYields = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var successCount = 0;

        foreach (var item in targets)
        {
            var result = await SalvageAsync(playerId, item.Id, ct);
            if (!result.Success) continue;

            successCount++;
            foreach (var y in result.Yields)
            {
                totalYields.TryGetValue(y.Name, out var current);
                totalYields[y.Name] = current + y.Quantity;
            }
        }

        if (successCount == 0)
            return new SalvageResult(false, $"All {targets.Count} salvage attempt(s) failed.", []);

        var combinedYields = totalYields
            .Select(kv => new SalvageYield(kv.Key, kv.Value))
            .ToList();

        var message = $"Salvaged {successCount}/{targets.Count} {category} item(s). "
                    + BuildYieldSummary(combinedYields);

        return new SalvageResult(true, message, combinedYields);
    }

    // ─── private helpers ────────────────────────────────────────────────────

    private static IReadOnlyList<SalvageYield> BuildYields(ItemCategory category, int bonus)
    {
        return category switch
        {
            ItemCategory.Weapon => BuildWeaponYields(bonus),
            ItemCategory.Armor  => BuildArmorYields(bonus),
            ItemCategory.Consumable => BuildConsumableYields(),
            _ => [new SalvageYield("Metal", 1)]
        };
    }

    private static IReadOnlyList<SalvageYield> BuildWeaponYields(int bonus)
    {
        var metal  = Math.Clamp(2 + bonus, 2, 5);
        var wood   = Math.Clamp(1 + bonus / 2, 1, 3);
        return [new SalvageYield("Metal", metal), new SalvageYield("Wood", wood)];
    }

    private static IReadOnlyList<SalvageYield> BuildArmorYields(int bonus)
    {
        var leather = Math.Clamp(2 + bonus, 2, 5);
        var metal   = Math.Clamp(1 + bonus / 2, 1, 3);
        return [new SalvageYield("Leather", leather), new SalvageYield("Metal", metal)];
    }

    private static IReadOnlyList<SalvageYield> BuildConsumableYields()
    {
        var pick = ConsumableComponents[Random.Shared.Next(ConsumableComponents.Length)];
        return [new SalvageYield(pick, 1)];
    }

    private static string BuildSuccessMessage(string itemName, IReadOnlyList<SalvageYield> yields)
    {
        var summary = BuildYieldSummary(yields);
        return $"Salvaged {itemName} into: {summary}";
    }

    private static string BuildYieldSummary(IReadOnlyList<SalvageYield> yields) =>
        string.Join(", ", yields.Select(y => $"{y.Name} x{y.Quantity}"));
}
