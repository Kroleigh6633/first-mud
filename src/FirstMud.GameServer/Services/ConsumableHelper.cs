using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Helpers for finding and consuming items during auto-farm.
///
/// Effect data (what potions exist, what they do, priority ordering) is
/// now loaded from content/consumables.json via IContentProvider. This
/// class only owns the "apply to player + persist + format message"
/// glue that can't reasonably live in a JSON file.
/// </summary>
public static class ConsumableHelper
{
    public const string HealingGroup = "Healing";
    public const string WeaveGroup   = "Weave";
    public const string BuffGroup    = "Buff";

    /// <summary>
    /// Snake-order list of healing consumable match-tokens, low-to-high potency.
    /// Backed by the content provider so designers can edit content/consumables.json.
    /// </summary>
    public static string[] HealingPriority(IContentProvider content) =>
        content.ConsumablesByGroup(HealingGroup).Select(c => c.MatchToken).ToArray();

    public static string[] WeavePriority(IContentProvider content) =>
        content.ConsumablesByGroup(WeaveGroup).Select(c => c.MatchToken).ToArray();

    public static string[] BuffNames(IContentProvider content) =>
        content.ConsumablesByGroup(BuffGroup).Select(c => c.MatchToken).ToArray();

    public static async Task<string?> ApplyAndConsumeAsync(
        Player player,
        Item item,
        IPlayerRepository playerRepo,
        IItemRepository itemRepo,
        IContentProvider content,
        CancellationToken ct)
    {
        var effect = content.ResolveConsumable(item.Name);
        if (effect is null) return null;

        string message;
        switch (effect.EffectType)
        {
            case "Heal":
            {
                var before = player.CurrentHp;
                player.HealHp(effect.Amount);
                var healed = player.CurrentHp - before;
                message = $"Auto-farm: used {item.Name}, restored {healed} HP. ({player.CurrentHp}/{player.MaxHp} HP)";
                break;
            }
            case "RestoreWeave":
            {
                player.RestoreWeave(effect.Amount);
                message = $"Auto-farm: used {item.Name}, restored {effect.Amount} Weave. ({player.Weave.VisibleState})";
                break;
            }
            case "Buff":
            {
                var label = effect.BuffKey switch
                {
                    "MaxHpBonus"        => $"+{(int)(effect.BuffValue * 100)}% max HP",
                    "SpeedBonus"        => $"+{(int)(effect.BuffValue * 100)}% speed",
                    "StrikeDamageBonus" => $"+{(int)(effect.BuffValue * 100)}% strike damage",
                    _                   => "a bonus"
                };
                message = $"Auto-farm: used {item.Name} ({label}) before dangerous fight.";
                break;
            }
            default:
                return null;
        }

        await playerRepo.UpdateAsync(player, ct);

        if (item.IsStackable && item.Quantity > 1)
        {
            item.TryRemoveQuantity(1, out _);
            await itemRepo.UpdateAsync(item, ct);
        }
        else
        {
            await itemRepo.DeleteAsync(item.Id, ct);
        }

        return message;
    }

    public static Item? FindBest(IReadOnlyList<Item> inventory, string[] priorityNames)
    {
        Item? best = null;
        int bestPriority = -1;

        foreach (var item in inventory)
        {
            if (item.Category != ItemCategory.Consumable) continue;
            var lower = item.Name.ToLowerInvariant();
            for (int i = 0; i < priorityNames.Length; i++)
            {
                if (lower.Contains(priorityNames[i]) && i > bestPriority)
                {
                    best = item;
                    bestPriority = i;
                }
            }
        }

        return best;
    }
}
