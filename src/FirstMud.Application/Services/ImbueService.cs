using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public enum ImbueOutcome
{
    Success,
    CriticalSuccess,
    Failure,
    CatastrophicFailure
}

public record ImbueResult(bool Success, string Message, ImbueOutcome Outcome);

public class ImbueService
{
    private readonly IPlayerRepository _players;
    private readonly IItemRepository _items;
    private readonly ILogger<ImbueService> _logger;

    // Base power by imbue category
    private const float ElementalBasePower   = 0.30f;
    private const float ProtectiveBasePower  = 0.20f;
    private const float WyrdBasePower        = 0.10f;
    private const float RestorationBasePower = 0.15f;

    public ImbueService(
        IPlayerRepository players,
        IItemRepository items,
        ILogger<ImbueService> logger)
    {
        _players = players;
        _items = items;
        _logger = logger;
    }

    public async Task<ImbueResult> ImbueAsync(
        Guid playerId,
        Guid itemId,
        Guid taperId,
        CancellationToken ct = default)
    {
        // 1. Load entities
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
            return new ImbueResult(false, "Player not found.", ImbueOutcome.Failure);

        var item = await _items.GetByIdAsync(itemId, ct);
        if (item is null || item.OwnerId != playerId)
            return new ImbueResult(false, "Item not found in your inventory.", ImbueOutcome.Failure);

        var taper = await _items.GetByIdAsync(taperId, ct);
        if (taper is null || taper.OwnerId != playerId)
            return new ImbueResult(false, "Taper not found in your inventory.", ImbueOutcome.Failure);

        // 2. Validate: taper must be a Reagent
        if (taper.Category != ItemCategory.Reagent)
            return new ImbueResult(false, $"{taper.Name} is not a usable imbuing reagent.", ImbueOutcome.Failure);

        // 3. Determine imbue type from taper name
        var imbueType = ResolveImbueType(taper.Name);
        if (imbueType == ImbueType.None)
            return new ImbueResult(false, $"{taper.Name} does not carry an imbuing resonance.", ImbueOutcome.Failure);

        // 4. Calculate success chance: min(100, CraftingSkill * 5 + 20)
        var successChance = Math.Min(100, player.CraftingSkill * 5 + 20);

        // 5. Overimbue check: if slots are already full, 50% catastrophic failure added on top
        bool isOverimbuingAttempt = item.Imbues.Count >= item.MaxImbueSlots;

        // 6. Roll outcome
        var roll = Random.Shared.Next(100); // 0–99

        // Critical success window: 5% base, grows by 0.5% per crafting skill point (max 10%)
        var critChance = Math.Min(10, 5 + player.CraftingSkill / 2);

        ImbueOutcome outcome;
        if (isOverimbuingAttempt)
        {
            // 50% chance of catastrophic failure when overimbuing
            if (roll < 50)
            {
                outcome = ImbueOutcome.CatastrophicFailure;
            }
            else
            {
                // Within the remaining 50%, apply normal outcome distribution
                var adjustedRoll = (roll - 50) * 2; // scale 50–99 → 0–99
                outcome = ResolveOutcome(adjustedRoll, successChance, critChance);
            }
        }
        else
        {
            outcome = ResolveOutcome(roll, successChance, critChance);
        }

        // Consume taper (always — even on failure/catastrophe)
        await ConsumeTaperAsync(taper, ct);

        // Handle outcome
        switch (outcome)
        {
            case ImbueOutcome.Failure:
                _logger.LogInformation("Imbue failed for player {PlayerId} on item {ItemId}", playerId, itemId);
                return new ImbueResult(false,
                    $"The {taper.Name} flickers and goes dark. {item.Name} remains unchanged.",
                    ImbueOutcome.Failure);

            case ImbueOutcome.CatastrophicFailure:
            {
                string catastropheMsg;
                if (item.Imbues.Count > 0 && Random.Shared.Next(2) == 0)
                {
                    var lost = item.Imbues[Random.Shared.Next(item.Imbues.Count)];
                    item.RemoveRandomImbue();
                    catastropheMsg = $"Catastrophic failure! The Weave backlashes — the {lost.Type} imbue on {item.Name} is torn away.";
                }
                else
                {
                    item.DegradeWorkmanship();
                    catastropheMsg = $"Catastrophic failure! The Weave backlashes — {item.Name} is degraded (now W{item.Workmanship.Value}).";
                }

                await _items.UpdateAsync(item, ct);
                _logger.LogWarning("Catastrophic imbue failure for player {PlayerId} on item {ItemId}", playerId, itemId);
                return new ImbueResult(false, catastropheMsg, ImbueOutcome.CatastrophicFailure);
            }

            case ImbueOutcome.Success:
            case ImbueOutcome.CriticalSuccess:
            {
                string resultMessage;

                if (imbueType == ImbueType.Fortifying)
                {
                    // Fortifying boosts Workmanship instead of adding a power imbue
                    var boost = outcome == ImbueOutcome.CriticalSuccess ? 2 : 1;
                    item.BoostWorkmanship(boost);
                    // Still apply imbue record to consume a slot and mark WyrdTouched
                    item.ApplyImbue(ImbueType.Fortifying, 0f);
                    var critNote = outcome == ImbueOutcome.CriticalSuccess ? " (Critical! +2 Workmanship)" : " (+1 Workmanship)";
                    resultMessage = $"Success! {item.Name} is fortified{critNote} — now W{item.Workmanship.Value}.";
                }
                else
                {
                    // Diminishing returns: power = basePower / 2^(sameTypeCount - 1)
                    var basePower = GetBasePower(imbueType);
                    var sameTypeCount = item.Imbues.Count(i => i.Type == imbueType);
                    var power = sameTypeCount == 0
                        ? basePower
                        : basePower / MathF.Pow(2f, sameTypeCount);

                    if (outcome == ImbueOutcome.CriticalSuccess)
                        power *= 2f;

                    item.ApplyImbue(imbueType, power);

                    var powerPct = (int)(power * 100);
                    var critSuffix    = outcome == ImbueOutcome.CriticalSuccess ? " (Critical! Double power)" : string.Empty;
                    var unstableSuffix = item.IsUnstable ? " [UNSTABLE]" : string.Empty;
                    resultMessage = $"Success! {item.Name} is now imbued with {imbueType} +{powerPct}%{critSuffix}{unstableSuffix}.";

                    _logger.LogInformation("Imbue success for player {PlayerId}: {ItemId} with {ImbueType} +{Power:P0}",
                        playerId, itemId, imbueType, power);
                }

                if (item.IsUnstable)
                    resultMessage += " Warning: this item is overimbued and unstable.";

                await _items.UpdateAsync(item, ct);
                player.GainCraftingSkillXp(1);
                await _players.UpdateAsync(player, ct);

                return new ImbueResult(true, resultMessage, outcome);
            }

            default:
                return new ImbueResult(false, "Unexpected imbue outcome.", ImbueOutcome.Failure);
        }
    }

