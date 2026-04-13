using FirstMud.Application.Models;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace FirstMud.GameServer.Handlers;

/// <summary>
/// Handles the CraftCommand: validate inputs, delegate to CraftingService,
/// persist component consumption on loss outcomes, and broadcast the result.
/// Components may come from inventory (OwnerId set) or homestead storage (OwnerId null).
/// When consuming a storage-sourced component its HomesteadStorageItem join row is also removed.
///
/// When ComponentIds is empty the handler auto-resolves the required items from the player's
/// inventory and homestead storage by ingredient name, supporting stacked items correctly.
/// </summary>
public class CraftCommandHandler(
    CraftingService craftingService,
    IRecipeRepository recipeRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    IHubContext<GameHub> hubContext,
    ILogger<CraftCommandHandler> logger) : ICommandHandler<CraftCommand>
{
    public async Task<CommandResult> HandleAsync(CraftCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.RecipeId))
            return new CommandResult(false, "Recipe ID is required.");

        // When no explicit component IDs are provided, auto-resolve from inventory + storage.
        // This is the primary code path for stackable materials (Leather, Wood, etc.).
        List<Guid> componentIds = cmd.ComponentIds is { Count: > 0 }
            ? cmd.ComponentIds
            : await ResolveComponentIdsAsync(cmd.PlayerId, cmd.RecipeId, ct);

        if (componentIds.Count == 0)
        {
            // Could not satisfy the recipe — send a proper CraftingComplete failure event
            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("CraftingComplete", new
                {
                    Outcome = CraftingOutcome.NearMiss.ToString(),
                    ItemId = (Guid?)null,
                    ItemName = (string?)null,
                    Workmanship = 0,
                    Category = (string?)null,
                    Slot = (string?)null,
                    IsDiscovery = false,
                    Message = "Not enough materials to craft this recipe.",
                }, ct);
            return new CommandResult(false, "Not enough materials to craft this recipe.");
        }

        // Delegate all validation, outcome rolling, and item creation to CraftingService
        var result = await craftingService.AttemptCraftAsync(
            cmd.PlayerId, cmd.RecipeId, componentIds, cmd.TaperId, ct);

        // Consume components on success, unexpected-result, and component-loss outcomes.
        // UnexpectedResult now produces an item (like Success) so components must be consumed.
        bool consumeComponents = result.Outcome is CraftingOutcome.Success
            or CraftingOutcome.Discovery
            or CraftingOutcome.UnexpectedResult
            or CraftingOutcome.ComponentLoss;

        if (consumeComponents)
        {
            // Resolve the player's homestead once (needed to clean up storage join rows)
            var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);

            // Build a map of item ID → units to consume.
            // For stacked items (Components/Reagents) we must consume the ingredient's BaseQuantity
            // from the stack, not just delete the item row. We derive the correct unit count by
            // loading the recipe and pairing each resolved item ID against its ingredient quantity.
            var consumeMap = await BuildConsumeMapAsync(cmd.RecipeId, componentIds, ct);

            // Load distinct items so we don't process the same row twice
            var resolvedItems = await itemRepository.GetByIdsAsync(componentIds.Distinct(), ct);

            foreach (var item in resolvedItems)
            {
                if (!consumeMap.TryGetValue(item.Id, out var unitsToConsume))
                    continue;

                try
                {
                    await ConsumeItemUnitsAsync(item, unitsToConsume, homestead, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to consume component {ItemId} (×{Units}) after crafting", item.Id, unitsToConsume);
                }
            }

            // Also consume the taper if one was used
            if (cmd.TaperId.HasValue)
            {
                try
                {
                    var taperItem = await itemRepository.GetByIdAsync(cmd.TaperId.Value, ct);
                    if (taperItem is not null)
                        await ConsumeItemUnitsAsync(taperItem, 1, homestead, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete taper {TaperId} after crafting", cmd.TaperId);
                }
            }
        }

        // Choose the message category based on outcome so the client styles it correctly.
        // UnexpectedResult → "loot" (yellow bonus), Discovery → "loot-rare" (blue),
        // ComponentLoss → "warning", NearMiss → "warning", Success → "system".
        // Only one message is sent per craft result — via CraftingComplete — so there is no
        // separate SendMessageAsync call here (that was the source of duplicate messages).
        string messageCategory = result.Outcome switch
        {
            CraftingOutcome.Success          => "system",
            CraftingOutcome.UnexpectedResult => "loot",
            CraftingOutcome.Discovery        => "loot-rare",
            CraftingOutcome.ComponentLoss    => "warning",
            _                                => "warning",   // NearMiss
        };

        // Broadcast the single CraftingComplete event — this is the only message sent.
        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("CraftingComplete", new
            {
                Outcome = result.Outcome.ToString(),
                ItemId = result.ProducedItem?.Id,
                ItemName = result.ProducedItem?.Name,
                Workmanship = result.ProducedItem?.Workmanship.Value ?? 0,
                Category = result.ProducedItem?.Category.ToString(),
                Slot = result.ProducedItem?.Slot.ToString(),
                IsDiscovery = result.IsFirstDiscovery,
                Message = result.Message,
                MessageCategory = messageCategory,
            }, ct);

        if (result.ProducedItem is not null)
        {
            logger.LogInformation(
                "Player {PlayerId} crafted {ItemName} (W{Work}) via recipe {RecipeId} — {Outcome}",
                cmd.PlayerId, result.ProducedItem.Name, result.ProducedItem.Workmanship.Value,
                cmd.RecipeId, result.Outcome);
        }
        else
        {
            logger.LogInformation(
                "Player {PlayerId} craft attempt for recipe {RecipeId} — {Outcome}: {Message}",
                cmd.PlayerId, cmd.RecipeId, result.Outcome, result.Message);
        }

        // UnexpectedResult, ComponentLoss, and Discovery are not errors — return Success = true
        // so GameLoopService does not broadcast an additional "error" message on top.
        bool commandSucceeded = result.Outcome is CraftingOutcome.Success
            or CraftingOutcome.Discovery
            or CraftingOutcome.UnexpectedResult
            or CraftingOutcome.ComponentLoss;

        return new CommandResult(
            commandSucceeded,
            result.Message,
            result.ProducedItem is not null ? new { result.ProducedItem.Id, result.ProducedItem.Name } : null);
    }

    /// <summary>
    /// Builds a map of item-ID → units-to-consume for the component list.
    /// When an item ID appears once in componentIds but represents a stacked ingredient
    /// (e.g. one Leather stack covering a need of 3), the map records the ingredient's
    /// BaseQuantity so the stack is reduced by the correct amount.
    /// Falls back to counting ID occurrences for manually-provided (non-stacked) lists.
    /// </summary>
    private async Task<Dictionary<Guid, int>> BuildConsumeMapAsync(
        string recipeId, List<Guid> componentIds, CancellationToken ct)
    {
        var recipe = await recipeRepository.GetByRecipeIdAsync(recipeId, ct);
        if (recipe is null)
        {
            // Fallback: consume 1 unit per occurrence
            return componentIds
                .GroupBy(id => id)
                .ToDictionary(g => g.Key, g => g.Count());
        }

        // Load the items to know their names
        var items = await itemRepository.GetByIdsAsync(componentIds.Distinct(), ct);
        var itemById = items.ToDictionary(i => i.Id);

        var consumeMap = new Dictionary<Guid, int>();

        // Process each ingredient in recipe order, matching against componentIds
        var remaining = new List<Guid>(componentIds);
        foreach (var ingredient in recipe.Ingredients)
        {
            var needed = ingredient.BaseQuantity;

            // Find items in the component list that match this ingredient
            var matchingIds = remaining
                .Where(id => itemById.TryGetValue(id, out var it)
                             && string.Equals(it.Name, ingredient.IngredientName, StringComparison.OrdinalIgnoreCase))
                .Distinct()
                .ToList();

            foreach (var id in matchingIds)
            {
                if (needed <= 0) break;
                var itemQty = itemById.TryGetValue(id, out var it) ? Math.Max(1, it.Quantity) : 1;
                var toConsume = Math.Min(needed, itemQty);
                consumeMap[id] = consumeMap.GetValueOrDefault(id) + toConsume;
                needed -= toConsume;
                remaining.Remove(id);
            }
        }

        // Any leftover IDs (e.g. manually-provided list with extras) — consume 1 each
        foreach (var id in remaining)
            consumeMap[id] = consumeMap.GetValueOrDefault(id) + 1;

        return consumeMap;
    }

    /// <summary>
    /// Auto-resolves component item IDs for a recipe by looking up items in the player's
    /// inventory and homestead storage by ingredient name. For stacked items a single item ID
    /// is returned once — the caller passes the full ID list to CraftingService which reads
    /// the Quantity field. Returns an empty list when materials are insufficient.
    /// </summary>
    private async Task<List<Guid>> ResolveComponentIdsAsync(
        Guid playerId, string recipeId, CancellationToken ct)
    {
        var recipe = await recipeRepository.GetByRecipeIdAsync(recipeId, ct);
        if (recipe is null) return [];

        // Load all inventory items for the player
        var inventoryItems = await itemRepository.GetByOwnerAsync(playerId, ct);

        // Load all homestead storage items
        var homestead = await homesteadRepository.GetByPlayerIdAsync(playerId, ct);
        IReadOnlyList<Item> storageItems = [];
        if (homestead is not null)
        {
            var storageEntries = await homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
            if (storageEntries.Count > 0)
                storageItems = await itemRepository.GetByIdsAsync(storageEntries.Select(s => s.ItemId), ct);
        }

        var resolved = new List<Guid>();

        foreach (var ingredient in recipe.Ingredients)
        {
            var needed = ingredient.BaseQuantity;

            // Prefer inventory items first, then storage items
            foreach (var source in new[] { inventoryItems, storageItems })
            {
                if (needed <= 0) break;

                var matching = source
                    .Where(i => string.Equals(i.Name, ingredient.IngredientName, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.Quantity) // prefer largest stacks first
                    .ToList();

                foreach (var item in matching)
                {
                    if (needed <= 0) break;
                    resolved.Add(item.Id);
                    needed -= Math.Max(1, item.Quantity);
                }
            }

            // If we still need more than 0 units, materials are insufficient
            if (needed > 0) return [];
        }

        return resolved;
    }

    /// <summary>
    /// Consumes <paramref name="units"/> from <paramref name="item"/>.
    /// For stacked items, reduces the Quantity and persists; deletes the item only when the
    /// stack reaches zero. For non-stacked items, always deletes.
    /// Also removes the HomesteadStorageItem join row when the item is fully consumed.
    /// </summary>
    private async Task ConsumeItemUnitsAsync(
        Item item, int units, Homestead? homestead, CancellationToken ct)
    {
        if (item.IsStackable && item.Quantity > units)
        {
            // Reduce the stack — do NOT delete the item
            item.TryRemoveQuantity(units, out _);
            await itemRepository.UpdateAsync(item, ct);
        }
        else
        {
            // Non-stacked or consuming the entire stack — remove join row then delete
            if (item.OwnerId is null && homestead is not null)
                await homesteadRepository.RemoveStorageItemAsync(homestead.Id, item.Id, ct);

            await itemRepository.DeleteAsync(item.Id, ct);
        }
    }
}

