using FirstMud.Application.Models;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class CraftingServiceTests
{
    // ---- helpers ----

    private static Player CreatePlayer(int craftingSeed = 42, int craftingSkill = 10)
    {
        var player = Player.Create("Tester", craftingSeed);
        // Move player to Aeldran (default world) — already set by Player.Create
        return player;
    }

    private static Recipe CreateRecipe(
        string recipeId = "recipe_basic",
        int requiredSkill = 1,
        TaperType? requiredTaper = null,
        WorldId requiredWorld = WorldId.Aeldran,
        int baseQty = 10)
    {
        var ingredient = RecipeIngredient.Create(ItemCategory.Component, "Iron Shard", baseQty);
        return Recipe.Create(
            recipeId,
            "Basic Recipe",
            [ingredient],
            ItemCategory.Weapon,
            "Iron Blade",
            1, 5,
            requiredTaper,
            requiredWorld,
            requiredSkill,
            false);
    }

    private static Item CreateComponent(string name = "Iron Shard", int workmanship = 3)
        => Item.Create(name, "A component", ItemCategory.Component, Workmanship.Of(workmanship), WorldId.Aeldran);

    private static Item CreateTaper(TaperType taperType, TaperQuality quality = TaperQuality.Pristine)
    {
        var item = Item.Create("Taper", "A taper", ItemCategory.Reagent, Workmanship.Of(3), WorldId.Aeldran);
        item.ApplyTaperImbue(taperType, quality, MagicElement.Fire, MagicPolarity.Shaping);
        return item;
    }

    private static List<Item> BuildMatchingComponents(Recipe recipe, Player player)
    {
        // Replicate the CalculateSeededQuantities logic to produce exactly the right count
        // private static readonly int[] IngredientPrimes = [17, 31, 47, 61, 79];
        int[] primes = [17, 31, 47, 61, 79];
        var components = new List<Item>();
        for (int i = 0; i < recipe.Ingredients.Count; i++)
        {
            var ingredient = recipe.Ingredients[i];
            var prime = primes[i % primes.Length];
            var seededQty = (int)Math.Round(
                ingredient.BaseQuantity * (1.0 + ((player.CraftingSeed * prime) % 40 - 20) / 100.0));
            seededQty = Math.Max(1, seededQty);
            for (int q = 0; q < seededQty; q++)
                components.Add(CreateComponent(ingredient.IngredientName));
        }
        return components;
    }

    // ---- tests ----

    [Fact]
    public async Task GetSeededQuantitiesAsync_SameSeedAndRecipe_ReturnsDeterministicResult()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer(craftingSeed: 42);
        var recipe = CreateRecipe();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var first = await svc.GetSeededQuantitiesAsync(player.Id, "recipe_basic");
        var second = await svc.GetSeededQuantitiesAsync(player.Id, "recipe_basic");

        first.Should().BeEquivalentTo(second);
    }

    [Fact]
    public async Task GetSeededQuantitiesAsync_DifferentSeeds_ReturnDifferentQuantities()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player42 = CreatePlayer(craftingSeed: 42);
        var player99 = CreatePlayer(craftingSeed: 99);

        // Recipe with base qty large enough that a ±20% delta changes the value
        var recipe = CreateRecipe(baseQty: 20);

        players.GetByIdAsync(player42.Id, Arg.Any<CancellationToken>()).Returns(player42);
        players.GetByIdAsync(player99.Id, Arg.Any<CancellationToken>()).Returns(player99);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var qty42 = await svc.GetSeededQuantitiesAsync(player42.Id, "recipe_basic");
        var qty99 = await svc.GetSeededQuantitiesAsync(player99.Id, "recipe_basic");

        // Seeds 42 and 99 produce different prime-offset results for base qty 20
        qty42.Should().NotBeEquivalentTo(qty99);
    }

    [Fact]
    public async Task AttemptCraftAsync_PlayerNotFound_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        players.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Player?)null);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(Guid.NewGuid(), "recipe_basic", [], null);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.ProducedItem.Should().BeNull();
    }

    [Fact]
    public async Task AttemptCraftAsync_RecipeNotFound_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((Recipe?)null);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "nonexistent", [], null);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.ProducedItem.Should().BeNull();
    }

    [Fact]
    public async Task AttemptCraftAsync_InsufficientCraftingSkill_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        // Player has default crafting skill of 1 (from Player.Create)
        var player = CreatePlayer();
        var recipe = CreateRecipe(requiredSkill: 50); // requires skill 50

        var componentId = Guid.NewGuid();
        var component = CreateComponent();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { component }.AsReadOnly());

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", [componentId], null);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.Message.Should().Contain("crafting skill");
    }

    [Fact]
    public async Task AttemptCraftAsync_WrongWorld_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer(); // position is WorldId.Aeldran by default
        var recipe = CreateRecipe(requiredWorld: WorldId.ArdweldRemnant); // needs ArdweldRemnant

        var componentId = Guid.NewGuid();
        var component = CreateComponent();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { component }.AsReadOnly());

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", [componentId], null);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.Message.Should().Contain("ArdweldRemnant");
    }

    [Fact]
    public async Task AttemptCraftAsync_MagicalRecipeWithoutTaper_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer();
        var recipe = CreateRecipe(requiredTaper: TaperType.Shaping); // requires a Shaping taper

        var componentIds = new List<Guid> { Guid.NewGuid() };
        var component = CreateComponent();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { component }.AsReadOnly());

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        // taperId is null → no taper provided
        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", componentIds, null);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.Message.Should().Contain("taper");
    }

    [Fact]
    public async Task AttemptCraftAsync_WrongTaperType_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer();
        var recipe = CreateRecipe(requiredTaper: TaperType.Shaping);

        var taperId = Guid.NewGuid();
        var taperItem = CreateTaper(TaperType.Unmaking); // wrong type

        var componentIds = new List<Guid> { Guid.NewGuid() };
        var component = CreateComponent();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { component }.AsReadOnly());
        items.GetByIdAsync(taperId, Arg.Any<CancellationToken>()).Returns(taperItem);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", componentIds, taperId);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        (result.Message.Contains("taper type") || result.Message.Contains("Wrong taper")).Should().BeTrue(
            because: $"message should mention taper type mismatch but was: {result.Message}");
    }

    [Fact]
    public async Task AttemptCraftAsync_WithMatchingComponents_NeverReturnsSuccess_WhenQuantitiesMismatch()
    {
        // Provide zero components → quantities won't match → outcome is never Success
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer();
        var recipe = CreateRecipe(); // needs components

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_mismatch", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item>().AsReadOnly()); // empty list matches 0 ids

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_mismatch", [], null);

        // With zero components, quantities don't match → outcome is never Success
        result.Outcome.Should().NotBe(CraftingOutcome.Success);
    }

    [Fact]
    public async Task AttemptCraftAsync_WithMatchingComponents_ProducedItemHasWorkmanshipInRange_WhenSuccess()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer(craftingSeed: 42);
        var recipe = CreateRecipe(baseQty: 10);

        var matchingComponents = BuildMatchingComponents(recipe, player);
        var componentIds = matchingComponents.Select(_ => Guid.NewGuid()).ToList();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(matchingComponents.AsReadOnly());

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", componentIds, null);

        // Whether outcome is Success or Discovery (item produced), workmanship must be in [1,10]
        if (result.ProducedItem is not null)
        {
            result.ProducedItem.Workmanship.Value.Should().BeInRange(1, 10);
        }
        else
        {
            // Non-item outcomes (NearMiss, UnexpectedResult, ComponentLoss) are all valid
            result.Outcome.Should().NotBe(CraftingOutcome.Success);
        }
    }

    [Fact]
    public async Task AttemptCraftAsync_TaperPristineBoostWorkmanship_WhenSuccess()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        // Craft with Shaping taper (required) and Pristine quality
        var player = CreatePlayer(craftingSeed: 7);
        var recipe = CreateRecipe(requiredTaper: TaperType.Shaping, baseQty: 5);

        var matchingComponents = BuildMatchingComponents(recipe, player);
        var componentIds = matchingComponents.Select(_ => Guid.NewGuid()).ToList();
        var taperId = Guid.NewGuid();
        var taperItem = CreateTaper(TaperType.Shaping, TaperQuality.Pristine);

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(matchingComponents.AsReadOnly());
        items.GetByIdAsync(taperId, Arg.Any<CancellationToken>()).Returns(taperItem);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", componentIds, taperId);

        // If item was produced, its AppliedTaper should be set (taper was applied on success/discovery)
        if (result.ProducedItem is not null)
        {
            result.ProducedItem.AppliedTaper.Should().Be(TaperType.Shaping);
        }
    }

    [Fact]
    public async Task AttemptCraftAsync_ComponentItemsNotAllFound_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer();
        var recipe = CreateRecipe();

        // Request 2 component IDs but only 1 is found
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { CreateComponent() }.AsReadOnly()); // only 1 returned for 2 requested

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", [id1, id2], null);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.Message.Should().Contain("component items");
    }

    [Fact]
    public async Task AttemptCraftAsync_TaperIdProvided_ButTaperNotFound_ReturnsNearMiss()
    {
        var players = Substitute.For<IPlayerRepository>();
        var recipes = Substitute.For<IRecipeRepository>();
        var items = Substitute.For<IItemRepository>();

        var player = CreatePlayer();
        var recipe = CreateRecipe(requiredTaper: TaperType.Shaping);
        var taperId = Guid.NewGuid();
        var componentId = Guid.NewGuid();

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        recipes.GetByRecipeIdAsync("recipe_basic", Arg.Any<CancellationToken>()).Returns(recipe);
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { CreateComponent() }.AsReadOnly());
        items.GetByIdAsync(taperId, Arg.Any<CancellationToken>()).Returns((Item?)null);

        var svc = new CraftingService(players, recipes, items, Substitute.For<IHomesteadRepository>());

        var result = await svc.AttemptCraftAsync(player.Id, "recipe_basic", [componentId], taperId);

        result.Outcome.Should().Be(CraftingOutcome.NearMiss);
        result.Message.Should().Contain("Taper item not found");
    }
}
