using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FirstMud.Tests.Application;

/// <summary>
/// Tests for <see cref="AutoImbueExecutor"/>. Uses NSubstitute mocks for the
/// repos + a fake <see cref="IContentProvider"/>. The planner's pure logic is
/// covered in <see cref="AutoImbuePlannerTests"/>; here we verify the
/// plumbing from live state → plan → imbue-or-farm-fallback.
/// </summary>
public class AutoImbueExecutorTests
{
    private static IContentProvider ContentWith(params ImbueRecipeDefinition[] recipes)
    {
        var content = Substitute.For<IContentProvider>();
        content.AllImbueRecipes().Returns(recipes);
        return content;
    }

    private static AutoImbueExecutor BuildExecutor(
        Player player,
        List<Item>? inventory = null,
        IContentProvider? content = null,
        Item? equipped = null)
    {
        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns(inventory ?? new List<Item>());
        if (equipped is not null)
            items.GetByIdAsync(equipped.Id, Arg.Any<CancellationToken>()).Returns(equipped);

        var homesteads = Substitute.For<IHomesteadRepository>();
        homesteads.GetByPlayerIdAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns((Homestead?)null);

        var imbue = new ImbueService(players, items, NullLogger<ImbueService>.Instance);

        return new AutoImbueExecutor(imbue, players, items, homesteads,
            content ?? Substitute.For<IContentProvider>());
    }

    [Fact]
    public async Task PlanAsync_returns_null_target_when_nothing_equipped()
    {
        var player = Player.Create("Tester", 42);
        var exec = BuildExecutor(player);

        var plan = await exec.PlanAsync(player.Id, CancellationToken.None);

        plan.TargetItemId.Should().BeNull();
    }

    [Fact]
    public async Task PlanAsync_returns_farm_plan_when_equipped_has_open_slot_but_no_reagents()
    {
        var player = Player.Create("Tester", 42);
        var sword = Item.Create("Iron Sword", "", ItemCategory.Weapon,
            Workmanship.Of(4), WorldId.Aeldran, slot: EquipmentSlot.MeleeWeapon);
        sword.SetOwner(player.Id);
        player.Equip(EquipmentSlot.MeleeWeapon, sword.Id);

        var exec = BuildExecutor(player, inventory: new List<Item> { sword }, equipped: sword);

        var plan = await exec.PlanAsync(player.Id, CancellationToken.None);

        plan.TargetItemId.Should().NotBeNull();
        plan.ImbuableNow.Should().BeFalse();
        plan.MissingReagents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task TickAsync_dispatches_imbue_when_reagent_and_target_present()
    {
        var player = Player.Create("Tester", 42);
        // Equipped sword (not the imbue target — we imbue an unequipped copy).
        var equippedSword = Item.Create("Iron Sword", "", ItemCategory.Weapon,
            Workmanship.Of(4), WorldId.Aeldran, slot: EquipmentSlot.MeleeWeapon);
        equippedSword.SetOwner(player.Id);
        player.Equip(EquipmentSlot.MeleeWeapon, equippedSword.Id);

        // Unequipped inventory sword that will be imbued.
        var bagSword = Item.Create("Iron Sword", "", ItemCategory.Weapon,
            Workmanship.Of(4), WorldId.Aeldran, slot: EquipmentSlot.MeleeWeapon);
        bagSword.SetOwner(player.Id);

        // Taper reagent. ImbueService.ResolveImbueType recognises "Fire Shaping Taper" → ImbueType.Fire.
        var taper = Item.Create("Fire Shaping Taper", "", ItemCategory.Reagent,
            Workmanship.Of(1), WorldId.Aeldran);
        taper.SetOwner(player.Id);

        var exec = BuildExecutor(
            player,
            inventory: new List<Item> { equippedSword, bagSword, taper },
            equipped: equippedSword);

        var tick = await exec.TickAsync(player.Id, CancellationToken.None);

        tick.DispatchedImbue.Should().BeTrue();
        tick.Imbue.Should().NotBeNull();
        tick.Plan.TargetItemId.Should().NotBeNull();
        tick.Plan.ImbuableNow.Should().BeTrue();
    }

    [Fact]
    public async Task TickAsync_does_not_dispatch_when_reagent_missing()
    {
        var player = Player.Create("Tester", 42);
        var sword = Item.Create("Iron Sword", "", ItemCategory.Weapon,
            Workmanship.Of(4), WorldId.Aeldran, slot: EquipmentSlot.MeleeWeapon);
        sword.SetOwner(player.Id);
        player.Equip(EquipmentSlot.MeleeWeapon, sword.Id);

        var exec = BuildExecutor(player, inventory: new List<Item> { sword }, equipped: sword);

        var tick = await exec.TickAsync(player.Id, CancellationToken.None);

        tick.DispatchedImbue.Should().BeFalse();
        tick.Plan.TargetItemId.Should().NotBeNull();
        tick.Plan.MissingReagents.Should().NotBeEmpty();
    }

    [Fact]
    public async Task BuildStashAsync_aggregates_inventory_and_storage()
    {
        var player = Player.Create("Tester", 42);

        var invTopaz = Item.Create("Topaz", "", ItemCategory.Reagent,
            Workmanship.Of(1), WorldId.Aeldran);
        invTopaz.AddQuantity(1); // 2 total
        invTopaz.SetOwner(player.Id);

        var homestead = Homestead.Create(player.Id, "Home");
        var storageTopaz = Item.Create("Topaz", "", ItemCategory.Reagent,
            Workmanship.Of(1), WorldId.Aeldran);
        storageTopaz.AddQuantity(2); // 3 in storage

        var entry = HomesteadStorageItem.Create(homestead.Id, storageTopaz.Id);

        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Item> { invTopaz });
        items.GetByIdsAsync(Arg.Any<IEnumerable<Guid>>(), Arg.Any<CancellationToken>())
            .Returns(new List<Item> { storageTopaz });

        var homesteads = Substitute.For<IHomesteadRepository>();
        homesteads.GetByPlayerIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(homestead);
        homesteads.GetStorageItemsAsync(homestead.Id, Arg.Any<CancellationToken>())
            .Returns(new List<HomesteadStorageItem> { entry });

        var imbue = new ImbueService(players, items, NullLogger<ImbueService>.Instance);
        var exec = new AutoImbueExecutor(imbue, players, items, homesteads,
            Substitute.For<IContentProvider>());

        var stash = await exec.BuildStashAsync(player.Id, CancellationToken.None);

        stash.Should().ContainKey("Topaz");
        stash["Topaz"].Should().Be(2 + 3);
    }
}