/// <summary>
/// Returns all recipes the player can craft (filtered by crafting skill and current world),
/// enriched with per-ingredient counts from both inventory and homestead storage so the
/// client can show availability across both sources without the player needing to withdraw first.
/// </summary>
public class ViewRecipesCommandHandler(
    IRecipeRepository recipeRepository,
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    IHomesteadRepository homesteadRepository,
    IHubContext<GameHub> hubContext) : ICommandHandler<ViewRecipesCommand>
{
    public async Task<CommandResult> HandleAsync(ViewRecipesCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        // Return all recipes the player's crafting skill can attempt
        var recipes = await recipeRepository.GetByCraftingSkillAsync(player.CraftingSkill, ct);

        // Build inventory count map (name → count) from player's carried items
        var inventoryItems = await itemRepository.GetByOwnerAsync(cmd.PlayerId, ct);
        var invCounts = inventoryItems
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(i => i.Quantity > 0 ? i.Quantity : 1),
                StringComparer.OrdinalIgnoreCase);

        // Build storage count map (name → count) from homestead storage
        var storageCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var homestead = await homesteadRepository.GetByPlayerIdAsync(cmd.PlayerId, ct);
        if (homestead is not null)
        {
            var storageEntries = await homesteadRepository.GetStorageItemsAsync(homestead.Id, ct);
            if (storageEntries.Count > 0)
            {
                var storageItemIds = storageEntries.Select(s => s.ItemId).ToList();
                var storageItems = await itemRepository.GetByIdsAsync(storageItemIds, ct);
                storageCounts = storageItems
                    .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Sum(i => i.Quantity > 0 ? i.Quantity : 1),
                        StringComparer.OrdinalIgnoreCase);
            }
        }

        var dtos = recipes.Select(r => new
        {
            r.RecipeId,
            r.Name,
            ResultItemName = r.ResultItemName,
            ResultCategory = r.ResultCategory.ToString(),
            RequiredCraftingSkill = r.RequiredCraftingSkill,
            RequiredWorld = r.RequiredWorld.ToString(),
            RequiredTaperType = r.RequiredTaperType?.ToString(),
            BaseWorkmanshipMin = r.BaseWorkmanshipMin,
            BaseWorkmanshipMax = r.BaseWorkmanshipMax,
            IsDiscoverable = r.IsDiscoverable,
            Ingredients = r.Ingredients.Select(i =>
            {
                invCounts.TryGetValue(i.IngredientName, out var invCount);
                storageCounts.TryGetValue(i.IngredientName, out var storageCount);
                return new
                {
                    i.IngredientName,
                    i.BaseQuantity,
                    Category = i.Category.ToString(),
                    InvCount = invCount,
                    StorageCount = storageCount,
                    TotalCount = invCount + storageCount,
                };
            }).ToList(),
        }).ToList();

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("RecipeList", new { recipes = dtos }, ct);

        return new CommandResult(true, $"{dtos.Count} recipe(s) available.", dtos);
    }
}
