using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Application.Services;

/// <summary>
/// Source of a single ingredient when it must be farmed/harvested/smelted.
/// </summary>
/// <param name="Kind">'harvest' (biome loot drop), 'loot' (common-materials drop), 'smelt' (craft from sub-recipe).</param>
/// <param name="ZoneHint">Zone name hint (for biome-keyed harvests), or null.</param>
/// <param name="Biome">Biome id (matches <c>DropPool.Id</c> suffix) for 'harvest', else null.</param>
/// <param name="SubRecipeId">Recipe id for 'smelt' (e.g. crafting Sinew from Bone Fragment), else null.</param>
public sealed record IngredientSource(
    string Kind,
    string? ZoneHint,
    string? Biome,
    string? SubRecipeId);

/// <summary>
/// One line of the farming-plan: need <paramref name="Quantity"/> more of <paramref name="ItemName"/>,
/// sourced per <paramref name="Source"/>.
/// </summary>
public sealed record IngredientPlanEntry(
    string ItemName,
    int Quantity,
    IngredientSource Source);

/// <summary>
/// Output of <see cref="AutoCraftPlanner.Plan"/>. Either <see cref="CraftableNow"/> = true (all
/// ingredients are in inventory+storage, dispatch directly to CraftCommandHandler) or false
/// (at least one <see cref="MissingIngredients"/> entry — dispatch to auto-farm to close the gap).
/// </summary>
public sealed record AutoCraftPlan(
    RecipeDefinition? TargetRecipe,
    EquipmentSlot TargetSlot,
    int ExpectedWorkmanship,
    int CurrentSlotWorkmanship,
    double Score,
    bool CraftableNow,
    IReadOnlyList<IngredientPlanEntry> MissingIngredients,
    string Reason);

/// <summary>
/// Pure planning logic for the auto-craft sub-mode. No DI, no I/O; the executor
/// feeds it current state and it returns a decision.
///
/// Scoring formula (see <see cref="ScoreRecipe"/>):
///   score = (expectedWorkmanshipGain ^ 2) / (totalIngredientUnits + max(0, skillGap) + 1)
///
/// Why squared gain: a +3 workmanship jump is worth much more than three +1 jumps
/// because Workmanship gates imbue-slot count (MaxImbueSlots steps at 3/5/7/9).
/// Why divide by ingredient units: favour cheap wins first — a 4-unit recipe that
/// returns Wm 5 beats a 12-unit recipe that returns Wm 6 when the player is
/// starving for components.
/// Why add skillGap: recipes you can't even attempt yet should be discounted
/// (we still rank them so they appear as soft goals, but they sink below
/// anything currently craftable).
///
/// Slot resolution: for equipment recipes, result item name maps to an
/// <see cref="EquipmentSlot"/> via <see cref="ResolveSlotForItem"/>. Non-equipment
/// recipes (Leather from Beast Hide, Sinew from Bone Fragment, Healing Draught)
/// are filtered out — this planner only chases gear upgrades.
/// </summary>
public static class AutoCraftPlanner
{
    // Canonical item-name → slot map. Mirrors StartupSeeder.CanonicalItemSlots,
    // extended to every equipment result in content/recipes.json. If an item
    // name isn't here, ResolveSlotForItem falls back to (Accessory | None).
    private static readonly IReadOnlyDictionary<string, EquipmentSlot> NameToSlot =
        new Dictionary<string, EquipmentSlot>(StringComparer.OrdinalIgnoreCase)
        {
            // Melee weapons
            ["Iron Sword"]          = EquipmentSlot.MeleeWeapon,
            ["Iron Dagger"]         = EquipmentSlot.MeleeWeapon,
            ["Stone Axe"]           = EquipmentSlot.MeleeWeapon,
            ["Battle Hammer"]       = EquipmentSlot.MeleeWeapon,
            ["War Pick"]            = EquipmentSlot.MeleeWeapon,
            ["Mithril Blade"]       = EquipmentSlot.MeleeWeapon,
            // Ranged
            ["Thornwood Bow"]       = EquipmentSlot.RangedWeapon,
            ["Hunter's Crossbow"]   = EquipmentSlot.RangedWeapon,
            ["Sling"]               = EquipmentSlot.RangedWeapon,
            // Focus
            ["Oak Wand"]            = EquipmentSlot.Focus,
            ["Crystal Focus"]       = EquipmentSlot.Focus,
            ["Ashwood Staff"]       = EquipmentSlot.Focus,
            // Head
            ["Leather Cap"]         = EquipmentSlot.Head,
            ["Scale Coif"]          = EquipmentSlot.Head,
            ["Iron Helm"]           = EquipmentSlot.Head,
            ["Fur Hood"]            = EquipmentSlot.Head,
            // Chest
            ["Leather Vest"]        = EquipmentSlot.Chest,
            ["Chain Shirt"]         = EquipmentSlot.Chest,
            ["Padded Gambeson"]     = EquipmentSlot.Chest,
            ["Leather Armor"]       = EquipmentSlot.Chest,
            ["Iron Buckler"]        = EquipmentSlot.Chest,
            // Legs
            ["Leather Leggings"]    = EquipmentSlot.Legs,
            ["Iron Greaves"]        = EquipmentSlot.Legs,
            ["Padded Trousers"]     = EquipmentSlot.Legs,
            ["Chain Leggings"]      = EquipmentSlot.Legs,
            ["Mithril Greaves"]     = EquipmentSlot.Legs,
            // Hands
            ["Leather Gloves"]      = EquipmentSlot.Hands,
            ["Iron Vambraces"]      = EquipmentSlot.Hands,
            ["Iron Gauntlets"]      = EquipmentSlot.Hands,
            ["Wrapped Handguards"]  = EquipmentSlot.Hands,
            ["Mithril Vambraces"]   = EquipmentSlot.Hands,
            // Feet
            ["Leather Boots"]       = EquipmentSlot.Feet,
            ["Iron Sabatons"]       = EquipmentSlot.Feet,
            ["Traveler's Sandals"]  = EquipmentSlot.Feet,
            ["Thornwood Treads"]    = EquipmentSlot.Feet,
            ["Mithril Boots"]       = EquipmentSlot.Feet,
            // Accessory
            ["Bone Ring"]           = EquipmentSlot.Accessory,
            ["Silver Amulet"]       = EquipmentSlot.Accessory,
            ["Wyrd Charm"]          = EquipmentSlot.Accessory,
        };

