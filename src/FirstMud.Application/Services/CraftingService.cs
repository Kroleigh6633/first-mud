using FirstMud.Application.Models;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Application.Services;

public class CraftingService
{
    private readonly IPlayerRepository _players;
    private readonly IRecipeRepository _recipes;
    private readonly IItemRepository _items;
    private readonly IHomesteadRepository _homesteads;

    // Primes used to diversify per-ingredient seed offset
    private static readonly int[] IngredientPrimes = [17, 31, 47, 61, 79];

    public CraftingService(
        IPlayerRepository players,
        IRecipeRepository recipes,
        IItemRepository items,
        IHomesteadRepository homesteads)
    {
        _players = players;
        _recipes = recipes;
        _items = items;
        _homesteads = homesteads;
    }

    public async Task<CraftingResult> AttemptCraftAsync(
        Guid playerId,
        string recipeId,
        List<Guid> componentItemIds,
        Guid? taperId,
        CancellationToken ct = default)
    {
        // 1. Load player
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            return Fail(CraftingOutcome.NearMiss, "Player not found.");

        // Load recipe
        var recipe = await _recipes.GetByRecipeIdAsync(recipeId, ct);
        if (recipe is null)
            return Fail(CraftingOutcome.NearMiss, "Recipe not found.");

        // Load component items — may come from inventory OR homestead storage
        var componentItems = await _items.GetByIdsAsync(componentItemIds, ct);
        if (componentItems.Count != componentItemIds.Count)
            return Fail(CraftingOutcome.NearMiss, "One or more component items could not be found.");

        // Load optional taper
        Item? taperItem = null;
        if (taperId.HasValue)
        {
            taperItem = await _items.GetByIdAsync(taperId.Value, ct);
            if (taperItem is null)
                return Fail(CraftingOutcome.NearMiss, "Taper item not found.");
        }

        // 2. Verify requirements
        if (player.CraftingSkill < recipe.RequiredCraftingSkill)
            return Fail(CraftingOutcome.NearMiss, $"Insufficient crafting skill. Required: {recipe.RequiredCraftingSkill}, have: {player.CraftingSkill}.");

        if (player.Position.World != recipe.RequiredWorld)
            return Fail(CraftingOutcome.NearMiss, $"You must be in {recipe.RequiredWorld} to craft this recipe.");

        if (recipe.RequiredTaperType.HasValue)
        {
            if (taperItem is null)
                return Fail(CraftingOutcome.NearMiss, $"This recipe requires a {recipe.RequiredTaperType} taper.");
            if (taperItem.AppliedTaper != recipe.RequiredTaperType)
                return Fail(CraftingOutcome.NearMiss, $"Wrong taper type. Required: {recipe.RequiredTaperType}.");
        }

        // 3. Calculate per-player seeded quantities
        var playerSeed = player.CraftingSeed;
        var seededQuantities = CalculateSeededQuantities(recipe, playerSeed);

        // 4. Check provided quantities match seeded requirements (±5% tolerance)
        bool quantitiesMatch = CheckQuantities(recipe, componentItems, seededQuantities);

        // 5. Roll crafting outcome using weighted random seeded by (playerSeed XOR recipeId.GetHashCode())
        var rollSeed = playerSeed ^ recipeId.GetHashCode();
        var rng = new Random(rollSeed);
        var roll = rng.NextDouble() * 100.0;

        CraftingOutcome outcome;
        if (!quantitiesMatch)
        {
            // Quantities off — 70% NearMiss, 20% UnexpectedResult, 8% ComponentLoss, 2% Discovery
            outcome = roll switch
            {
                < 70.0 => CraftingOutcome.NearMiss,
                < 90.0 => CraftingOutcome.UnexpectedResult,
                < 98.0 => CraftingOutcome.ComponentLoss,
                _ => CraftingOutcome.Discovery
            };
        }
        else
        {
            // Quantities match — 85% Success, 20% would be UnexpectedResult so adjusted:
            // 85% Success, 10% UnexpectedResult, 3% ComponentLoss, 2% Discovery
            outcome = roll switch
            {
                < 85.0 => CraftingOutcome.Success,
                < 95.0 => CraftingOutcome.UnexpectedResult,
                < 98.0 => CraftingOutcome.ComponentLoss,
                _ => CraftingOutcome.Discovery
            };
        }

        if (outcome == CraftingOutcome.NearMiss)
            return new CraftingResult(CraftingOutcome.NearMiss, null, "The components didn't quite come together. Check your quantities.", false, null);

        if (outcome == CraftingOutcome.ComponentLoss)
            return new CraftingResult(CraftingOutcome.ComponentLoss, null, "The crafting attempt failed and some components were lost.", false, null);

        if (outcome == CraftingOutcome.UnexpectedResult)
            return new CraftingResult(CraftingOutcome.UnexpectedResult, null, "Something unexpected happened during crafting.", false, null);

        // 6. On success / discovery: calculate final Workmanship
        var workmanship = CalculateWorkmanship(componentItems, player.CraftingSkill, taperItem);

        // 7. Create the item
        bool isDiscovery = outcome == CraftingOutcome.Discovery;
        var itemName = isDiscovery
            ? $"{recipe.ResultItemName} (Variant)"
            : recipe.ResultItemName;

        var item = Item.Create(
            itemName,
            $"Crafted from recipe: {recipe.Name}",
            recipe.ResultCategory,
            workmanship,
            recipe.RequiredWorld);

        // Apply taper if present
        if (taperItem?.AppliedTaper is TaperType taperType &&
            taperItem.TaperQuality is TaperQuality taperQuality &&
            taperItem.MagicalElement is MagicElement element &&
            taperItem.MagicalPolarity is MagicPolarity polarity)
        {
            item.ApplyTaperImbue(taperType, taperQuality, element, polarity);
        }

        // 8. Place crafted item in inventory (if room) or homestead storage (if inventory full)
        var inventoryItems = await _items.GetByOwnerAsync(playerId, ct);
        if (player.CanCarryMore(inventoryItems.Count))
        {
            // Room in inventory — add directly to player's inventory
            item.SetOwner(playerId);
            await _items.AddAsync(item, ct);
        }
        else
        {
            // Inventory full — deposit to homestead storage
            var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
            if (homestead is not null)
            {
                await _items.AddAsync(item, ct);
                var storageEntry = HomesteadStorageItem.Create(homestead.Id, item.Id);
                await _homesteads.AddStorageItemAsync(storageEntry, ct);
            }
            else
            {
                // No homestead — just add to inventory regardless of cap
                item.SetOwner(playerId);
                await _items.AddAsync(item, ct);
            }
        }

        // 9. Discovery check — first discovery flag returned to caller for logging
        string? discoveryRecipeId = isDiscovery ? recipeId : null;
        string message = isDiscovery
            ? $"Extraordinary! You've crafted a variant of {recipe.ResultItemName} — a first discovery!"
            : $"You successfully crafted {item.Name}.";

        if (!player.CanCarryMore(inventoryItems.Count) && await _homesteads.GetByPlayerIdAsync(playerId, ct) is not null)
            message += " (sent to homestead storage — inventory full)";

        return new CraftingResult(
            isDiscovery ? CraftingOutcome.Discovery : CraftingOutcome.Success,
            item,
            message,
            isDiscovery,
            discoveryRecipeId);
    }

