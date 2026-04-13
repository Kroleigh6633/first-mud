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
    private static readonly string[] WeaponMetalComponents = ["Iron Ore"];
    private static readonly string[] WeaponWoodComponents  = ["Wood"];
    private static readonly string[] ArmorLeatherComponents = ["Leather"];
    private static readonly string[] ArmorMetalComponents  = ["Iron Ore"];
    private static readonly string[] ConsumableComponents  = ["Stone", "Wood", "Iron Ore", "Leather", "Sand", "Herbs"];

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
            return new SalvageResult(false, $"{item.DisplayName} is already a component and cannot be salvaged.", []);

        if (item.Category == ItemCategory.Reagent)
            return new SalvageResult(false, $"{item.DisplayName} is a reagent and cannot be salvaged.", []);

        if (item.IsLocked)
            return new SalvageResult(false, $"{item.DisplayName} is locked. Unlock it first (★) to salvage.", []);

        if (player.IsItemEquipped(itemId))
            return new SalvageResult(false, $"{item.DisplayName} is currently equipped. Unequip it before salvaging.", []);

        if (!item.IsSalvageable)
            return new SalvageResult(false, $"{item.DisplayName} is broken and cannot be salvaged.", []);

        // Skill gate: check SalvageSkill against item Workmanship
        var skillCheckResult = CheckSalvageSkill(player.SalvageSkill, item);
        if (!skillCheckResult.Allowed)
            return new SalvageResult(false, skillCheckResult.Message, []);

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

        // Recover imbue taper materials before deleting
        var recoveredTapers = await RecoverImbueResiduesAsync(player, item, ct);

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

        var successMsg = BuildSuccessMessage(item.Name, yields);
        if (recoveredTapers.Count > 0)
            successMsg += $" Recovered imbue residue: {string.Join(", ", recoveredTapers)}.";

        return new SalvageResult(true, successMsg, yields);
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
            .Where(i => i.Category == parsedCategory && i.IsSalvageable && !i.IsLocked && !player.IsItemEquipped(i.Id))
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

    /// <summary>
    /// Checks whether the item should be auto-salvaged based on the player's thresholds.
    /// If so, salvages it immediately (without adding to inventory) and returns a summary message.
    /// Returns null if the item should NOT be auto-salvaged.
    /// </summary>
    public async Task<string?> TryAutoSalvageAsync(
        Player player,
        Item item,
        CancellationToken ct = default)
    {
        // Only weapons and armor are subject to auto-salvage; locked items are always skipped
        if (item.Category != ItemCategory.Weapon && item.Category != ItemCategory.Armor)
            return null;

        if (item.IsLocked)
            return null;

        // Never auto-salvage an item the player has equipped
        if (player.IsItemEquipped(item.Id))
            return null;

        // Auto-equip has priority: never salvage an item that could fill an empty slot.
        // This prevents Feet/Hands/Legs/Focus/Accessory items from being salvaged before
        // the caller has a chance to equip them.
        if (item.Slot != Domain.Enums.EquipmentSlot.None && player.GetEquipped(item.Slot) is null)
            return null;

        var threshold = item.Category == ItemCategory.Weapon
            ? player.AutoSalvageWeaponThreshold
            : player.AutoSalvageArmorThreshold;

        if (threshold == 0 || item.Workmanship.Value > threshold)
            return null;

        // Skill gate still applies for auto-salvage
        var skillCheck = CheckSalvageSkill(player.SalvageSkill, item);
        if (!skillCheck.Allowed)
            return null; // Too low to salvage — just drop it normally

        // Perform the salvage (item is not yet in inventory)
        var workValue = item.Workmanship.Value;
        var yieldBonus = workValue / 3;
        var skillBonus  = player.SalvageSkill / 10;
        var yields = BuildYields(item.Category, yieldBonus + skillBonus);

        foreach (var yield in yields)
        {
            var existing = await _items.GetByOwnerAndNameAsync(player.Id, yield.Name, ItemCategory.Component, ct);
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
                newItem.SetOwner(player.Id);
                newItem.AddQuantity(yield.Quantity - 1);
                await _items.AddAsync(newItem, ct);
            }
        }

        player.GainSalvageSkillXp(1);
        await _players.UpdateAsync(player, ct);

        var yieldSummary = BuildYieldSummary(yields);
        return $"Auto-salvaged {item.Name} (W{workValue}) → {yieldSummary}";
    }

    // ─── private helpers ────────────────────────────────────────────────────

    /// <summary>
    /// For each imbue on the item, rolls a skill-based chance to recover the matching taper material.
    /// Returns the names of any recovered tapers (for message building).
    /// </summary>
    private async Task<List<string>> RecoverImbueResiduesAsync(
        Player player,
        Item item,
        CancellationToken ct)
    {
        var recovered = new List<string>();
        if (item.Imbues.Count == 0)
            return recovered;

        // Recovery chance: SalvageSkill * 3 + 25 (capped at 80%)
        var baseChance = Math.Min(80, player.SalvageSkill * 3 + 25);

        foreach (var imbue in item.Imbues)
        {
            if (Random.Shared.Next(100) >= baseChance)
                continue;

            var taperName = ImbueTypeToTaperName(imbue.Type);
            if (taperName is null)
                continue;

            var existing = await _items.GetByOwnerAndNameAsync(player.Id, taperName, ItemCategory.Reagent, ct);
            if (existing is not null)
            {
                existing.AddQuantity(1);
                await _items.UpdateAsync(existing, ct);
            }
            else
            {
                var taperItem = Item.Create(
                    taperName,
                    $"A residue recovered from salvaging an imbued item.",
                    ItemCategory.Reagent,
                    Workmanship.Of(1),
                    item.OriginWorld);
                taperItem.SetOwner(player.Id);
                await _items.AddAsync(taperItem, ct);
            }

            recovered.Add(taperName);
        }

        return recovered;
    }

    private static string? ImbueTypeToTaperName(Domain.Enums.ImbueType type) => type switch
    {
        Domain.Enums.ImbueType.Fire        => "Fire Shaping Taper",
        Domain.Enums.ImbueType.Water       => "Water Shaping Taper",
        Domain.Enums.ImbueType.Earth       => "Earth Shaping Taper",
        Domain.Enums.ImbueType.Air         => "Air Shaping Taper",
        Domain.Enums.ImbueType.Fortifying  => "Fortitude Taper",
        Domain.Enums.ImbueType.Protective  => "Warding Taper",
        Domain.Enums.ImbueType.Wyrd        => "Wyrd Shard",
        Domain.Enums.ImbueType.Restoration => "Dravenite Dust",
        _                                  => null
    };

    private static IReadOnlyList<SalvageYield> BuildYields(ItemCategory category, int bonus)
    {
        return category switch
        {
            ItemCategory.Weapon => BuildWeaponYields(bonus),
            ItemCategory.Armor  => BuildArmorYields(bonus),
            ItemCategory.Consumable => BuildConsumableYields(),
            _ => [new SalvageYield("Iron Ore", 1)]
        };
    }

    private static IReadOnlyList<SalvageYield> BuildWeaponYields(int bonus)
    {
        var metal  = Math.Clamp(2 + bonus, 2, 5);
        var wood   = Math.Clamp(1 + bonus / 2, 1, 3);
        return [new SalvageYield("Iron Ore", metal), new SalvageYield("Wood", wood)];
    }

    private static IReadOnlyList<SalvageYield> BuildArmorYields(int bonus)
    {
        var leather = Math.Clamp(2 + bonus, 2, 5);
        var metal   = Math.Clamp(1 + bonus / 2, 1, 3);
        return [new SalvageYield("Leather", leather), new SalvageYield("Iron Ore", metal)];
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

    /// <summary>
    /// Returns the maximum Workmanship the player's SalvageSkill allows them to salvage.
    /// Skill  1-5  → W3
    /// Skill  6-10 → W5
    /// Skill 11-20 → W7
    /// Skill 21+   → W10
    /// </summary>
    public static int MaxWorkmanshipForSkill(int skill) => skill switch
    {
        <= 5  => 3,
        <= 10 => 5,
        <= 20 => 7,
        _     => 10
    };

    /// <summary>
    /// Returns the minimum SalvageSkill needed to salvage a given Workmanship level.
    /// </summary>
    private static int RequiredSkillForWorkmanship(int workmanship) => workmanship switch
    {
        <= 3  => 1,
        <= 5  => 6,
        <= 7  => 11,
        _     => 21
    };

    private static readonly string[] HighTierMaterials = ["Mithril", "Dravenite", "Wyrd"];

    private static SkillCheckResult CheckSalvageSkill(int skill, Item item)
    {
        var workmanship = item.Workmanship.Value;
        var maxAllowed  = MaxWorkmanshipForSkill(skill);

        if (workmanship > maxAllowed)
        {
            var needed = RequiredSkillForWorkmanship(workmanship);
            return new SkillCheckResult(false,
                $"Your salvage skill ({skill}) is too low to salvage this {item.Name} (W{workmanship}). Need skill {needed}.");
        }

        // High-tier material names require Skill 15+
        foreach (var material in HighTierMaterials)
        {
            if (item.Name.Contains(material, StringComparison.OrdinalIgnoreCase) && skill < 15)
                return new SkillCheckResult(false,
                    $"Your salvage skill ({skill}) is too low to salvage {item.Name}. Need skill 15 for {material} materials.");
        }

        return new SkillCheckResult(true, string.Empty);
    }

    private readonly record struct SkillCheckResult(bool Allowed, string Message);
}
