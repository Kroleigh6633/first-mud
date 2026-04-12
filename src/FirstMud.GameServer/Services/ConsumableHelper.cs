using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Pure static helpers for finding and consuming items during auto-farm.
///
/// Extracted from AutoFarmCommandHandler where they lived as private static
/// methods — giving them a named home and making them testable.
/// </summary>
public static class ConsumableHelper
{
    public static readonly string[] HealingPriority =
    [
        "minor healing draught",
        "healing potion",
        "greater healing elixir"
    ];

    public static readonly string[] WeavePriority =
    [
        "weave tincture",
        "weave elixir"
    ];

    public static readonly string[] BuffNames =
    [
        "fortitude brew",
        "speed draught",
        "strength tonic"
    ];

    private sealed record ConsumableEffect(
        string EffectType,
        int Amount,
        string? BuffKey = null,
        float BuffValue = 0f);

    private static ConsumableEffect? ResolveEffect(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("minor healing draught"))  return new ConsumableEffect("Heal", 30);
        if (n.Contains("healing potion"))         return new ConsumableEffect("Heal", 60);
        if (n.Contains("greater healing elixir")) return new ConsumableEffect("Heal", 100);
        if (n.Contains("weave tincture"))         return new ConsumableEffect("RestoreWeave", 20);
        if (n.Contains("weave elixir"))           return new ConsumableEffect("RestoreWeave", 50);
        if (n.Contains("fortitude brew"))         return new ConsumableEffect("Buff", 0, "MaxHpBonus", 0.10f);
        if (n.Contains("speed draught"))          return new ConsumableEffect("Buff", 0, "SpeedBonus", 0.20f);
        if (n.Contains("strength tonic"))         return new ConsumableEffect("Buff", 0, "StrikeDamageBonus", 0.15f);
        return null;
    }

    public static async Task<string?> ApplyAndConsumeAsync(
        Player player,
        Item item,
        IPlayerRepository playerRepo,
        IItemRepository itemRepo,
        CancellationToken ct)
    {
        var effect = ResolveEffect(item.Name);
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
