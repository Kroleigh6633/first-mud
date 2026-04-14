using FirstMud.Application.Content;
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
    IContentProvider content,
    Neo4jDriverWrapper neo4j,
    ILogger<StartupSeeder> logger)
#pragma warning restore CS9113
{
    public async Task SeedAsync(CancellationToken ct)
    {
        await FixTrailingCommaEquippedItemsJsonAsync(ct);
        await FixMisassignedEquipmentSlotsAsync(ct);
        await FixFlatStartingStatsAsync(ct);
        await FixDefaultInventorySlotsAsync(ct);
        await FixInflatedCraftingSkillAsync(ct);
        await RenameAndMergeLegacyMaterialsAsync(ct);
        await MergeDuplicateStorageStacksAsync(ct);
        await SeedDevPlayerAsync(ct);
        await SeedNeo4jLoreAsync(ct);
        await SeedAeldranZonesAsync(ct);
        await SeedStarterRecipesAsync(ct);
        await SeedHomesteadsAsync(ct);
        await SeedResourceNodesAsync(ct);
        await MigrateHutAssignmentsToHousingAsync(ct);
        await FixIdleCompanionsAsync(ct);
        await PurgeStaleProcgenQuestsAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Purge stale procedurally-generated quests
    // -------------------------------------------------------------------------
    //
    // Two classes of stale DM-generated quest exist in the Neo4j graph after
    // the content-layer fixes landed:
    //
    //   1. Escort / deliver / protect quests from before the TEMPLATE_BLOCKLIST
    //      (commit d9f3c96). These carrier flows have no pickup mechanic and
    //      can never complete.
    //
    //   2. Gather quests whose title references a generic noun phrase
    //      ("healing herbs", "fungal spore", "Thornwood resin") rather than a
    //      real in-game item. The completion check regex-extracts the noun
    //      and fails to match inventory.
    //
    // The safe, reliable fix is to detach every active DM-generated quest and
    // delete it. The DungeonMasterService will regenerate fresh quests on its
    // normal tick using the corrected templates + typed targetItemNames.
    //
    // Idempotent — a boot with no stale quests is a no-op.
    private async Task PurgeStaleProcgenQuestsAsync(CancellationToken ct)
    {
        try
        {
            // Count first so we can log how many we're about to remove.
            var purged = await neo4j.ExecuteReadAsync(async tx =>
            {
                var cursor = await tx.RunAsync(
                    """
                    MATCH (q:Quest {isDmGenerated: true})
                    WHERE NOT ()-[:COMPLETED]->(q)
                    RETURN count(q) AS purgedCount
                    """);
                await cursor.FetchAsync();
                return (int)(long)cursor.Current["purgedCount"];
            }, ct);

            if (purged > 0)
            {
                await neo4j.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync(
                        """
                        MATCH (q:Quest {isDmGenerated: true})
                        WHERE NOT ()-[:COMPLETED]->(q)
                        DETACH DELETE q
                        """);
                }, ct);
            }

            if (purged > 0)
            {
                logger.LogInformation(
                    "StartupSeeder: purged {Count} stale procgen quest(s) with pre-fix titles. " +
                    "DungeonMasterService will regenerate fresh ones on its next quest tick.",
                    purged);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StartupSeeder: PurgeStaleProcgenQuestsAsync failed; continuing.");
        }
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
        ["Thornwood Bow"]       = EquipmentSlot.RangedWeapon,
        ["Hunter's Crossbow"]   = EquipmentSlot.RangedWeapon,
        ["Sling"]               = EquipmentSlot.RangedWeapon,
        // Focus weapons
        ["Oak Wand"]            = EquipmentSlot.Focus,
        ["Crystal Focus"]       = EquipmentSlot.Focus,
        ["Ashwood Staff"]       = EquipmentSlot.Focus,
        // Head armor
        ["Leather Cap"]         = EquipmentSlot.Head,
        ["Scale Coif"]          = EquipmentSlot.Head,
        ["Iron Helm"]           = EquipmentSlot.Head,
        ["Fur Hood"]            = EquipmentSlot.Head,
        // Chest armor
        ["Leather Vest"]        = EquipmentSlot.Chest,
        ["Iron Buckler"]        = EquipmentSlot.Chest,
        ["Padded Gambeson"]     = EquipmentSlot.Chest,
        ["Chain Shirt"]         = EquipmentSlot.Chest,
        // Legs armor
        ["Leather Leggings"]    = EquipmentSlot.Legs,
        ["Iron Greaves"]        = EquipmentSlot.Legs,
        ["Padded Trousers"]     = EquipmentSlot.Legs,
        ["Chain Leggings"]      = EquipmentSlot.Legs,
        ["Mithril Greaves"]     = EquipmentSlot.Legs,
        // Hands armor
        ["Leather Gloves"]      = EquipmentSlot.Hands,
        ["Iron Vambraces"]      = EquipmentSlot.Hands,
        ["Iron Gauntlets"]      = EquipmentSlot.Hands,
        ["Wrapped Handguards"]  = EquipmentSlot.Hands,
        ["Mithril Vambraces"]   = EquipmentSlot.Hands,
        // Feet armor
        ["Leather Boots"]       = EquipmentSlot.Feet,
        ["Iron Sabatons"]       = EquipmentSlot.Feet,
        ["Traveler's Sandals"]  = EquipmentSlot.Feet,
        ["Thornwood Treads"]    = EquipmentSlot.Feet,
        ["Mithril Boots"]       = EquipmentSlot.Feet,
        // Accessories
        ["Bone Ring"]           = EquipmentSlot.Accessory,
        ["Silver Amulet"]       = EquipmentSlot.Accessory,
        ["Wyrd Charm"]          = EquipmentSlot.Accessory,
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
    // Inventory slot fixup
    // -------------------------------------------------------------------------

    // Players created before the 40-slot default were stored with MaxInventorySlots=20.
    // Bump those rows to 40 on every startup so existing characters get the larger cap.
    // Safe to run on every startup — only touches rows where the value is exactly 20.
    private async Task FixDefaultInventorySlotsAsync(CancellationToken ct)
    {
        var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(ct);
        try
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE Players SET MaxInventorySlots = 40 WHERE MaxInventorySlots = 20";
            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected > 0)
                logger.LogWarning("FixDefaultInventorySlots: updated MaxInventorySlots to 40 for {Count} player(s).", affected);
            else
                logger.LogInformation("FixDefaultInventorySlots: no players with MaxInventorySlots=20 found.");
        }
        finally
        {
            await conn.CloseAsync();
        }
    }

    // -------------------------------------------------------------------------
    // Inflated crafting skill fixup
    // -------------------------------------------------------------------------

    // A bug in the smelting system previously granted CraftingSkill XP on every
    // smelt operation.  Reset any player whose CraftingSkill exceeds 10 back to 1
    // so the skill ladder is meaningful again.  Safe to run on every startup —
    // once the affected rows are reset they fall below the threshold and are skipped.
    private async Task FixInflatedCraftingSkillAsync(CancellationToken ct)
    {
        var inflated = await db.Players
            .Where(p => p.CraftingSkill > 10)
            .ToListAsync(ct);

        if (inflated.Count == 0)
        {
            logger.LogInformation("FixInflatedCraftingSkill: no players with CraftingSkill > 10 found.");
            return;
        }

        foreach (var p in inflated)
        {
            logger.LogWarning(
                "FixInflatedCraftingSkill: resetting CraftingSkill from {Old} to 1 for player {Name} ({Id}).",
                p.CraftingSkill, p.Name, p.Id);
            p.ResetCraftingSkill(1);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("FixInflatedCraftingSkill: reset {Count} player(s).", inflated.Count);
    }

    // -------------------------------------------------------------------------
    // Legacy material rename + merge fixup
    // -------------------------------------------------------------------------

    // Renames "Wood Bundle" → "Wood" and "Herbs Bundle" → "Herbs" everywhere
    // (player inventory + homestead storage).  After renaming, any duplicate
    // stacks with the same canonical name in the same location are collapsed.
    // "Metal" is intentionally left as-is — it remains a smeltable raw material.
    // Safe to run on every startup — idempotent.
    private static readonly Dictionary<string, string> LegacyMaterialRenames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Wood Bundle"]  = "Wood",
        ["Herbs Bundle"] = "Sage",
        // Herb tiering migration (2026-04-13): generic "Herbs" splits into
        // tier-1 named herbs. Existing player stock migrates to "Sage" (the
        // most-common tier-1). Legacy biome-specific names map onto their
        // canonical tier-2 equivalents where they exist, otherwise Sage.
        ["Herbs"]         = "Sage",
        ["Mountain Herbs"] = "Fire Moss",
        ["Mountain Herb"]  = "Fire Moss",
        ["Forest Herbs"]  = "Nightshade",
        ["Meadow Herbs"]  = "Sage",
        ["Wild Herbs"]    = "Lavender",
        ["Swamp Herbs"]   = "Bogweed",
        ["Wyrd Bloom"]    = "Thornroot",
    };

    private async Task RenameAndMergeLegacyMaterialsAsync(CancellationToken ct)
    {
        var oldNames = LegacyMaterialRenames.Keys.ToList();
        var legacyItems = await db.Items
            .Where(i => oldNames.Contains(i.Name))
            .ToListAsync(ct);

        if (legacyItems.Count == 0)
        {
            logger.LogInformation("RenameAndMergeLegacyMaterials: no legacy material names found.");
            return;
        }

        var renamedCount = 0;
        foreach (var item in legacyItems)
        {
            if (!LegacyMaterialRenames.TryGetValue(item.Name, out var newName)) continue;
            item.Rename(newName);
            renamedCount++;
            logger.LogWarning(
                "RenameAndMergeLegacyMaterials: renamed item {Id} '{OldName}' → '{NewName}'.",
                item.Id, item.Name, newName);
        }

        if (renamedCount > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "RenameAndMergeLegacyMaterials: renamed {Count} item(s). MergeDuplicateStorageStacks will consolidate them.",
            renamedCount);
    }

    // -------------------------------------------------------------------------
    // Storage stack consolidation fixup
    // -------------------------------------------------------------------------

    // Merges duplicate storage stacks that accumulated before the deposit handler
    // started enforcing stack-merge logic. Any two (or more) Items in the same
    // homestead storage vault that share the same Name, Category, and stackable flag
    // are collapsed into a single row whose Quantity is the sum of all duplicates.
    // The extra Item rows are deleted. Safe to run on every startup — idempotent.
    private async Task MergeDuplicateStorageStacksAsync(CancellationToken ct)
    {
        var homesteads = await db.Homesteads.ToListAsync(ct);
        var totalMerged = 0;

        foreach (var homestead in homesteads)
        {
            var storageEntries = await db.HomesteadStorageItems
                .Where(s => s.HomesteadId == homestead.Id)
                .ToListAsync(ct);

            if (storageEntries.Count == 0) continue;

            var itemIds = storageEntries.Select(s => s.ItemId).ToList();
            var items = await db.Items
                .Where(i => itemIds.Contains(i.Id))
                .ToListAsync(ct);

            // Group stackable items by Name + Category
            var groups = items
                .Where(i => i.IsStackable)
                .GroupBy(i => (i.Name, i.Category))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in groups)
            {
                var ordered = group.OrderBy(i => i.Id).ToList(); // deterministic primary pick
                var primary = ordered[0];
                var duplicates = ordered.Skip(1).ToList();

                // Sum all duplicate quantities into the primary
                var extraQty = duplicates.Sum(d => d.Quantity);
                primary.AddQuantity(extraQty);
                db.Items.Update(primary);

                // Remove the HomesteadStorageItem rows for the duplicates
                foreach (var dup in duplicates)
                {
                    var entry = storageEntries.FirstOrDefault(s => s.ItemId == dup.Id);
                    if (entry is not null)
                        db.HomesteadStorageItems.Remove(entry);

                    db.Items.Remove(dup);
                    totalMerged++;
                }
            }
        }

        if (totalMerged > 0)
        {
            await db.SaveChangesAsync(ct);
            logger.LogWarning(
                "MergeDuplicateStorageStacks: collapsed {Count} duplicate storage item row(s) across all homesteads.",
                totalMerged);
        }
        else
        {
            logger.LogInformation("MergeDuplicateStorageStacks: no duplicate storage stacks found.");
        }
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

        var definitions = content.AllZones();
        var zones = definitions.Select(def => Zone.Create(
            worldId: def.World,
            zoneId: def.ZoneNumber,
            name: def.Name,
            description: def.Description,
            asciiSymbol: def.AsciiSymbol,
            dangerLevel: def.DangerLevel,
            isPortalZone: def.IsPortalZone,
            portalDestination: def.PortalDestination)).ToArray();

        await db.Zones.AddRangeAsync(zones, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} Aeldran zones.", zones.Length);
    }

    // -------------------------------------------------------------------------
    // Starter recipes
    // -------------------------------------------------------------------------

    // Recipe definitions are data-driven — authored in content/recipes.json
    // and loaded via IContentProvider. Upsert pattern: missing recipes are
    // inserted; existing ones have their ingredients verified and corrected.
    // Add new entries to the JSON and they are picked up on the next startup
    // without any DB migration.
    private async Task SeedStarterRecipesAsync(CancellationToken ct)
    {
        var definitions = content.AllRecipes();

        var allDefinedIds = definitions.Select(r => r.RecipeId).ToList();
        var existingRecipes = await db.Recipes
            .Where(r => allDefinedIds.Contains(r.RecipeId))
            .ToListAsync(ct);

        var existingById = existingRecipes.ToDictionary(r => r.RecipeId);
        var toAdd        = new List<Recipe>();
        var fixCount     = 0;

        foreach (var def in definitions)
        {
            var ingredients = def.Ingredients
                .Select(i => RecipeIngredient.Create(i.Category, i.Name, i.BaseQuantity))
                .ToArray();

            if (existingById.TryGetValue(def.RecipeId, out var existing))
            {
                // Verify and correct ingredients on already-seeded rows
                var needsUpdate = existing.Ingredients.Count != ingredients.Length
                    || existing.Ingredients.Zip(ingredients).Any(p =>
                        p.First.IngredientName != p.Second.IngredientName
                        || p.First.BaseQuantity != p.Second.BaseQuantity);

                if (needsUpdate)
                {
                    existing.ReplaceIngredients(ingredients);
                    db.Recipes.Update(existing);
                    fixCount++;
                    logger.LogWarning(
                        "SeedStarterRecipes: corrected ingredients for {RecipeId} ({Name}).",
                        def.RecipeId, def.Name);
                }
            }
            else
            {
                // Recipe not yet in DB — queue for insert
                toAdd.Add(Recipe.Create(
                    recipeId:              def.RecipeId,
                    name:                  def.Name,
                    ingredients:           ingredients,
                    resultCategory:        def.ResultCategory,
                    resultItemName:        def.ResultItemName,
                    baseWorkmanshipMin:    def.BaseWorkmanshipMin,
                    baseWorkmanshipMax:    def.BaseWorkmanshipMax,
                    requiredTaperType:     def.RequiredTaperType,
                    requiredWorld:         def.RequiredWorld,
                    requiredCraftingSkill: def.RequiredCraftingSkill,
                    isDiscoverable:        def.IsDiscoverable));
            }
        }

        if (toAdd.Count > 0)
            await db.Recipes.AddRangeAsync(toAdd, ct);

        if (toAdd.Count > 0 || fixCount > 0)
            await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "SeedStarterRecipes: {Added} new recipe(s) inserted, {Fixed} ingredient set(s) corrected. Total defined: {Total}.",
            toAdd.Count, fixCount, definitions.Count);
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

        // Migrate any legacy homesteads that still have the old 50-slot default to 100
        var legacyHomesteads = db.Homesteads.Where(h => h.StorageSlots == 50);
        foreach (var h in legacyHomesteads)
        {
            h.ExpandStorage(50);  // 50 → 100
            logger.LogInformation("Upgraded homestead {Id} storage from 50 to 100 slots", h.Id);
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

        // Assign resource types by zone name/danger — driven by zones.json.
        var zoneDefsByName = content.AllZones().ToDictionary(z => z.Name, StringComparer.Ordinal);

        foreach (var zone in zones)
        {
            ResourceType type1 = ResourceType.Wood, type2 = ResourceType.Stone;
            if (zoneDefsByName.TryGetValue(zone.Name, out var def))
            {
                type1 = def.PrimaryResource ?? ResourceType.Wood;
                type2 = def.SecondaryResource ?? ResourceType.Stone;
            }

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

    // -------------------------------------------------------------------------
    // Housing migration — migrate hut AssignedCompanionId to HousingBuildingId
    // -------------------------------------------------------------------------

    // Before this change, huts used AssignedCompanionId (a single worker slot) to track occupants.
    // Now huts are housing: companions point back to the hut via their own HousingBuildingId.
    // This fixup migrates any existing data:
    //   - For each Hut with an AssignedCompanionId: set that companion's HousingBuildingId = hut.Id
    //     and clear the hut's AssignedCompanionId.
    // Safe to run repeatedly — idempotent.
    private async Task MigrateHutAssignmentsToHousingAsync(CancellationToken ct)
    {
        var hutType = (int)Domain.Enums.BuildingType.Hut;
        var hutsWithAssignees = await db.HomesteadBuildings
            .Where(b => b.Type == (Domain.Enums.BuildingType)hutType && b.AssignedCompanionIdsJson != "[]" && b.AssignedCompanionIdsJson != "")
            .ToListAsync(ct);

        if (hutsWithAssignees.Count == 0)
        {
            logger.LogInformation("MigrateHutAssignments: no legacy hut assignments found — nothing to do.");
            return;
        }

        int migrated = 0;
        foreach (var hut in hutsWithAssignees)
        {
            if (hut.WorkerCount == 0) continue;
            var companionId = hut.AssignedCompanionIds[0];
            var companion = await db.Companions.FindAsync([companionId], ct);
            if (companion is not null && companion.HousingBuildingId is null)
            {
                companion.AssignHousing(hut.Id);
                db.Companions.Update(companion);
                migrated++;
                logger.LogInformation(
                    "MigrateHutAssignments: moved {Name} ({Id}) from hut AssignedCompanionId → HousingBuildingId.",
                    companion.Name, companion.Id);
            }

            // Clear the worker slot on the hut
            hut.UnassignCompanion();
            db.HomesteadBuildings.Update(hut);
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "MigrateHutAssignments: migrated {Count} companion(s) to HousingBuildingId.", migrated);
    }

    // -------------------------------------------------------------------------
    // Idle companion fixup
    // -------------------------------------------------------------------------

    // Any companion that is not in a player's active combat party (max 3 slots)
    // AND has no assigned homestead duty is considered "forgotten idle".
    // This fixup also rotates maxed (Layer 6) active companions to homestead and
    // pulls in the lowest-bond growable replacements.
    // Safe to run on every startup — assignments are idempotent.
    private async Task FixIdleCompanionsAsync(CancellationToken ct)
    {
        var allPlayers    = await db.Players.ToListAsync(ct);
        var allCompanions = await db.Companions.ToListAsync(ct);

        var duties = new[] { HomesteadDuty.Harvester, HomesteadDuty.Salvager, HomesteadDuty.Guard, HomesteadDuty.Crafter };
        var modifiedCount = 0;

        // ── Pass 1: Fix stale IsActive flags ─────────────────────────────────
        // Build the complete set of companion IDs that are in any player's active slots
        var activeIds = allPlayers
            .SelectMany(p => p.ActiveCompanionIds)
            .ToHashSet();

        foreach (var companion in allCompanions.Where(c => !c.IsPermanentlyGone))
        {
            var inActiveSlot = activeIds.Contains(companion.Id);
            if (companion.IsActive && !inActiveSlot)
            {
                // Stale IsActive flag — clear it
                companion.SetActive(false);
                db.Companions.Update(companion);
                modifiedCount++;
                logger.LogWarning(
                    "FixIdleCompanions: cleared stale IsActive on {Name} ({Id}) — not in any player's active slots.",
                    companion.Name, companion.Id);
            }
        }

        // ── Pass 2: Rotate maxed active companions to homestead ───────────────
        foreach (var player in allPlayers)
        {
            if (!player.AutoRotateMaxedCompanions) continue;

            var activeCompanions = allCompanions
                .Where(c => !c.IsPermanentlyGone && player.ActiveCompanionIds.Contains(c.Id))
                .ToList();

            var maxedActive = activeCompanions
                .Where(c => c.CurrentLayer >= 6)
                .ToList();

            if (maxedActive.Count == 0) continue;

            // Growable candidates not in active slots (including those already on duty)
            var growable = allCompanions
                .Where(c => !c.IsPermanentlyGone
                         && !player.ActiveCompanionIds.Contains(c.Id)
                         && c.CurrentLayer < 6)
                .OrderBy(c => c.CurrentLayer)
                .ThenBy(c => c.UsageCounter)
                .ToList();

            if (growable.Count == 0)
            {
                logger.LogInformation(
                    "FixIdleCompanions: player {Name} has {Count} maxed active companion(s) but no growable replacements available.",
                    player.Name, maxedActive.Count);
                continue;
            }

            foreach (var maxed in maxedActive)
            {
                if (growable.Count == 0) break;

                var replacement = growable[0];
                growable.RemoveAt(0);

                // Deactivate maxed companion and send to homestead
                player.RemoveActiveCompanion(maxed.Id);
                maxed.SetActive(false);
                var bestDuty = duties.OrderByDescending(d => maxed.GetAptitude(d)).First();
                maxed.AssignToHomestead(bestDuty);
                db.Companions.Update(maxed);

                // Recall replacement from homestead if needed, then activate
                if (replacement.AssignedDuty.HasValue && replacement.AssignedDuty != HomesteadDuty.None)
                {
                    replacement.RecallFromHomestead();
                    // Clear building reference so building doesn't retain a ghost worker.
                    // NOTE: HasCompanion() isn't SQL-translatable (reads from JSON column),
                    // so we filter client-side after a cheap pre-filter on the JSON text.
                    var replacementIdString = replacement.Id.ToString();
                    var candidateBuildings = db.HomesteadBuildings
                        .Where(b => b.AssignedCompanionIdsJson != null
                                 && b.AssignedCompanionIdsJson != "[]"
                                 && b.AssignedCompanionIdsJson != ""
                                 && b.AssignedCompanionIdsJson.Contains(replacementIdString))
                        .ToList();
                    var assignedBuilding = candidateBuildings
                        .FirstOrDefault(b => b.HasCompanion(replacement.Id));
                    if (assignedBuilding is not null)
                    {
                        assignedBuilding.UnassignCompanion();
                        db.HomesteadBuildings.Update(assignedBuilding);
                    }
                }

                player.TryAddActiveCompanion(replacement.Id);
                replacement.SetActive(true);
                db.Companions.Update(replacement);

                modifiedCount += 2;
                logger.LogWarning(
                    "FixIdleCompanions: rotated {MaxedName} (Layer {Layer}) → {Duty}; " +
                    "{RepName} (Layer {RepLayer}) → active for player {PlayerName}.",
                    maxed.Name, maxed.CurrentLayer, bestDuty,
                    replacement.Name, replacement.CurrentLayer, player.Name);
            }

            db.Players.Update(player);
        }

        // ── Pass 3: Assign idle companions (no duty, not active) to homestead ─
        // Reload activeIds after the rotations above
        activeIds = allPlayers
            .SelectMany(p => p.ActiveCompanionIds)
            .ToHashSet();

        var idle = allCompanions
            .Where(c => !c.IsPermanentlyGone
                     && (c.AssignedDuty is null || c.AssignedDuty == HomesteadDuty.None)
                     && !activeIds.Contains(c.Id)
                     && !c.IsActive)
            .ToList();

        foreach (var companion in idle)
        {
            var bestDuty = duties.OrderByDescending(d => companion.GetAptitude(d)).First();
            companion.AssignToHomestead(bestDuty);
            db.Companions.Update(companion);
            modifiedCount++;

            logger.LogWarning(
                "FixIdleCompanions: assigned {Name} ({Id}) to {Duty} duty (aptitude {Apt}/3).",
                companion.Name, companion.Id, bestDuty, companion.GetAptitude(bestDuty));
        }

        if (modifiedCount == 0)
        {
            logger.LogInformation("FixIdleCompanions: all companions already properly assigned — nothing to do.");
            return;
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("FixIdleCompanions: fixed {Count} companion record(s) across all passes.", modifiedCount);
    }
}
