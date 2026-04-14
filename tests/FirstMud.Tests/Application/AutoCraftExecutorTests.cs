using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace FirstMud.Tests.Application;

/// <summary>
/// Tests for <see cref="AutoCraftExecutor"/>. Uses NSubstitute mocks for the repos
/// and a fake <see cref="IContentProvider"/>. The planner's pure logic is covered
/// separately in <see cref="AutoCraftPlannerTests"/>; here we verify the plumbing
/// from live state → plan → craft-or-farm-fallback.
/// </summary>
public class AutoCraftExecutorTests
{
    private static RecipeDefinition Recipe(string id, string name, int wmMin, int wmMax,
        params (string ing, int qty)[] ingredients) => new(
            RecipeId: id, Name: name, ResultItemName: name,
            ResultCategory: ItemCategory.Weapon,
            RequiredCraftingSkill: 1,
            RequiredWorld: WorldId.Aeldran,
            RequiredTaperType: null,
            BaseWorkmanshipMin: wmMin, BaseWorkmanshipMax: wmMax,
            IsDiscoverable: false,
            Ingredients: ingredients.Select(i =>
                new RecipeIngredientDefinition(ItemCategory.Component, i.ing, i.qty)).ToList());

    private static IContentProvider ContentWith(params RecipeDefinition[] recipes)
    {
        var content = Substitute.For<IContentProvider>();
        content.AllRecipes().Returns(recipes);
        return content;
    }

    [Fact]
    public async Task PlanAsync_returns_farm_plan_when_ingredients_missing()
    {
        var player = Player.Create("Tester", 42);
        var content = ContentWith(
            Recipe("IRON_SWORD", "Iron Sword", 2, 4, ("Iron Ore", 3), ("Wood", 1)));

        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Item>()); // empty inventory

        var homesteads = Substitute.For<IHomesteadRepository>();
        homesteads.GetByPlayerIdAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns((Homestead?)null);

        var crafting = new CraftingService(players,
            Substitute.For<IRecipeRepository>(), items, homesteads);

        var executor = new AutoCraftExecutor(crafting, players, items, homesteads, content);

        var plan = await executor.PlanAsync(player.Id, CancellationToken.None);

        plan.TargetRecipe.Should().NotBeNull();
        plan.CraftableNow.Should().BeFalse();
        plan.MissingIngredients.Should().NotBeEmpty();
        plan.MissingIngredients.Should().Contain(e => e.ItemName == "Iron Ore" && e.Quantity == 3);
    }

    [Fact]
    public async Task TickAsync_returns_no_dispatch_when_no_viable_recipe()
    {
        var player = Player.Create("Tester", 42);
        // Player already has a Wm-10 sword equipped — no recipe can upgrade it.
        var existingSword = Item.Create("Iron Sword", "x", ItemCategory.Weapon,
            Workmanship.Of(10), WorldId.Aeldran, slot: EquipmentSlot.MeleeWeapon);
        existingSword.SetOwner(player.Id);
        player.Equip(EquipmentSlot.MeleeWeapon, existingSword.Id);

        var content = ContentWith(
            Recipe("IRON_SWORD", "Iron Sword", 2, 4, ("Iron Ore", 3)));

        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAsync(player.Id, Arg.Any<CancellationToken>()).Returns(new List<Item>());
        items.GetByIdAsync(existingSword.Id, Arg.Any<CancellationToken>()).Returns(existingSword);
        var homesteads = Substitute.For<IHomesteadRepository>();
        homesteads.GetByPlayerIdAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns((Homestead?)null);

        var crafting = new CraftingService(players, Substitute.For<IRecipeRepository>(), items, homesteads);
        var executor = new AutoCraftExecutor(crafting, players, items, homesteads, content);

        var tick = await executor.TickAsync(player.Id, CancellationToken.None);

        tick.DispatchedCraft.Should().BeFalse();
        tick.DidEquip.Should().BeFalse();
        tick.Plan.TargetRecipe.Should().BeNull();
    }

    [Fact]
    public async Task BuildStashAsync_aggregates_inventory_and_storage()
    {
        var player = Player.Create("Tester", 42);

        var invOre = Item.Create("Iron Ore", "", ItemCategory.Component,
            Workmanship.Of(1), WorldId.Aeldran);
        invOre.AddQuantity(2); // inv has 3
        invOre.SetOwner(player.Id);

        var homestead = Homestead.Create(player.Id, "Home");
        var storageOre = Item.Create("Iron Ore", "", ItemCategory.Component,
            Workmanship.Of(1), WorldId.Aeldran);
        storageOre.AddQuantity(4); // storage has 5
        var storageEntry = HomesteadStorageItem.Create(homestead.Id, storageOre.Id);

        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Item> { invOre });
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { storageOre });

        var homesteads = Substitute.For<IHomesteadRepository>();
        homesteads.GetByPlayerIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(homestead);
        homesteads.GetStorageItemsAsync(homestead.Id, Arg.Any<CancellationToken>())
            .Returns(new List<HomesteadStorageItem> { storageEntry });

        var crafting = new CraftingService(players, Substitute.For<IRecipeRepository>(), items, homesteads);
        var executor = new AutoCraftExecutor(crafting, players, items, homesteads,
            Substitute.For<IContentProvider>());

        var stash = await executor.BuildStashAsync(player.Id, CancellationToken.None);

        stash.Should().ContainKey("Iron Ore");
        stash["Iron Ore"].Should().Be(3 + 5);
    }
}
