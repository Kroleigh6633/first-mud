using FirstMud.Application.Models;
using FirstMud.Application.Services;
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
/// </summary>
public class CraftCommandHandler(
    CraftingService craftingService,
    IItemRepository itemRepository,
    GameNotificationService notificationService,
    IHubContext<GameHub> hubContext,
    ILogger<CraftCommandHandler> logger) : ICommandHandler<CraftCommand>
{
    public async Task<CommandResult> HandleAsync(CraftCommand cmd, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.RecipeId))
            return new CommandResult(false, "Recipe ID is required.");

        if (cmd.ComponentIds is null || cmd.ComponentIds.Count == 0)
            return new CommandResult(false, "No component items provided.");

        // Delegate all validation, outcome rolling, and item creation to CraftingService
        var result = await craftingService.AttemptCraftAsync(
            cmd.PlayerId, cmd.RecipeId, cmd.ComponentIds, cmd.TaperId, ct);

        // Consume components on success or component-loss outcomes
        bool consumeComponents = result.Outcome is CraftingOutcome.Success
            or CraftingOutcome.Discovery
            or CraftingOutcome.ComponentLoss;

        if (consumeComponents)
        {
            foreach (var componentId in cmd.ComponentIds)
            {
                try
                {
                    var item = await itemRepository.GetByIdAsync(componentId, ct);
                    if (item is not null)
                        await itemRepository.DeleteAsync(item.Id, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete component {ItemId} after crafting", componentId);
                }
            }

            // Also consume the taper if one was used
            if (cmd.TaperId.HasValue)
            {
                try
                {
                    var taperItem = await itemRepository.GetByIdAsync(cmd.TaperId.Value, ct);
                    if (taperItem is not null)
                        await itemRepository.DeleteAsync(taperItem.Id, ct);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to delete taper {TaperId} after crafting", cmd.TaperId);
                }
            }
        }

        // Notify the player of the outcome
        await notificationService.SendMessageAsync(cmd.PlayerId, "system", result.Message, ct);

        // If an item was produced, notify the client so the inventory updates
        if (result.ProducedItem is not null)
        {
            result.ProducedItem.SetOwner(cmd.PlayerId);
            // Item was already persisted by CraftingService; broadcast to client
            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("CraftingComplete", new
                {
                    Outcome = result.Outcome.ToString(),
                    ItemId = result.ProducedItem.Id,
                    ItemName = result.ProducedItem.Name,
                    Workmanship = result.ProducedItem.Workmanship.Value,
                    Category = result.ProducedItem.Category.ToString(),
                    Slot = result.ProducedItem.Slot.ToString(),
                    IsDiscovery = result.IsFirstDiscovery,
                    Message = result.Message,
                }, ct);

            logger.LogInformation(
                "Player {PlayerId} crafted {ItemName} (W{Work}) via recipe {RecipeId} — {Outcome}",
                cmd.PlayerId, result.ProducedItem.Name, result.ProducedItem.Workmanship.Value,
                cmd.RecipeId, result.Outcome);
        }
        else
        {
            // No item produced — still tell the client the outcome so the UI resets
            await hubContext.Clients
                .Group(cmd.PlayerId.ToString())
                .SendAsync("CraftingComplete", new
                {
                    Outcome = result.Outcome.ToString(),
                    ItemId = (Guid?)null,
                    ItemName = (string?)null,
                    Workmanship = 0,
                    Category = (string?)null,
                    Slot = (string?)null,
                    IsDiscovery = false,
                    Message = result.Message,
                }, ct);

            logger.LogInformation(
                "Player {PlayerId} craft attempt for recipe {RecipeId} — {Outcome}: {Message}",
                cmd.PlayerId, cmd.RecipeId, result.Outcome, result.Message);
        }

        return new CommandResult(result.Outcome == CraftingOutcome.Success || result.Outcome == CraftingOutcome.Discovery,
            result.Message,
            result.ProducedItem is not null ? new { result.ProducedItem.Id, result.ProducedItem.Name } : null);
    }
}

/// <summary>
/// Returns all recipes the player can craft (filtered by crafting skill and current world).
/// </summary>
public class ViewRecipesCommandHandler(
    IRecipeRepository recipeRepository,
    IPlayerRepository playerRepository,
    IHubContext<GameHub> hubContext) : ICommandHandler<ViewRecipesCommand>
{
    public async Task<CommandResult> HandleAsync(ViewRecipesCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        // Return all recipes the player's crafting skill can attempt
        var recipes = await recipeRepository.GetByCraftingSkillAsync(player.CraftingSkill, ct);

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
            Ingredients = r.Ingredients.Select(i => new
            {
                i.IngredientName,
                i.BaseQuantity,
                Category = i.Category.ToString(),
            }).ToList(),
        }).ToList();

        await hubContext.Clients
            .Group(cmd.PlayerId.ToString())
            .SendAsync("RecipeList", new { recipes = dtos }, ct);

        return new CommandResult(true, $"{dtos.Count} recipe(s) available.", dtos);
    }
}
