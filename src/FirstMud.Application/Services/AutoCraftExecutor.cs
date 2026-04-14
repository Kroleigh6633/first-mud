using FirstMud.Application.Content;
using FirstMud.Application.Models;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

/// <summary>
/// Outcome of a single <see cref="AutoCraftExecutor.TickAsync"/> call. When the
/// planner can satisfy a recipe right now, Craft succeeds and DidEquip reflects
/// whether the result beat the old gear. Otherwise (no recipe, or missing
/// ingredients) the plan itself is returned so the caller (AutoProgressionService)
/// can route to auto-farm instead.
/// </summary>
public sealed record AutoCraftTickResult(
    bool DispatchedCraft,
    CraftingResult? Crafting,
    AutoCraftPlan Plan,
    bool DidEquip,
    string Reason);

/// <summary>
/// Executes <see cref="AutoCraftPlanner"/> plans against live services. Separated
/// from the planner so the planner stays pure and unit-testable. The executor
/// owns the I/O: stash lookup, CraftingService.AttemptCraftAsync, auto-equip.
/// </summary>
public class AutoCraftExecutor
{
    private readonly CraftingService _crafting;
    private readonly IPlayerRepository _players;
    private readonly IItemRepository _items;
    private readonly IHomesteadRepository _homesteads;
    private readonly IContentProvider _content;

    public AutoCraftExecutor(
        CraftingService crafting,
        IPlayerRepository players,
        IItemRepository items,
        IHomesteadRepository homesteads,
        IContentProvider content)
    {
        _crafting = crafting;
        _players = players;
        _items = items;
        _homesteads = homesteads;
        _content = content;
    }

    /// <summary>
    /// Build a plan from live state without executing. Useful for the
    /// AutoProgressionService Gear dispatch when we only need the ingredient
    /// plan to route auto-farm. Optionally reachability-gated by
    /// <paramref name="playerReliableDanger"/> (Task #114).
    /// </summary>
    public async Task<AutoCraftPlan> PlanAsync(
        Guid playerId,
        CancellationToken ct,
        int? playerReliableDanger = null)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
        {
            return new AutoCraftPlan(null, EquipmentSlot.None, 0, 0, 0.0,
                false, Array.Empty<IngredientPlanEntry>(), "Player not found.");
        }

        var stash = await BuildStashAsync(playerId, ct);
        var equippedWm = await BuildEquippedWorkmanshipAsync(player, ct);

        if (playerReliableDanger is int prd)
        {
            var zoneDangers = _content.AllZones()
                .GroupBy(z => z.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Max(z => z.DangerLevel), StringComparer.OrdinalIgnoreCase);
            return AutoCraftPlanner.Plan(
                _content.AllRecipes(),
                player.Position.World,
                player.CraftingSkill,
                equippedWm,
                stash,
                playerReliableDanger: prd,
                zoneDangerByName: zoneDangers);
        }

