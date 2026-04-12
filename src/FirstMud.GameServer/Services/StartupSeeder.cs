using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.Infrastructure.Data;
using FirstMud.Infrastructure.Neo4j;
using Microsoft.EntityFrameworkCore;

namespace FirstMud.GameServer.Services;

#pragma warning disable CS9113 // questGraph is reserved for future quest-prerequisite checks during seeding
public class StartupSeeder(
    GameDbContext db,
    IQuestGraphRepository questGraph,
    LoreSeeder loreSeed,
    ILogger<StartupSeeder> logger)
#pragma warning restore CS9113
{
    public async Task SeedAsync(CancellationToken ct)
    {
        await FixTrailingCommaEquippedItemsJsonAsync(ct);
        await FixMisassignedEquipmentSlotsAsync(ct);
        await FixFlatStartingStatsAsync(ct);
        await SeedDevPlayerAsync(ct);
        await SeedNeo4jLoreAsync(ct);
        await SeedAeldranZonesAsync(ct);
        await SeedStarterRecipesAsync(ct);
        await SeedHomesteadsAsync(ct);
        await SeedResourceNodesAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Data fixups
    // -------------------------------------------------------------------------

    // The EquipmentSlotOverhaul migration contained an off-by-one error in its
    // trailing-comma cleanup step (LEN-1 instead of LEN-2), which could leave
    // EquippedItemsJson values like {"1":"guid","5":"guid",} in the database.
    // This fixup repairs any such rows on every startup so that the EF Core
    // value converter never encounters malformed JSON, even on databases that
    // ran the migration before the SQL was corrected.
    private async Task FixTrailingCommaEquippedItemsJsonAsync(CancellationToken ct)
    {
        // Use raw ADO.NET because EF's ExecuteSqlRawAsync interprets braces
        // in SQL strings as format parameter placeholders, and our SQL
        // contains literal '}' characters.
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "UPDATE Players SET EquippedItemsJson = LEFT(EquippedItemsJson, LEN(EquippedItemsJson) - 2) + '}' WHERE EquippedItemsJson LIKE '%,}' AND LEN(EquippedItemsJson) > 2";
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected > 0)
                logger.LogWarning("Fixed trailing-comma JSON in EquippedItemsJson for {Count} player row(s).", affected);
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Equipment slot fixup
    // -------------------------------------------------------------------------

    // Slot mismatch fixup: corrects any persisted Item rows whose Slot field
    // does not match the canonical slot for that item's name, and repairs the
    // owning player's EquippedItems dictionary if the item is currently
    // equipped in the wrong slot.
    //
    // Covers ranged weapons (Thornwood Bow, Hunter's Crossbow, Sling) that may
    // have been persisted as MeleeWeapon due to an earlier template error.
    // Designed to be idempotent — safe to run on every startup.
    private static readonly Dictionary<string, EquipmentSlot> CanonicalItemSlots = new(StringComparer.OrdinalIgnoreCase)
    {
        // Ranged weapons
        ["Thornwood Bow"]     = EquipmentSlot.RangedWeapon,
        ["Hunter's Crossbow"] = EquipmentSlot.RangedWeapon,
        ["Sling"]             = EquipmentSlot.RangedWeapon,
        // Focus weapons
        ["Oak Wand"]          = EquipmentSlot.Focus,
        ["Crystal Focus"]     = EquipmentSlot.Focus,
        ["Ashwood Staff"]     = EquipmentSlot.Focus,
    };

    private async Task FixMisassignedEquipmentSlotsAsync(CancellationToken ct)
    {
        // Load all items whose name is in our canonical map
        var targetNames = CanonicalItemSlots.Keys.ToList();
        var mismatchedItems = await db.Items
            .Where(i => targetNames.Contains(i.Name) && i.Slot != EquipmentSlot.None)
            .ToListAsync(ct);

        var toFix = mismatchedItems
            .Where(i => CanonicalItemSlots.TryGetValue(i.Name, out var correct) && i.Slot != correct)
            .ToList();

        if (toFix.Count == 0)
        {
            logger.LogInformation("FixMisassignedEquipmentSlots: no mismatched items found.");
            return;
        }

        // For each mismatched item, correct its Slot and repair any player
        // EquippedItems dictionary that references it under the wrong key.
        var affectedItemIds = toFix.Select(i => i.Id).ToHashSet();

        // Load all players so we can inspect and repair EquippedItems
        var allPlayers = await db.Players.ToListAsync(ct);
        var playersModified = 0;

        foreach (var item in toFix)
        {
            var wrongSlot  = item.Slot;
            var rightSlot  = CanonicalItemSlots[item.Name];

            item.SetSlot(rightSlot);

            // Repair each player who has this item equipped under the wrong slot
            foreach (var player in allPlayers)
            {
                if (!player.EquippedItems.TryGetValue(wrongSlot, out var equippedId)
                    || equippedId != item.Id)
                    continue;

                // Remove the bad slot entry and place item in the correct slot.
                // If the correct slot is already occupied, bump that item to
                // inventory (unequip only — it stays in the player's item list).
                player.Unequip(wrongSlot);
                var displaced = player.Equip(rightSlot, item.Id);
                playersModified++;

                logger.LogWarning(
                    "FixMisassignedEquipmentSlots: moved {ItemName} (Id={ItemId}) from {WrongSlot} to {RightSlot} for player {PlayerId}{Displaced}.",
                    item.Name, item.Id, wrongSlot, rightSlot, player.Id,
                    displaced.HasValue ? $"; displaced item {displaced.Value} to inventory" : string.Empty);
            }

            logger.LogWarning(
                "FixMisassignedEquipmentSlots: corrected Slot on item {ItemName} (Id={ItemId}) from {WrongSlot} to {RightSlot}.",
                item.Name, item.Id, wrongSlot, rightSlot);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "FixMisassignedEquipmentSlots: corrected {ItemCount} item(s), updated {PlayerCount} player(s).",
            toFix.Count, playersModified);
    }

    // -------------------------------------------------------------------------
    // Flat stat fixup
    // -------------------------------------------------------------------------

    // One-time fixup: players created before the element-based stat system had
    // all five attributes set to exactly 10. Reassign their starting stats based
    // on their PrimaryElement so existing characters get the correct archetype.
    // Safe to run on every startup — it only touches rows where all five stats
    // are exactly 10 and the player is at level 1 (no level-up gains applied yet).
    private async Task FixFlatStartingStatsAsync(CancellationToken ct)
    {
        var flatPlayers = await db.Players
            .Where(p => p.Level == 1
                     && p.Strength == 10 && p.Agility == 10
                     && p.Intellect == 10 && p.Fortitude == 10
                     && p.Speed == 10)
            .ToListAsync(ct);

        if (flatPlayers.Count == 0)
        {
            logger.LogInformation("FixFlatStartingStats: no players with uniform stats found.");
            return;
        }

        foreach (var player in flatPlayers)
        {
            player.ReassignArchetypeStats();
            logger.LogWarning(
                "FixFlatStartingStats: reassigned stats for player {Name} ({Id}) as {Element} archetype.",
                player.Name, player.Id, player.PrimaryElement);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("FixFlatStartingStats: updated {Count} player(s).", flatPlayers.Count);
    }

    // -------------------------------------------------------------------------
    // Dev player
    // -------------------------------------------------------------------------

    private async Task SeedDevPlayerAsync(CancellationToken ct)
    {
        if (!await db.Players.AnyAsync(ct))
        {
            var player = Player.Create("Kira Ashwood", craftingSeed: 42);
            await db.Players.AddAsync(player, ct);
            await db.SaveChangesAsync(ct);
            logger.LogInformation("Dev player seeded: Id={PlayerId} Name={Name}", player.Id, player.Name);
        }
        else
        {
            logger.LogInformation("Players table already has data — skipping dev player seed.");
        }

        // Dev convenience: teleport any player sitting off the Starting
        // Road back onto it so tile feedback works immediately on first
        // load. Harmless in dev since the seeder runs on every boot.
        var (startX, startY) = ZoneGridLayout.StartingRoad;
        var strays = await db.Players
            .Where(p => p.Position.X != startX || p.Position.Y != startY)
            .ToListAsync(ct);
        foreach (var p in strays)
        {
            p.Move(new Position(p.Position.World, p.Position.ZoneId, startX, startY));
        }
        if (strays.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation(
                "Repositioned {Count} dev player(s) to Starting Road ({X},{Y}).",
                strays.Count, startX, startY);
        }
    }

    // -------------------------------------------------------------------------
    // Neo4j lore
    // -------------------------------------------------------------------------

    private async Task SeedNeo4jLoreAsync(CancellationToken ct)
    {
        try
        {
            await loreSeed.SeedAsync(ct);
            logger.LogInformation("Neo4j lore seeded successfully.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Neo4j lore seeding failed — continuing without Neo4j data.");
        }
    }

    // -------------------------------------------------------------------------
    // Aeldran zones
    // -------------------------------------------------------------------------

    private async Task SeedAeldranZonesAsync(CancellationToken ct)
    {
        if (await db.Zones.AnyAsync(ct))
        {
            logger.LogInformation("Zones table already has data — skipping zone seed.");
            return;
        }

        var zones = new[]
        {
            Zone.Create(WorldId.Aeldran, 1, "Caervorn Highlands",
                "The high moorland domains of House Caervorn, swept by cold winds and watched by stone keeps.",
                "^", dangerLevel: 4),
            Zone.Create(WorldId.Aeldran, 2, "The Thornwood",
                "A dense and ancient forest where the Thornwood Covens weave their arts into the living wood.",
                "#", dangerLevel: 3),
            Zone.Create(WorldId.Aeldran, 3, "Portmere (Compact)",
                "The trading city of the Emerald Compact, where coin and contract govern more than steel.",
                "C", dangerLevel: 1),
            Zone.Create(WorldId.Aeldran, 4, "Gravenmarsh",
                "Boggy lowlands east of Gravenhold, haunted by old things that predate the Compact.",
                ".", dangerLevel: 3),
            Zone.Create(WorldId.Aeldran, 5, "The Drowned Coast",
                "A jagged shoreline where the Fairgean sometimes surface, and the tides follow no natural pattern.",
                "~", dangerLevel: 5),
            Zone.Create(WorldId.Aeldran, 6, "The Ashen Reach",
                "Scorched flatlands that have not recovered since the Ardweld's collapse. Something still moves here.",
                "*", dangerLevel: 8),
            Zone.Create(WorldId.Aeldran, 7, "Starting Road",
                "The long road south from the Highlands, where every rider begins their first contract.",
                ".", dangerLevel: 2, isPortalZone: false),
            Zone.Create(WorldId.Aeldran, 8, "Gravenhold",
                "The Gravenguard's fortified city, carved into the cliffside above the marsh.",
                "!", dangerLevel: 2),
            Zone.Create(WorldId.Aeldran, 9, "The Maw Borderlands",
                "The unstable frontier where Aeldran begins to fray at the edges, and the Wyrd leaks through.",
                "M", dangerLevel: 6),
        };

        await db.Zones.AddRangeAsync(zones, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} Aeldran zones.", zones.Length);
    }

    // -------------------------------------------------------------------------
    // Starter recipes
    // -------------------------------------------------------------------------

    private async Task SeedStarterRecipesAsync(CancellationToken ct)
    {
        if (await db.Recipes.AnyAsync(ct))
        {
            logger.LogInformation("Recipes table already has data — skipping recipe seed.");
            return;
        }

        var recipes = new[]
        {
            Recipe.Create(
                recipeId: "IRON_SWORD_001",
                name: "Iron Sword",
                ingredients:
                [
                    RecipeIngredient.Create(ItemCategory.Component, "Iron Ore", 3),
                    RecipeIngredient.Create(ItemCategory.Component, "Leather Strip", 1),
                ],
                resultCategory: ItemCategory.Weapon,
                resultItemName: "Iron Sword",
                baseWorkmanshipMin: 2,
                baseWorkmanshipMax: 6,
                requiredTaperType: null,
                requiredWorld: WorldId.Aeldran,
                requiredCraftingSkill: 1,
                isDiscoverable: false),

            Recipe.Create(
                recipeId: "LEATHER_ARMOR_001",
                name: "Leather Armor",
                ingredients:
                [
                    RecipeIngredient.Create(ItemCategory.Component, "Beast Hide", 4),
                    RecipeIngredient.Create(ItemCategory.Component, "Sinew", 2),
                ],
                resultCategory: ItemCategory.Armor,
                resultItemName: "Leather Armor",
                baseWorkmanshipMin: 2,
                baseWorkmanshipMax: 5,
                requiredTaperType: null,
                requiredWorld: WorldId.Aeldran,
                requiredCraftingSkill: 1,
                isDiscoverable: false),

            Recipe.Create(
                recipeId: "HEALING_DRAUGHT_001",
                name: "Healing Draught",
                ingredients:
                [
                    RecipeIngredient.Create(ItemCategory.Reagent, "Thornwood Herb", 2),
                    RecipeIngredient.Create(ItemCategory.Reagent, "Pure Water", 1),
                ],
                resultCategory: ItemCategory.Consumable,
                resultItemName: "Healing Draught",
                baseWorkmanshipMin: 3,
                baseWorkmanshipMax: 7,
                requiredTaperType: null,
                requiredWorld: WorldId.Aeldran,
                requiredCraftingSkill: 1,
                isDiscoverable: false),

            Recipe.Create(
                recipeId: "FOCUS_STONE_001",
                name: "Rough Focus Stone",
                ingredients:
                [
                    RecipeIngredient.Create(ItemCategory.Component, "Dravenite Dust", 5),
                    RecipeIngredient.Create(ItemCategory.Component, "Iron Wire", 1),
                ],
                resultCategory: ItemCategory.Accessory,
                resultItemName: "Rough Focus Stone",
                baseWorkmanshipMin: 2,
                baseWorkmanshipMax: 5,
                requiredTaperType: null,
                requiredWorld: WorldId.Aeldran,
                requiredCraftingSkill: 2,
                isDiscoverable: false),

            Recipe.Create(
                recipeId: "TAPER_SHAPING_001",
                name: "Shaping Taper",
                ingredients:
                [
                    RecipeIngredient.Create(ItemCategory.Component, "Wildfolk Essence", 1),
                    RecipeIngredient.Create(ItemCategory.Reagent, "Candle Wax", 2),
                ],
                resultCategory: ItemCategory.Reagent,
                resultItemName: "Shaping Taper",
                baseWorkmanshipMin: 4,
                baseWorkmanshipMax: 8,
                requiredTaperType: null,
                requiredWorld: WorldId.Aeldran,
                requiredCraftingSkill: 3,
                isDiscoverable: false),
        };

        await db.Recipes.AddRangeAsync(recipes, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} starter recipes.", recipes.Length);
    }

    // -------------------------------------------------------------------------
    // Homesteads — one per player
    // -------------------------------------------------------------------------

    private async Task SeedHomesteadsAsync(CancellationToken ct)
    {
        var players = await db.Players.ToListAsync(ct);
        foreach (var player in players)
        {
            var exists = await db.Homesteads.AnyAsync(h => h.PlayerId == player.Id, ct);
            if (!exists)
            {
                var homestead = Homestead.Create(player.Id, $"{player.Name}'s Homestead");
                await db.Homesteads.AddAsync(homestead, ct);
                logger.LogInformation("Created homestead for player {Name} ({Id})", player.Name, player.Id);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Resource nodes — 2-3 per zone
    // -------------------------------------------------------------------------

    private async Task SeedResourceNodesAsync(CancellationToken ct)
    {
        if (await db.ResourceNodes.AnyAsync(ct))
        {
            logger.LogInformation("ResourceNodes already seeded — skipping.");
            return;
        }

        var zones = await db.Zones.ToListAsync(ct);
        var nodes = new List<ResourceNode>();

        // Assign resource types by zone name/danger
        foreach (var zone in zones)
        {
            var (type1, type2) = zone.Name switch
            {
                "Caervorn Highlands"   => (ResourceType.Stone, ResourceType.Herbs),
                "The Thornwood"        => (ResourceType.Wood, ResourceType.Herbs),
                "Portmere (Compact)"   => (ResourceType.Metal, ResourceType.Sand),
                "Gravenmarsh"          => (ResourceType.Herbs, ResourceType.Stone),
                "The Drowned Coast"    => (ResourceType.Sand, ResourceType.Stone),
                "The Ashen Reach"      => (ResourceType.Metal, ResourceType.Stone),
                "Starting Road"        => (ResourceType.Wood, ResourceType.Herbs),
                "Gravenhold"           => (ResourceType.Metal, ResourceType.Stone),
                "The Maw Borderlands"  => (ResourceType.Metal, ResourceType.Herbs),
                _                      => (ResourceType.Wood, ResourceType.Stone),
            };

            nodes.Add(ResourceNode.Create(zone.Id, type1, maxYield: 20, regenerationRate: 2));
            nodes.Add(ResourceNode.Create(zone.Id, type2, maxYield: 15, regenerationRate: 1));

            // Third node for high-danger zones
            if (zone.DangerLevel >= 5)
                nodes.Add(ResourceNode.Create(zone.Id, ResourceType.Metal, maxYield: 10, regenerationRate: 1));
        }

        await db.ResourceNodes.AddRangeAsync(nodes, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} resource nodes across {ZoneCount} zones.", nodes.Count, zones.Count);
    }
}