    // ─── private helpers ────────────────────────────────────────────────────

    private static ImbueOutcome ResolveOutcome(int roll, int successChance, int critChance)
    {
        // Catastrophic failure: 5% chance at skill 1, diminishes as successChance rises, 0% at skill 10+
        var catastrophicChance = Math.Max(0, 5 - (successChance - 20) / 20);

        if (roll < catastrophicChance)
            return ImbueOutcome.CatastrophicFailure;

        if (roll >= successChance)
            return ImbueOutcome.Failure;

        // Within success window: top critChance% are critical
        var critThreshold = successChance - critChance;
        if (roll >= critThreshold)
            return ImbueOutcome.CriticalSuccess;

        return ImbueOutcome.Success;
    }

    public static ImbueType ResolveImbueType(string taperName)
    {
        var n = taperName.ToLowerInvariant();
        if (n.Contains("fire shaping") || n.Contains("fire shard"))  return ImbueType.Fire;
        if (n.Contains("water shaping") || n.Contains("water shard")) return ImbueType.Water;
        if (n.Contains("earth shaping") || n.Contains("earth shard")) return ImbueType.Earth;
        if (n.Contains("air shaping") || n.Contains("air shard"))    return ImbueType.Air;
        if (n.Contains("fortitude") || n.Contains("fortifying"))     return ImbueType.Fortifying;
        if (n.Contains("warding") || n.Contains("protective"))       return ImbueType.Protective;
        if (n.Contains("wyrd shard") || n.Contains("wyrd taper"))    return ImbueType.Wyrd;
        if (n.Contains("dravenite") || n.Contains("restoration"))    return ImbueType.Restoration;
        return ImbueType.None;
    }

    private static float GetBasePower(ImbueType type) => type switch
    {
        ImbueType.Fire        => ElementalBasePower,
        ImbueType.Water       => ElementalBasePower,
        ImbueType.Earth       => ElementalBasePower,
        ImbueType.Air         => ElementalBasePower,
        ImbueType.Protective  => ProtectiveBasePower,
        ImbueType.Wyrd        => WyrdBasePower,
        ImbueType.Restoration => RestorationBasePower,
        _                     => 0f
    };

    private async Task ConsumeTaperAsync(Item taper, CancellationToken ct)
    {
        if (taper.IsStackable && taper.Quantity > 1)
        {
            taper.TryRemoveQuantity(1, out _);
            await _items.UpdateAsync(taper, ct);
        }
        else
        {
            await _items.DeleteAsync(taper.Id, ct);
        }
    }
}
