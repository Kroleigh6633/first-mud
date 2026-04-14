using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;

namespace FirstMud.Application.Services;

/// <summary>
/// Outcome of a single <see cref="AutoImbueExecutor.TickAsync"/> call. When
/// the planner can satisfy an imbue right now, DispatchedImbue = true and
/// <see cref="ImbueResult"/> describes what happened. Otherwise the plan is
/// returned so the caller (<see cref="AutoProgressionService"/>) can route
/// the tick to auto-farm for the missing reagent.
/// </summary>
public sealed record AutoImbueTickResult(
    bool DispatchedImbue,
    ImbueResult? Imbue,
    AutoImbuePlan Plan,
    string Reason);

/// <summary>
/// Executes <see cref="AutoImbuePlanner"/> plans against live services.
/// Separated from the planner so the planner stays pure and unit-testable.
///
/// The executor:
///  1. Loads the player + equipped items (via <see cref="IItemRepository"/>
///     and <see cref="IHomesteadRepository"/>).
///  2. Builds a stash snapshot (inventory + homestead storage, name-aggregated).
///  3. Calls <see cref="AutoImbuePlanner.Plan"/>.
///  4. If the plan is <see cref="AutoImbuePlan.ImbuableNow"/>, resolves the
///     target-item id + reagent item id and dispatches through
///     <see cref="ImbueService"/> (taper path — which accepts both tapers
///     AND gem-style reagents provided ResolveImbueType can map them).
///  5. Otherwise returns the plan with DispatchedImbue=false so the caller
///     can dispatch Farm for the missing reagent.
/// </summary>
public class AutoImbueExecutor
{
    private readonly ImbueService _imbue;
    private readonly IPlayerRepository _players;
    private readonly IItemRepository _items;
    private readonly IHomesteadRepository _homesteads;
    private readonly IContentProvider _content;

    public AutoImbueExecutor(
        ImbueService imbue,
        IPlayerRepository players,
        IItemRepository items,
        IHomesteadRepository homesteads,
        IContentProvider content)
    {
        _imbue = imbue;
        _players = players;
        _items = items;
        _homesteads = homesteads;
        _content = content;
    }

    /// <summary>Plan without executing — used by AutoProgressionService when
    /// all we need is the missing-reagent plan to route auto-farm.</summary>
    public async Task<AutoImbuePlan> PlanAsync(Guid playerId, CancellationToken ct)
    {
        var player = await _players.GetByIdAsync(playerId, ct);
        if (player is null)
        {
            return new AutoImbuePlan(null, null, EquipmentSlot.None, 0.0, 1.0,
                ImbueType.None, null, null, false, false,
                Array.Empty<ImbueReagentPlanEntry>(), "Player not found.");
        }

        var snapshots = await BuildEquippedSnapshotsAsync(player, ct);
        var stash = await BuildStashAsync(playerId, ct);

        // Pick weapon element — prefer melee, then ranged, then focus.
        MagicElement? weaponElement = null;
        foreach (var slot in new[] { EquipmentSlot.MeleeWeapon, EquipmentSlot.RangedWeapon, EquipmentSlot.Focus })
        {
            var wId = player.GetEquipped(slot);
            if (wId is Guid wg && wg != Guid.Empty)
            {
                var w = await _items.GetByIdAsync(wg, ct);
                if (w?.MagicalElement is MagicElement me) { weaponElement = me; break; }
            }
        }

        return AutoImbuePlanner.Plan(
            snapshots,
            weaponElement,
            player.PrimaryElement == default ? MagicElement.Aether : player.PrimaryElement,
            player.CraftingSkill,
            stash,
            _content.AllImbueRecipes());
    }

    /// <summary>One auto-imbue tick: plan → (imbue) or return plan for the
    /// caller to dispatch auto-farm against.</summary>
    public async Task<AutoImbueTickResult> TickAsync(Guid playerId, CancellationToken ct)
    {
        var plan = await PlanAsync(playerId, ct);

        if (plan.TargetItemId is null)
            return new AutoImbueTickResult(false, null, plan, plan.Reason);

        if (!plan.ImbuableNow)
            return new AutoImbueTickResult(false, null, plan, plan.Reason);

        // Resolve an unequipped copy of the target item. The imbue path in
        // ImbueService rejects equipped/locked items; the PracticeEnchanting
        // handler already works around this, and so do we — we imbue an
        // UNEQUIPPED inventory item with the same name + best Workmanship and
        // leave the equipped slot alone. If none is found we fall back to the
        // equipped id (ImbueService will reject and we report the failure).
        var inv = await _items.GetByOwnerAsync(playerId, ct);
        var target = inv
            .Where(i => i.Id == plan.TargetItemId
                        || (string.Equals(i.Name, plan.TargetItemName, StringComparison.OrdinalIgnoreCase)
                            && !i.IsLocked))
            .OrderByDescending(i => i.Workmanship.Value)
            .FirstOrDefault();

        if (target is null)
            return new AutoImbueTickResult(false, null, plan with
            {
                ImbuableNow = false,
                Reason = $"Could not resolve unequipped target item for {plan.TargetItemName}."
            }, $"Could not resolve unequipped target item for {plan.TargetItemName}.");

        // Resolve a reagent item id matching the selected reagent name (case-insensitive contains).
        var reagent = inv.FirstOrDefault(i =>
            i.Category == ItemCategory.Reagent
            && (i.Name?.Contains(plan.SelectedReagentName ?? "", StringComparison.OrdinalIgnoreCase) ?? false));

        if (reagent is null)
            return new AutoImbueTickResult(false, null, plan with
            {
                ImbuableNow = false,
                Reason = $"Reagent {plan.SelectedReagentName} not in inventory (may be in storage — farm-dispatch skipped)."
            }, $"Reagent {plan.SelectedReagentName} not in inventory.");

        var imbueResult = await _imbue.ImbueAsync(playerId, target.Id, reagent.Id, ct);
        return new AutoImbueTickResult(
            DispatchedImbue: true,
            Imbue: imbueResult,
            Plan: plan,
            Reason: imbueResult.Message);
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

    private async Task<IReadOnlyList<EquippedSlotSnapshot>> BuildEquippedSnapshotsAsync(
        Player player, CancellationToken ct)
    {
        var snaps = new List<EquippedSlotSnapshot>();
        foreach (var (slot, itemId) in player.EquippedItems)
        {
            if (itemId == Guid.Empty) continue;
            var item = await _items.GetByIdAsync(itemId, ct);
            if (item is null) continue;
            snaps.Add(new EquippedSlotSnapshot(
                Slot: slot,
                ItemId: item.Id,
                ItemName: item.Name,
                MaxImbueSlots: item.MaxImbueSlots,
                ImbueCount: item.Imbues.Count));
        }
        return snaps;
    }
}