    /// <summary>
    /// Returns storage item counts for a player's homestead, keyed by item name (case-insensitive).
    /// Used by ViewRecipesCommandHandler to include cross-source availability in the recipe list.
    /// </summary>
    public async Task<Dictionary<string, int>> GetStorageItemCountsAsync(
        Guid playerId,
        CancellationToken ct = default)
    {
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is null)
            return [];

        var storageEntries = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
        if (storageEntries.Count == 0)
            return [];

        var storageItemIds = storageEntries.Select(s => s.ItemId).ToList();
        var storageItems = await _items.GetByIdsAsync(storageItemIds, ct);

        return storageItems
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(i => i.Quantity > 0 ? i.Quantity : 1),
                StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<int>> GetSeededQuantitiesAsync(
        Guid playerId,
        string recipeId,
        CancellationToken ct = default)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            return Array.Empty<int>();

        var recipe = await _recipes.GetByRecipeIdAsync(recipeId, ct);
        if (recipe is null)
            return Array.Empty<int>();

        var seeded = CalculateSeededQuantities(recipe, player.CraftingSeed);

        // Round to nearest 5 for haziness
        return seeded
            .Select(q => (int)(Math.Round(q / 5.0) * 5))
            .ToList()
            .AsReadOnly();
    }

    // --- private helpers ---

    private static IReadOnlyList<int> CalculateSeededQuantities(Recipe recipe, int playerSeed)
    {
        var result = new List<int>();
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ingredient = recipe.Ingredients[i];
            var prime = IngredientPrimes[i % IngredientPrimes.Length];
            var seededQty = (int)Math.Round(
                ingredient.BaseQuantity * (1.0 + ((playerSeed * prime) % 40 - 20) / 100.0));
            result.Add(Math.Max(1, seededQty));
        }
        return result;
    }

    private static bool CheckQuantities(
        Recipe recipe,
        IReadOnlyList<Item> componentItems,
        IReadOnlyList<int> seededQuantities)
    {
        // Count components by ingredient name
        var providedCounts = componentItems
            .GroupBy(i => i.Name)
            .ToDictionary(g => g.Key, g => g.Count());

        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ingredient = recipe.Ingredients[i];
            var required = seededQuantities[i];
            providedCounts.TryGetValue(ingredient.IngredientName, out var provided);

            // ±5% tolerance
            var tolerance = Math.Max(1, (int)Math.Round(required * 0.05));
            if (Math.Abs(provided - required) > tolerance)
                return false;
        }

        return true;
    }

    private static Workmanship CalculateWorkmanship(
        IReadOnlyList<Item> components,
        int craftingSkill,
        Item? taperItem)
    {
        if (components.Count == 0)
            return Workmanship.Of(1);

        // Average workmanship of all components
        var avgComponentWorkmanship = (int)Math.Round(
            components.Average(c => c.Workmanship.Value));

        // Taper quality bonus
        int taperBonus = taperItem?.TaperQuality switch
        {
            TaperQuality.Pristine => 2,
            TaperQuality.Flawed => 0,
            TaperQuality.Spent => -1,
            _ => 0
        };

        var skillBonus = craftingSkill / 20;
        var raw = Math.Clamp(avgComponentWorkmanship + skillBonus + taperBonus, 1, 10);
        return Workmanship.Of(raw);
    }

    private static CraftingResult Fail(CraftingOutcome outcome, string message) =>
        new(outcome, null, message, false, null);
}
