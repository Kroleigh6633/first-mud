using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
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
        await SeedDevPlayerAsync(ct);
        await SeedNeo4jLoreAsync(ct);
        await SeedAeldranZonesAsync(ct);
        await SeedStarterRecipesAsync(ct);
    }

    // -------------------------------------------------------------------------
    // Dev player
    // -------------------------------------------------------------------------

    private async Task SeedDevPlayerAsync(CancellationToken ct)
    {
        if (await db.Players.AnyAsync(ct))
        {
            logger.LogInformation("Players table already has data — skipping dev player seed.");
            return;
        }

        var player = Player.Create("Kira Ashwood", craftingSeed: 42);
        await db.Players.AddAsync(player, ct);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Dev player seeded: Id={PlayerId} Name={Name}", player.Id, player.Name);
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
}
