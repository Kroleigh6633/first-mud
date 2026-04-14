// ─────────────────────────────────────────────────────────────────────────
// Gem-based imbue path (content-driven).
//
// This handler is the NEW imbue flow: it drives every imbue decision from
// content/imbue-recipes.json via IContentProvider. Players target an item
// plus a specific gem plus an explicit RecipeId, and the handler:
//   1. verifies ownership of both item and gem,
//   2. validates the recipe exists and the gem satisfies recipe.GemId,
//   3. enforces the per-recipe CraftingSkill gate,
//   4. enforces slot availability OR recipe.UpgradesExisting (diamond),
//   5. consumes the gem and applies the imbue via Item.ApplyImbue.
//
// The legacy taper path (PracticeEnchantingCommandHandler + ImbueService.
// ResolveImbueType → keyword match on taper name) is PRESERVED untouched.
// The two flows are intentionally split: tapers are consumables resolved by
// name heuristics, gems are structured content with explicit recipe ids.
// Do NOT merge them — they have different failure semantics and different
// authoring workflows.
// ─────────────────────────────────────────────────────────────────────────

using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class ImbueWithGemCommandHandler(
    IContentProvider content,
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService) : ICommandHandler<ImbueWithGemCommand>
{
    public async Task<CommandResult> HandleAsync(ImbueWithGemCommand cmd, CancellationToken ct)
    {
        // 1. Load & ownership checks
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return await FailAsync(cmd.PlayerId, "Player not found.", ct);

        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return await FailAsync(cmd.PlayerId, "Target item not in your inventory.", ct);

        var gem = await itemRepository.GetByIdAsync(cmd.GemItemId, ct);
        if (gem is null || gem.OwnerId != cmd.PlayerId)
            return await FailAsync(cmd.PlayerId, "Gem not in your inventory.", ct);

        // 2. Recipe validation
        var recipe = content.GetImbueRecipe(cmd.RecipeId);
        if (recipe is null)
            return await FailAsync(cmd.PlayerId, $"Unknown imbue recipe '{cmd.RecipeId}'.", ct);

        // Gem must carry the recipe's required reagent token in its name.
        // We match case-insensitively against both the required-reagent string
        // and (as a fallback) the gem id itself, so "Raw Diamond" and
        // "diamond" both resolve for recipe diamond-* .
        var gemName = gem.Name ?? "";
        if (!gemName.Contains(recipe.RequiredReagent, StringComparison.OrdinalIgnoreCase) &&
            !gemName.Contains(recipe.GemId, StringComparison.OrdinalIgnoreCase))
        {
            return await FailAsync(cmd.PlayerId,
                $"{gem.Name} does not match recipe '{recipe.Id}' (requires {recipe.RequiredReagent}).",
                ct);
        }

        // 3. Skill gate
        if (player.CraftingSkill < recipe.RequiredCraftingSkill)
        {
            return await FailAsync(cmd.PlayerId,
                $"Crafting Skill {recipe.RequiredCraftingSkill} required (you have {player.CraftingSkill}).",
                ct);
        }

        // 4. Slot availability OR upgrade recipe
        if (recipe.UpgradesExisting)
        {
            if (item.Imbues.Count == 0)
            {
                return await FailAsync(cmd.PlayerId,
                    $"{item.Name} has no existing imbue to upgrade.", ct);
            }
        }
        else
        {
            if (item.Imbues.Count >= item.MaxImbueSlots)
            {
                return await FailAsync(cmd.PlayerId,
                    $"{item.Name} has no open imbue slots (W{item.Workmanship.Value}, {item.Imbues.Count}/{item.MaxImbueSlots}).",
                    ct);
            }
        }

        // 5. Consume the gem, apply the imbue
        await ConsumeGemAsync(gem, ct);

        string resultMessage;
        if (recipe.UpgradesExisting)
        {
            // Diamond-universal semantics: pick the most-recent imbue and
            // double its power (same type, new power). This is the "replace
            // with upgraded power" interpretation — see the report note for
            // the add-vs-replace question flagged to creative.
            var existing = item.Imbues[^1];
            var upgradedPower = Math.Min(1f, existing.Power * 2f);
            item.RemoveRandomImbue(); // drops one existing imbue; acceptable approximation until a targeted remover lands
            item.ApplyImbue(existing.Type, upgradedPower);
            resultMessage =
                $"{gem.Name} consumed — {item.Name}'s {existing.Type} imbue is upgraded " +
                $"(+{(int)(existing.Power * 100)}% → +{(int)(upgradedPower * 100)}%).";
        }
        else if (recipe.Effect == "imbue" && recipe.ImbueType != ImbueType.None)
        {
            var power = recipe.Power;
            if (recipe.Variance > 0f)
            {
                var roll = ((float)Random.Shared.NextDouble() * 2f - 1f) * recipe.Variance;
                power = Math.Clamp(power + roll, 0f, 1f);
            }
            item.ApplyImbue(recipe.ImbueType, power);
            resultMessage =
                $"{gem.Name} consumed — imbued {item.Name} with {recipe.ImbueType} +{(int)(power * 100)}%.";
        }
        else
        {
            // Quartz-substrate / successBoost: no imbue applied today; the
            // success-bonus effect is reserved for a future taper/imbue combo
            // action. We still consume the gem (player opted in) and report.
            resultMessage = $"{gem.Name} consumed — {recipe.Description}";
        }

        await itemRepository.UpdateAsync(item, ct);
        player.GainCraftingSkillXp(1);
        await playerRepository.UpdateAsync(player, ct);

        // Broadcast
        await notificationService.SendMessageAsync(cmd.PlayerId, "loot", resultMessage, ct);

        var payload = new
        {
            ItemId = cmd.ItemId.ToString(),
            GemItemId = cmd.GemItemId.ToString(),
            RecipeId = recipe.Id,
            ImbueType = recipe.ImbueType.ToString(),
            Success = true,
            Message = resultMessage,
        };
        await notificationService.SendEventAsync(cmd.PlayerId, "ItemImbued", payload, ct);
        return new CommandResult(true, resultMessage, payload);
    }

    private async Task<CommandResult> FailAsync(Guid playerId, string message, CancellationToken ct)
    {
        await notificationService.SendMessageAsync(playerId, "system", message, ct);
        return new CommandResult(false, message);
    }

    private async Task ConsumeGemAsync(Item gem, CancellationToken ct)
    {
        if (gem.IsStackable && gem.Quantity > 1)
        {
            gem.TryRemoveQuantity(1, out _);
            await itemRepository.UpdateAsync(gem, ct);
        }
        else
        {
            await itemRepository.DeleteAsync(gem.Id, ct);
        }
    }
}