    // Known harvestable material → (biome id, representative zone hint).
    // Built by cross-referencing content/loot-tables.json dropPools. When a
    // material appears in multiple pools we pick the most specific biome;
    // generic fall-through is "common-materials" / "Starting Road".
    private static readonly IReadOnlyDictionary<string, (string Biome, string ZoneHint)> MaterialSource =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            // common-materials (drops in every biome, but plains zone is safest for a L1 character)
            ["Iron Ore"]       = ("common-materials", "Starting Road"),
            ["Wood"]           = ("common-materials", "Starting Road"),
            ["Leather"]        = ("common-materials", "Starting Road"),
            ["Sinew"]          = ("common-materials", "Starting Road"),
            ["Bone Fragment"]  = ("common-materials", "Starting Road"),
            ["Stone"]          = ("common-materials", "Starting Road"),
            // biome-forest
            ["Beast Hide"]     = ("biome-forest", "The Thornwood"),
            ["Beast Leather"]  = ("biome-forest", "The Thornwood"),
            ["Spider Silk"]    = ("biome-forest", "The Thornwood"),
            ["Feather"]        = ("biome-forest", "The Thornwood"),
            // biome-mountain
            ["Mithril Ore"]    = ("biome-mountain", "Caervorn Highlands"),
            ["Diamond Shard"]  = ("biome-mountain", "Caervorn Highlands"),
            ["Mountain Herb"]  = ("biome-mountain", "Caervorn Highlands"),
            // biome-plains
            ["Cotton Fiber"]   = ("biome-plains", "Portmere (Compact)"),
            ["Horse Hair"]     = ("biome-plains", "Portmere (Compact)"),
            // Reagents / herbs — generic
            ["Herbs"]          = ("common-materials", "Starting Road"),
        };

    /// <summary>Public for testing.</summary>
    public static EquipmentSlot ResolveSlotForItem(string itemName, ItemCategory category)
    {
        if (NameToSlot.TryGetValue(itemName, out var slot)) return slot;
        // Unknown equipment falls back to Accessory for Weapon/Armor, None for Component/Reagent/Consumable.
        return category switch
        {
            ItemCategory.Weapon    => EquipmentSlot.MeleeWeapon,
            ItemCategory.Armor     => EquipmentSlot.Accessory,
            ItemCategory.Accessory => EquipmentSlot.Accessory,
            _                      => EquipmentSlot.None,
        };
    }

    /// <summary>
    /// Score a candidate recipe. Higher is better. Equipment-producing
    /// recipes only — callers must filter non-gear recipes out first.
    /// </summary>
    public static double ScoreRecipe(
        RecipeDefinition recipe,
        int currentSlotWorkmanship,
        int playerCraftingSkill)
    {
        // Expected Wm from this recipe — use midpoint of the Wm band.
        var expectedWm = (recipe.BaseWorkmanshipMin + recipe.BaseWorkmanshipMax) / 2.0;
        var gain = expectedWm - currentSlotWorkmanship;
        // Downgrades score negative (we'd never want to equip them, but rank for the order-of-recommendation).
        var gainSq = gain * Math.Abs(gain); // preserves sign
        var units  = Math.Max(1, recipe.Ingredients.Sum(i => i.BaseQuantity));
        var skillGap = Math.Max(0, recipe.RequiredCraftingSkill - playerCraftingSkill);
        return gainSq / (units + skillGap + 1.0);
    }

    /// <summary>
    /// Build an ingredient-diff for <paramref name="recipe"/> given current stash.
    /// </summary>
    public static IReadOnlyList<IngredientPlanEntry> DiffIngredients(
        RecipeDefinition recipe,
        IReadOnlyDictionary<string, int> stash)
    {
        var missing = new List<IngredientPlanEntry>();
        foreach (var ing in recipe.Ingredients)
        {
            stash.TryGetValue(ing.Name, out var have);
            var need = ing.BaseQuantity - have;
            if (need <= 0) continue;
            var src = ResolveSource(ing.Name);
            missing.Add(new IngredientPlanEntry(ing.Name, need, src));
        }
        return missing;
    }

    /// <summary>Resolve where a given ingredient comes from. Public for testing.</summary>
    public static IngredientSource ResolveSource(string ingredientName)
    {
        if (MaterialSource.TryGetValue(ingredientName, out var m))
        {
            var kind = m.Biome == "common-materials" ? "loot" : "harvest";
            return new IngredientSource(kind, m.ZoneHint, m.Biome, SubRecipeId: null);
        }
        // Fallback — treat as common-materials loot from the starting zone.
        return new IngredientSource("loot", "Starting Road", "common-materials", SubRecipeId: null);
    }

    /// <summary>
    /// Main planning entry point. Returns the best-ranked recipe given current
    /// state, or a plan with <see cref="AutoCraftPlan.TargetRecipe"/> = null
    /// when no gear recipe makes sense (e.g. all equipment already beats every
    /// recipe's midpoint).
    /// </summary>
    public static AutoCraftPlan Plan(
        IReadOnlyList<RecipeDefinition> allRecipes,
        WorldId playerWorld,
        int playerCraftingSkill,
        IReadOnlyDictionary<EquipmentSlot, int> currentEquippedWorkmanship,
        IReadOnlyDictionary<string, int> stash)
    {
        // 1. Filter to equipment recipes the player can see.
        var candidates = allRecipes
            .Where(r => r.RequiredWorld == playerWorld)
            .Where(r => r.ResultCategory is ItemCategory.Weapon or ItemCategory.Armor or ItemCategory.Accessory)
            .Select(r =>
            {
                var slot = ResolveSlotForItem(r.ResultItemName, r.ResultCategory);
                var currentWm = currentEquippedWorkmanship.TryGetValue(slot, out var v) ? v : 0;
                var score = ScoreRecipe(r, currentWm, playerCraftingSkill);
                return new { Recipe = r, Slot = slot, CurrentWm = currentWm, Score = score };
            })
            // Downgrades are useless — skip recipes whose midpoint Wm ≤ current equipped Wm.
            .Where(c => (c.Recipe.BaseWorkmanshipMin + c.Recipe.BaseWorkmanshipMax) / 2.0 > c.CurrentWm)
            .ToList();

        if (candidates.Count == 0)
        {
            return new AutoCraftPlan(
                TargetRecipe: null,
                TargetSlot: EquipmentSlot.None,
                ExpectedWorkmanship: 0,
                CurrentSlotWorkmanship: 0,
                Score: 0.0,
                CraftableNow: false,
                MissingIngredients: Array.Empty<IngredientPlanEntry>(),
                Reason: "No upgrade-worthy recipes for this world at current gear tier.");
        }

        // 2. Split into craftable-now (all ingredients in stash + skill met) vs not.
        bool Craftable(RecipeDefinition r) =>
            playerCraftingSkill >= r.RequiredCraftingSkill &&
            DiffIngredients(r, stash).Count == 0;

        var nowCraftable = candidates.Where(c => Craftable(c.Recipe)).ToList();
        var chosen = (nowCraftable.Count > 0 ? nowCraftable : candidates)
            .OrderByDescending(c => c.Score)
            .First();

        var expectedWm = (chosen.Recipe.BaseWorkmanshipMin + chosen.Recipe.BaseWorkmanshipMax) / 2;
        var missing = DiffIngredients(chosen.Recipe, stash);
        var craftableNow = missing.Count == 0 && playerCraftingSkill >= chosen.Recipe.RequiredCraftingSkill;

        string reason = craftableNow
            ? $"Craft {chosen.Recipe.ResultItemName} (slot {chosen.Slot}, Wm {chosen.CurrentWm}→{expectedWm})."
            : missing.Count > 0
                ? $"Need {string.Join(", ", missing.Select(m => $"{m.Quantity}×{m.ItemName}"))} for {chosen.Recipe.ResultItemName}."
                : $"Crafting skill {playerCraftingSkill} < {chosen.Recipe.RequiredCraftingSkill} for {chosen.Recipe.ResultItemName}.";

        return new AutoCraftPlan(
            TargetRecipe: chosen.Recipe,
            TargetSlot: chosen.Slot,
            ExpectedWorkmanship: expectedWm,
            CurrentSlotWorkmanship: chosen.CurrentWm,
            Score: chosen.Score,
            CraftableNow: craftableNow,
            MissingIngredients: missing,
            Reason: reason);
    }
}
