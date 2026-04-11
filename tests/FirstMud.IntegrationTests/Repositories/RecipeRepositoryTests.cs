using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.IntegrationTests.Fixtures;
using FluentAssertions;

namespace FirstMud.IntegrationTests.Repositories;

[Collection("SqlServer")]
[Trait("Category", "Integration")]
public sealed class RecipeRepositoryTests
{
    private readonly SqlServerFixture _fixture;

    public RecipeRepositoryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAndGetByRecipeId_IngredientsRoundTrip()
    {
        var uniqueId = "TEST_RECIPE_" + Guid.NewGuid().ToString("N")[..8];

        await using var writeCtx = _fixture.CreateContext();

        var recipe = Recipe.Create(
            recipeId: uniqueId,
            name: "Integration Test Brew",
            ingredients:
            [
                RecipeIngredient.Create(ItemCategory.Reagent, "Moon Dust", 2),
                RecipeIngredient.Create(ItemCategory.Component, "Silver Wire", 1),
            ],
            resultCategory: ItemCategory.Consumable,
            resultItemName: "Integration Potion",
            baseWorkmanshipMin: 3,
            baseWorkmanshipMax: 7,
            requiredTaperType: null,
            requiredWorld: WorldId.Aeldran,
            requiredCraftingSkill: 2,
            isDiscoverable: false);

        await writeCtx.Recipes.AddAsync(recipe);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var loaded = await readCtx.Recipes.FirstOrDefaultAsync(r => r.RecipeId == uniqueId);

        loaded.Should().NotBeNull();
        loaded!.Ingredients.Should().HaveCount(2);
        loaded.Ingredients.Should().Contain(i => i.IngredientName == "Moon Dust" && i.BaseQuantity == 2);
        loaded.Ingredients.Should().Contain(i => i.IngredientName == "Silver Wire" && i.BaseQuantity == 1);
    }

    [Fact]
    public async Task GetByWorld_ReturnsOnlyRecipesForThatWorld()
    {
        var uniqueId = "WORLD_TEST_" + Guid.NewGuid().ToString("N")[..8];

        await using var writeCtx = _fixture.CreateContext();

        var recipe = Recipe.Create(
            recipeId: uniqueId,
            name: "Aeldran Blade",
            ingredients: [RecipeIngredient.Create(ItemCategory.Component, "Ore", 3)],
            resultCategory: ItemCategory.Weapon,
            resultItemName: "Aeldran Blade",
            baseWorkmanshipMin: 2,
            baseWorkmanshipMax: 5,
            requiredTaperType: null,
            requiredWorld: WorldId.Aeldran,
            requiredCraftingSkill: 1,
            isDiscoverable: false);

        await writeCtx.Recipes.AddAsync(recipe);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var results = await readCtx.Recipes
            .Where(r => r.RequiredWorld == WorldId.Aeldran)
            .ToListAsync();

        results.Should().Contain(r => r.RecipeId == uniqueId);
        results.Should().OnlyContain(r => r.RequiredWorld == WorldId.Aeldran,
            because: "the query filtered to Aeldran world");
    }
}
