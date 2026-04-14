using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Application.Services;

/// <summary>
/// One line of an imbue plan: need <paramref name="Quantity"/> more of
/// <paramref name="ItemName"/> (typically a gem or a taper reagent).
/// Reuses <see cref="IngredientSource"/> from the auto-craft planner so the
/// downstream Farm dispatch in <see cref="AutoProgressionService"/> can route
/// both paths identically.
/// </summary>
public sealed record ImbueReagentPlanEntry(
    string ItemName,
    int Quantity,
    IngredientSource Source);

/// <summary>
/// Output of <see cref="AutoImbuePlanner.Plan"/>.
///
/// When <see cref="TargetItemId"/> is null the planner could not find any
/// equipped item with an open imbue slot the player can fill (caller should
/// fall through to the default sub-mode dispatch — e.g. Farm).
///
/// When <see cref="ImbuableNow"/> is true the executor can immediately call
/// into <see cref="ImbueService"/> / gem-imbue path; otherwise
/// <see cref="MissingReagents"/> lists what to farm first.
/// </summary>
public sealed record AutoImbuePlan(
    Guid? TargetItemId,
    string? TargetItemName,
    EquipmentSlot TargetSlot,
    double PartyCoverage,
    double WorstSlotCoverage,
    ImbueType SelectedImbueType,
    string? SelectedRecipeId,          // non-null when a gem recipe was picked
    string? SelectedReagentName,       // the taper/gem item name to consume
    bool UsesGem,                      // true when SelectedRecipeId points at imbue-recipes.json
    bool ImbuableNow,
    IReadOnlyList<ImbueReagentPlanEntry> MissingReagents,
    string Reason);

/// <summary>
/// Pure planning logic for the auto-imbue sub-mode. Mirrors
/// <see cref="AutoCraftPlanner"/> — no DI, no I/O. Callers feed live state
/// in, the planner returns a decision.
///
/// Coverage metric (per-slot):
///   coverage(slot) = min(1.0, imbuesFilled / maxImbueSlots)
/// Party coverage is the unweighted mean across all equipped slots with a
/// non-empty item. Worst-covered slot is the equipped slot with the lowest
/// ratio; ties break by (a) larger <c>MaxImbueSlots</c> capacity (big items
/// benefit more per imbue), then (b) lower <see cref="EquipmentSlot"/>
/// ordinal. A slot must have <c>Imbues.Count &lt; MaxImbueSlots</c> to be
/// considered — fully-saturated slots are ineligible.
///
/// Imbue-type selection (deterministic):
///   1. If the equipped weapon has a non-null <c>MagicalElement</c>, prefer
///      that element as the imbue type (fire → Fire, water → Water, etc.).
///   2. Else, map the player's <c>PrimaryElement</c> onto the same type.
///   3. Else, histogram reagent names in stash and pick the imbue type whose
///      preferred reagent has the largest count.
/// Whatever survives step 3 is filtered by:
///   - reagent in stash OR reagent in <see cref="ResolveReagentSource"/>
///     (farmable), AND
///   - <c>recipe.RequiredCraftingSkill ≤ playerCraftingSkill</c>.
/// Gem recipes (when <c>allImbueRecipes</c> is non-empty) win when their
/// power exceeds the taper baseline for the same <see cref="ImbueType"/>.
/// </summary>
public static class AutoImbuePlanner
{
    // Canonical taper/gem reagent names by imbue type. The gem-preferred
    // column is tried FIRST (higher power); taper as fallback.
    private static readonly IReadOnlyDictionary<ImbueType, string> PreferredGemByType =
        new Dictionary<ImbueType, string>
        {
            [ImbueType.Fire]        = "Topaz",
            [ImbueType.Water]       = "Aquamarine",
            [ImbueType.Earth]       = "Emerald",
            [ImbueType.Air]         = "Topaz",
            [ImbueType.Wyrd]        = "Amethyst",
            [ImbueType.Restoration] = "Aquamarine",
            [ImbueType.Protective]  = "Diamond",
            [ImbueType.Fortifying]  = "Quartz",
        };

    private static readonly IReadOnlyDictionary<ImbueType, string> PreferredTaperByType =
        new Dictionary<ImbueType, string>
        {
            [ImbueType.Fire]        = "Fire Shaping Taper",
            [ImbueType.Water]       = "Water Shaping Taper",
            [ImbueType.Earth]       = "Earth Shaping Taper",
            [ImbueType.Air]         = "Air Shaping Taper",
            [ImbueType.Wyrd]        = "Wyrd Taper",
            [ImbueType.Restoration] = "Dravenite Taper",
            [ImbueType.Protective]  = "Warding Taper",
            [ImbueType.Fortifying]  = "Fortifying Taper",
        };