        return AutoCraftPlanner.Plan(
            _content.AllRecipes(),
            player.Position.World,
            player.CraftingSkill,
            equippedWm,
            stash);
    }

    /// <summary>
    /// Single auto-craft tick: plan → (craft + maybe equip) or return plan for
    /// the caller to dispatch auto-farm against. When <paramref name="playerReliableDanger"/>
    /// is supplied, unreachable recipes are gated out (Task #114).
    /// </summary>
    public async Task<AutoCraftTickResult> TickAsync(
        Guid playerId,
        CancellationToken ct,
        int? playerReliableDanger = null)
    {
        var plan = await PlanAsync(playerId, ct, playerReliableDanger);
        if (plan.TargetRecipe is null)
        {
            return new AutoCraftTickResult(false, null, plan, false, plan.Reason);
        }

        if (!plan.CraftableNow)
        {
            return new AutoCraftTickResult(false, null, plan, false, plan.Reason);
        }

        // Ingredients are present — resolve component item ids and craft.
        var componentIds = await ResolveComponentIdsAsync(playerId, plan.TargetRecipe, ct);
        if (componentIds.Count == 0)
        {
            // Race condition: stash said we had it, but concrete item ids couldn't be gathered.
            return new AutoCraftTickResult(false, null, plan with
            {
                CraftableNow = false,
                Reason = $"Could not resolve component item ids for {plan.TargetRecipe.ResultItemName}."
            }, false, plan.Reason);
        }

        var craftResult = await _crafting.AttemptCraftAsync(
            playerId, plan.TargetRecipe.RecipeId, componentIds, taperId: null, ct);

        // Auto-equip if produced item beats currently-equipped in the target slot.
        bool didEquip = false;
        if (craftResult.ProducedItem is Item crafted &&
            (craftResult.Outcome is CraftingOutcome.Success or CraftingOutcome.Discovery))
        {
            didEquip = await TryAutoEquipAsync(playerId, plan.TargetSlot, crafted, ct);
        }

        return new AutoCraftTickResult(
            DispatchedCraft: true,
            Crafting: craftResult,
            Plan: plan,
            DidEquip: didEquip,
            Reason: craftResult.Message ?? plan.Reason);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Aggregated name→quantity map over inventory + homestead storage.</summary>
    public async Task<IReadOnlyDictionary<string, int>> BuildStashAsync(
        Guid playerId, CancellationToken ct)
    {
        var inv = await _items.GetByOwnerAsync(playerId, ct);
        var stash = inv
            .GroupBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Sum(i => Math.Max(1, i.Quantity)), StringComparer.OrdinalIgnoreCase);

        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is not null)
        {
            var entries = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
            if (entries.Count > 0)
            {
                var storageItems = await _items.GetByIdsAsync(entries.Select(e => e.ItemId), ct);
                foreach (var item in storageItems)
                {
                    stash.TryGetValue(item.Name, out var existing);
                    stash[item.Name] = existing + Math.Max(1, item.Quantity);
                }
            }
        }
        return stash;
    }

    private async Task<IReadOnlyDictionary<EquipmentSlot, int>> BuildEquippedWorkmanshipAsync(
        Player player, CancellationToken ct)
    {
        var map = new Dictionary<EquipmentSlot, int>();
        foreach (var (slot, itemId) in player.EquippedItems)
        {
            if (itemId == Guid.Empty) continue;
            var item = await _items.GetByIdAsync(itemId, ct);
            if (item is null) continue;
            map[slot] = item.Workmanship.Value;
        }
        return map;
    }

    private async Task<List<Guid>> ResolveComponentIdsAsync(
        Guid playerId, RecipeDefinition recipe, CancellationToken ct)
    {
        var inv = await _items.GetByOwnerAsync(playerId, ct);
        IReadOnlyList<Item> storage = Array.Empty<Item>();
        var homestead = await _homesteads.GetByPlayerIdAsync(playerId, ct);
        if (homestead is not null)
        {
            var entries = await _homesteads.GetStorageItemsAsync(homestead.Id, ct);
            if (entries.Count > 0)
                storage = await _items.GetByIdsAsync(entries.Select(e => e.ItemId), ct);
        }

        var resolved = new List<Guid>();
        foreach (var ing in recipe.Ingredients)
        {
            var needed = ing.BaseQuantity;
            foreach (var source in new[] { inv, storage })
            {
                if (needed <= 0) break;
                var matching = source
                    .Where(i => string.Equals(i.Name, ing.Name, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(i => i.Quantity)
                    .ToList();
                foreach (var item in matching)
                {
                    if (needed <= 0) break;
                    resolved.Add(item.Id);
                    needed -= Math.Max(1, item.Quantity);
                }
            }
            if (needed > 0) return new List<Guid>();
        }
        return resolved;
    }

    private async Task<bool> TryAutoEquipAsync(
        Guid playerId, EquipmentSlot slot, Item crafted, CancellationToken ct)
    {
        if (slot == EquipmentSlot.None) return false;

        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null) return false;

        // Compare Workmanship with whatever's already in that slot.
        var currentId = player.GetEquipped(slot);
        int currentWm = 0;
        if (currentId is Guid gid && gid != Guid.Empty)
        {
            var existing = await _items.GetByIdAsync(gid, ct);
            if (existing is not null) currentWm = existing.Workmanship.Value;
        }

        if (crafted.Workmanship.Value <= currentWm) return false;

        // Ensure the crafted item has the right slot and owner set before equipping.
        if (crafted.Slot == EquipmentSlot.None) crafted.SetSlot(slot);
        if (crafted.OwnerId != playerId) crafted.SetOwner(playerId);
        await _items.UpdateAsync(crafted, ct);

        player.Equip(slot, crafted.Id);
        await _players.UpdateAsync(player, ct);
        return true;
    }
}
