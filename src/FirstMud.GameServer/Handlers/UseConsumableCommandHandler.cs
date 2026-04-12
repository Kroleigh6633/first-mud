using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Services;

namespace FirstMud.GameServer.Handlers;

public class UseConsumableCommandHandler(
    IPlayerRepository playerRepository,
    IItemRepository itemRepository,
    GameNotificationService notificationService) : ICommandHandler<UseConsumableCommand>
{
    public async Task<CommandResult> HandleAsync(UseConsumableCommand cmd, CancellationToken ct)
    {
        var player = await playerRepository.GetByIdAsync(cmd.PlayerId, ct);
        if (player is null)
            return new CommandResult(false, "Player not found.");

        var item = await itemRepository.GetByIdAsync(cmd.ItemId, ct);
        if (item is null || item.OwnerId != cmd.PlayerId)
            return new CommandResult(false, "Item not found in your inventory.");

        if (item.Category != ItemCategory.Consumable)
            return new CommandResult(false, $"{item.Name} is not a consumable.");

        var effect = ResolveEffect(item.Name);
        if (effect is null)
            return new CommandResult(false, $"{item.Name} has no known effect.");

        var message = ApplyEffect(player, effect);

        await playerRepository.UpdateAsync(player, ct);

        // Consume the item (stackable or single)
        if (item.IsStackable && item.Quantity > 1)
        {
            item.TryRemoveQuantity(1, out _);
            await itemRepository.UpdateAsync(item, ct);
        }
        else
        {
            await itemRepository.DeleteAsync(item.Id, ct);
        }

        await notificationService.SendMessageAsync(cmd.PlayerId, "system", message, ct);

        // Refresh world-state so HP/Weave bars update immediately
        var hpPayload = new
        {
            PlayerId = player.Id.ToString(),
            player.CurrentHp,
            player.MaxHp,
            WeavePercent = player.Weave.Percentage,
            WeaveState = player.Weave.VisibleState.ToString()
        };
        await notificationService.SendEventAsync(cmd.PlayerId, "StatsRefreshed", hpPayload, ct);

        return new CommandResult(true, message);
    }

    // ─── Effect resolution ────────────────────────────────────────────────────

    private sealed record ConsumableEffect(
        string EffectType,   // Heal | RestoreWeave | Buff
        int Amount,
        string? BuffKey = null,
        float BuffValue = 0f);

    private static ConsumableEffect? ResolveEffect(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("minor healing draught"))    return new ConsumableEffect("Heal", 30);
        if (n.Contains("healing potion"))           return new ConsumableEffect("Heal", 60);
        if (n.Contains("greater healing elixir"))   return new ConsumableEffect("Heal", 100);
        if (n.Contains("weave tincture"))           return new ConsumableEffect("RestoreWeave", 20);
        if (n.Contains("weave elixir"))             return new ConsumableEffect("RestoreWeave", 50);
        if (n.Contains("fortitude brew"))           return new ConsumableEffect("Buff", 0, "MaxHpBonus", 0.10f);
        if (n.Contains("speed draught"))            return new ConsumableEffect("Buff", 0, "SpeedBonus", 0.20f);
        if (n.Contains("strength tonic"))           return new ConsumableEffect("Buff", 0, "StrikeDamageBonus", 0.15f);
        return null;
    }

    private static string ApplyEffect(Domain.Entities.Player player, ConsumableEffect effect)
    {
        switch (effect.EffectType)
        {
            case "Heal":
            {
                var before = player.CurrentHp;
                player.HealHp(effect.Amount);
                var healed = player.CurrentHp - before;
                return $"You drink the {effect.Amount} HP potion and recover {healed} HP. ({player.CurrentHp}/{player.MaxHp} HP)";
            }
            case "RestoreWeave":
            {
                player.RestoreWeave(effect.Amount);
                return $"You drink the tincture and restore {effect.Amount} Weave. ({player.Weave.VisibleState})";
            }
            case "Buff":
            {
                // Buff effects are cosmetic for now — the buff system will hook into
                // CombatService when next-combat tracking is implemented.
                var label = effect.BuffKey switch
                {
                    "MaxHpBonus"        => $"+{(int)(effect.BuffValue * 100)}% max HP",
                    "SpeedBonus"        => $"+{(int)(effect.BuffValue * 100)}% speed",
                    "StrikeDamageBonus" => $"+{(int)(effect.BuffValue * 100)}% strike damage",
                    _                   => "a bonus"
                };
                return $"You drink the brew. You feel empowered: {label} for the next combat.";
            }
            default:
                return "You use the item. Nothing seems to happen.";
        }
    }
}