    // Gems come from common-materials loot (see loot-tables.json). Tapers
    // are smelted/purchased; we treat them as common-materials loot for now
    // so the farm dispatch has a zone hint.
    private static readonly IReadOnlyDictionary<string, (string Biome, string ZoneHint)> ReagentSources =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["Topaz"]      = ("common-materials", "Starting Road"),
            ["Aquamarine"] = ("common-materials", "Starting Road"),
            ["Emerald"]    = ("common-materials", "Starting Road"),
            ["Amethyst"]   = ("common-materials", "Starting Road"),
            ["Quartz"]     = ("common-materials", "Starting Road"),
            ["Obsidian"]   = ("common-materials", "Starting Road"),
            ["Diamond"]    = ("biome-mountain",   "Caervorn Highlands"),
            ["Black Pearl"] = ("biome-ocean",     "Fairgean Coast"),
            ["Fire Shaping Taper"]  = ("common-materials", "Starting Road"),
            ["Water Shaping Taper"] = ("common-materials", "Starting Road"),
            ["Earth Shaping Taper"] = ("common-materials", "Starting Road"),
            ["Air Shaping Taper"]   = ("common-materials", "Starting Road"),
            ["Wyrd Taper"]          = ("common-materials", "Starting Road"),
            ["Dravenite Taper"]     = ("common-materials", "Starting Road"),
            ["Warding Taper"]       = ("common-materials", "Starting Road"),
            ["Fortifying Taper"]    = ("common-materials", "Starting Road"),
        };

    /// <summary>Per-slot imbue coverage as a fraction 0..1.</summary>
    public readonly record struct SlotCoverage(EquipmentSlot Slot, int Filled, int Max)
    {
        public double Ratio => Max == 0 ? 1.0 : (double)Filled / Max;
        public bool HasOpenSlot => Filled < Max;
    }

    /// <summary>
    /// Compute party coverage as unweighted mean of per-slot ratios across
    /// every equipped item. Empty equipped slots contribute 0 (the party
    /// isn't wearing that piece at all).
    /// </summary>
    public static double PartyCoverage(IReadOnlyList<SlotCoverage> equippedSlots)
    {
        if (equippedSlots.Count == 0) return 0.0;
        return equippedSlots.Average(s => s.Ratio);
    }

    /// <summary>
    /// Pick the worst-covered slot (lowest ratio), breaking ties by (1) larger
    /// Max capacity then (2) lower EquipmentSlot ordinal. Returns null if no
    /// slot has an open imbue.
    /// </summary>
    public static SlotCoverage? PickWorstSlot(IReadOnlyList<SlotCoverage> equippedSlots)
    {
        var candidates = equippedSlots.Where(s => s.HasOpenSlot).ToList();
        if (candidates.Count == 0) return null;
        return candidates
            .OrderBy(s => s.Ratio)
            .ThenByDescending(s => s.Max)
            .ThenBy(s => (int)s.Slot)
            .First();
    }

    /// <summary>Resolve where a reagent can be sourced from. Public for testing.</summary>
    public static IngredientSource ResolveReagentSource(string reagentName)
    {
        if (ReagentSources.TryGetValue(reagentName, out var r))
        {
            var kind = r.Biome == "common-materials" ? "loot" : "harvest";
            return new IngredientSource(kind, r.ZoneHint, r.Biome, SubRecipeId: null);
        }
        return new IngredientSource("loot", "Starting Road", "common-materials", SubRecipeId: null);
    }

    /// <summary>
    /// Pick the best ImbueType for the party given the weapon element, the
    /// player's primary magic element, and a histogram of stash reagent
    /// counts. Public for testing.
    /// </summary>
    public static ImbueType SelectImbueType(
        MagicElement? weaponElement,
        MagicElement playerPrimaryElement,
        IReadOnlyDictionary<string, int> stash)
    {
        if (weaponElement is MagicElement we && we != default)
        {
            var mapped = MapElement(we);
            if (mapped != ImbueType.None) return mapped;
        }
        if (playerPrimaryElement != default)
        {
            var mapped = MapElement(playerPrimaryElement);
            if (mapped != ImbueType.None) return mapped;
        }
        // Histogram fallback: pick the ImbueType with the best-stocked preferred reagent.
        ImbueType best = ImbueType.Fire;
        int bestCount = -1;
        foreach (var kvp in PreferredGemByType)
        {
            stash.TryGetValue(kvp.Value, out var gemCount);
            stash.TryGetValue(PreferredTaperByType.GetValueOrDefault(kvp.Key) ?? "", out var taperCount);
            var total = gemCount + taperCount;
            if (total > bestCount)
            {
                bestCount = total;
                best = kvp.Key;
            }
        }
        return best;
    }

    private static ImbueType MapElement(MagicElement e) => e switch
    {
        MagicElement.Fire  => ImbueType.Fire,
        MagicElement.Water => ImbueType.Water,
        MagicElement.Earth => ImbueType.Earth,
        MagicElement.Air   => ImbueType.Air,
        MagicElement.Aether => ImbueType.Wyrd,
        _ => ImbueType.None,
    };

    /// <summary>Build a reagent-diff for a preferred reagent name. Public for testing.</summary>
    public static IReadOnlyList<ImbueReagentPlanEntry> DiffReagent(
        string reagentName, int needed, IReadOnlyDictionary<string, int> stash)
    {
        stash.TryGetValue(reagentName, out var have);
        var missing = needed - have;
        if (missing <= 0) return Array.Empty<ImbueReagentPlanEntry>();
        return new[]
        {
            new ImbueReagentPlanEntry(reagentName, missing, ResolveReagentSource(reagentName)),
        };
    }

    /// <summary>
    /// Select the best gem recipe for <paramref name="type"/> that the player
    /// can actually execute (skill gate + reagent availability). Returns null
    /// if no gem recipe fits — caller should fall back to taper path.
    /// </summary>
    public static ImbueRecipeDefinition? PickBestGemRecipe(
        ImbueType type,
        int playerCraftingSkill,
        IReadOnlyList<ImbueRecipeDefinition> allImbueRecipes,
        IReadOnlyDictionary<string, int> stash)
    {
        // Filter to "imbue" effect recipes matching the requested type that the
        // player has the skill for.
        var viable = allImbueRecipes
            .Where(r => r.Effect == "imbue" && r.ImbueType == type)
            .Where(r => r.RequiredCraftingSkill <= playerCraftingSkill)
            .ToList();
        if (viable.Count == 0) return null;

        // Prefer recipes whose reagent is already in stash — highest power first.
        var inStash = viable
            .Where(r => stash.Keys.Any(k => k.Contains(r.RequiredReagent, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(r => r.Power)
            .ToList();
        if (inStash.Count > 0) return inStash[0];

        // Otherwise pick highest-power skill-legal recipe (caller will farm it).
        return viable.OrderByDescending(r => r.Power).First();
    }

    /// <summary>
    /// Main planning entry point. The executor feeds us:
    ///  - every equipped item (id, slot, MaxImbueSlots, current imbue count,
    ///    and for the weapon: magical element),
    ///  - the player's crafting skill + primary element,
    ///  - name→quantity stash (inventory + storage, aggregated),
    ///  - content-provider's gem imbue recipes.
    /// We return the next action.
    /// </summary>
    public static AutoImbuePlan Plan(
        IReadOnlyList<EquippedSlotSnapshot> equippedSnapshots,
        MagicElement? equippedWeaponElement,
        MagicElement playerPrimaryElement,
        int playerCraftingSkill,
        IReadOnlyDictionary<string, int> stash,
        IReadOnlyList<ImbueRecipeDefinition> allImbueRecipes)
    {
        var coverages = equippedSnapshots
            .Select(s => new SlotCoverage(s.Slot, s.ImbueCount, s.MaxImbueSlots))
            .ToList();

        var party = PartyCoverage(coverages);
        var worst = PickWorstSlot(coverages);

        if (worst is null)
        {
            return new AutoImbuePlan(
                TargetItemId: null, TargetItemName: null,
                TargetSlot: EquipmentSlot.None,
                PartyCoverage: party, WorstSlotCoverage: 1.0,
                SelectedImbueType: ImbueType.None,
                SelectedRecipeId: null, SelectedReagentName: null,
                UsesGem: false, ImbuableNow: false,
                MissingReagents: Array.Empty<ImbueReagentPlanEntry>(),
                Reason: equippedSnapshots.Count == 0
                    ? "No equipped items — cannot imbue."
                    : $"All equipped slots fully imbued (coverage {party:P0}).");
        }

        var target = equippedSnapshots.First(s => s.Slot == worst.Value.Slot);

        var type = SelectImbueType(equippedWeaponElement, playerPrimaryElement, stash);
        if (type == ImbueType.None) type = ImbueType.Fire;

        // Try gem recipe first (preferred — higher power).
        var gemRecipe = PickBestGemRecipe(type, playerCraftingSkill, allImbueRecipes, stash);
        string? selectedRecipeId = null;
        string? selectedReagent = null;
        bool usesGem = false;

        if (gemRecipe is not null)
        {
            selectedRecipeId = gemRecipe.Id;
            selectedReagent = gemRecipe.RequiredReagent;
            usesGem = true;
        }
        else
        {
            // Fall back to taper reagent — still must respect skill gate implicitly
            // (taper path has a skill-scaled success chance but no hard gate other
            // than ImbueService, so we use it as last resort).
            selectedReagent = PreferredTaperByType.GetValueOrDefault(type);
            if (selectedReagent is null)
            {
                return new AutoImbuePlan(
                    TargetItemId: target.ItemId, TargetItemName: target.ItemName,
                    TargetSlot: target.Slot,
                    PartyCoverage: party, WorstSlotCoverage: worst.Value.Ratio,
                    SelectedImbueType: type,
                    SelectedRecipeId: null, SelectedReagentName: null,
                    UsesGem: false, ImbuableNow: false,
                    MissingReagents: Array.Empty<ImbueReagentPlanEntry>(),
                    Reason: $"No gem recipe and no taper mapping for {type}; skill={playerCraftingSkill}.");
            }
        }

        // Skill gate (gem path) — should already be enforced by PickBestGemRecipe,
        // but re-check defensively so the reason string is precise.
        if (usesGem && gemRecipe!.RequiredCraftingSkill > playerCraftingSkill)
        {
            return new AutoImbuePlan(
                TargetItemId: target.ItemId, TargetItemName: target.ItemName,
                TargetSlot: target.Slot,
                PartyCoverage: party, WorstSlotCoverage: worst.Value.Ratio,
                SelectedImbueType: type,
                SelectedRecipeId: gemRecipe.Id, SelectedReagentName: gemRecipe.RequiredReagent,
                UsesGem: true, ImbuableNow: false,
                MissingReagents: Array.Empty<ImbueReagentPlanEntry>(),
                Reason: $"Crafting skill {playerCraftingSkill} < {gemRecipe.RequiredCraftingSkill} for {gemRecipe.Id}.");
        }

        // Reagent availability check — look up by best-effort name match.
        int have = 0;
        foreach (var kv in stash)
        {
            if (kv.Key.Contains(selectedReagent!, StringComparison.OrdinalIgnoreCase))
            {
                have += kv.Value;
            }
        }

        var missing = have >= 1
            ? (IReadOnlyList<ImbueReagentPlanEntry>)Array.Empty<ImbueReagentPlanEntry>()
            : new[] { new ImbueReagentPlanEntry(
                selectedReagent!, 1, ResolveReagentSource(selectedReagent!)) };

        var imbuableNow = missing.Count == 0;
        var reason = imbuableNow
            ? $"Imbue {target.ItemName} (slot {target.Slot}, {target.ImbueCount}/{target.MaxImbueSlots}) with {type} via {(usesGem ? gemRecipe!.Id : selectedReagent)}."
            : $"Need 1×{selectedReagent} from {ResolveReagentSource(selectedReagent!).ZoneHint} to imbue {target.ItemName} with {type}.";

        return new AutoImbuePlan(
            TargetItemId: target.ItemId, TargetItemName: target.ItemName,
            TargetSlot: target.Slot,
            PartyCoverage: party, WorstSlotCoverage: worst.Value.Ratio,
            SelectedImbueType: type,
            SelectedRecipeId: selectedRecipeId, SelectedReagentName: selectedReagent,
            UsesGem: usesGem, ImbuableNow: imbuableNow,
            MissingReagents: missing, Reason: reason);
    }
}

/// <summary>
/// Snapshot of one equipped slot. Executor builds this from live state, the
/// planner consumes it. Keep flat so it serialises trivially for logs/tests.
/// </summary>
public sealed record EquippedSlotSnapshot(
    EquipmentSlot Slot,
    Guid ItemId,
    string ItemName,
    int MaxImbueSlots,
    int ImbueCount);
