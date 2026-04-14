using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Commands;
using FirstMud.GameServer.Handlers;
using FirstMud.GameServer.Hubs;
using FirstMud.GameServer.Services;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace FirstMud.Tests.Handlers;

/// <summary>
/// Unit tests for the NEW gem-driven imbue path. Legacy taper imbues
/// continue to flow through <see cref="ImbueCommandHandler"/> and
/// PracticeEnchantingCommandHandler — see those tests (MechanicsSweepTests
/// covers the legacy path via ImbueService behaviour).
/// </summary>
public class ImbueWithGemCommandHandlerTests
{
    private static IHubContext<GameHub> CreateHubContext()
    {
        var hub = Substitute.For<IHubContext<GameHub>>();
        var clients = Substitute.For<IHubClients>();
        var clientProxy = Substitute.For<IClientProxy>();
        hub.Clients.Returns(clients);
        clients.Group(Arg.Any<string>()).Returns(clientProxy);
        return hub;
    }

    private static GameNotificationService CreateNotificationService() =>
        new(CreateHubContext());

    private static Item MakeItem(string name, Guid owner, int workmanship, int imbueCount = 0)
    {
        var item = Item.Create(
            name,
            "Test weapon",
            ItemCategory.Weapon,
            Workmanship.Of(workmanship),
            WorldId.Aeldran,
            slot: EquipmentSlot.MeleeWeapon);
        item.SetOwner(owner);
        for (int i = 0; i < imbueCount; i++)
            item.ApplyImbue(ImbueType.Fire, 0.20f);
        return item;
    }

    private static Item MakeGem(string name, Guid owner)
    {
        var gem = Item.Create(
            name,
            "Test gem",
            ItemCategory.Reagent,
            Workmanship.Of(1),
            WorldId.Aeldran);
        gem.SetOwner(owner);
        return gem;
    }

    private static (Player player, Item target, Item gem,
                     IPlayerRepository players, IItemRepository items)
        Setup(string gemName, int craftingSkill, int workmanship, int existingImbues = 0)
    {
        var player = Player.Create("Tester", 1);
        if (craftingSkill > 1) player.GainCraftingSkillXp(craftingSkill - 1);

        var target = MakeItem("Iron Sword", player.Id, workmanship, existingImbues);
        var gem = MakeGem(gemName, player.Id);

        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Player?>(player));

        var items = Substitute.For<IItemRepository>();
        items.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Item?>(target));
        items.GetByIdAsync(gem.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Item?>(gem));

        return (player, target, gem, players, items);
    }

    [Fact]
    public async Task Successful_gem_imbue_applies_recipe_power_and_consumes_gem()
    {
        var (player, target, gem, players, items) =
            Setup("Polished Emerald", craftingSkill: 4, workmanship: 3);

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "emerald-earth"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        target.Imbues.Should().ContainSingle()
            .Which.Should().Match<AppliedImbue>(i => i.Type == ImbueType.Earth && i.Power > 0.34f && i.Power < 0.36f);
        // Gem was non-stackable single → DeleteAsync should have fired
        await items.Received(1).DeleteAsync(gem.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Insufficient_crafting_skill_is_rejected_and_gem_is_preserved()
    {
        var (player, target, gem, players, items) =
            Setup("Raw Diamond", craftingSkill: 3, workmanship: 3);

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        // diamond-high-fire requires CraftingSkill 8
        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "diamond-high-fire"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Crafting Skill 8 required");
        target.Imbues.Should().BeEmpty();
        await items.DidNotReceive().DeleteAsync(gem.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Gem_that_does_not_match_recipe_reagent_is_rejected()
    {
        var (player, target, gem, players, items) =
            Setup("Polished Aquamarine", craftingSkill: 4, workmanship: 3);

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        // recipe emerald-earth requires an emerald, not an aquamarine
        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "emerald-earth"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("does not match recipe");
        target.Imbues.Should().BeEmpty();
        await items.DidNotReceive().DeleteAsync(gem.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unknown_recipe_id_is_rejected()
    {
        var (player, target, gem, players, items) =
            Setup("Raw Emerald", craftingSkill: 10, workmanship: 3);

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "not-a-real-recipe"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Unknown imbue recipe");
    }

    [Fact]
    public async Task Full_imbue_slots_rejects_non_upgrade_recipe()
    {
        // W3 → MaxImbueSlots = 2. Pre-fill both.
        var (player, target, gem, players, items) =
            Setup("Polished Emerald", craftingSkill: 4, workmanship: 3, existingImbues: 2);

        target.MaxImbueSlots.Should().Be(2);
        target.Imbues.Should().HaveCount(2);

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "emerald-earth"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("no open imbue slots");
        target.Imbues.Should().HaveCount(2);
    }

    [Fact]
    public async Task Diamond_upgrade_is_allowed_even_when_slots_are_full()
    {
        var (player, target, gem, players, items) =
            Setup("Flawless Diamond", craftingSkill: 5, workmanship: 3, existingImbues: 2);

        target.MaxImbueSlots.Should().Be(2);
        target.Imbues.Should().HaveCount(2);
        var imbueCountBefore = target.Imbues.Count;

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "diamond-universal"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        // Upgrade semantics (replace): imbue count stays the same, but one
        // imbue's power has increased.
        target.Imbues.Count.Should().Be(imbueCountBefore);
        target.Imbues.Any(i => i.Power > 0.20f).Should().BeTrue(
            "diamond-universal should double an existing imbue's power");
        await items.Received(1).DeleteAsync(gem.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Target_item_not_owned_by_player_is_rejected()
    {
        var player = Player.Create("Tester", 1);
        var stranger = Guid.NewGuid();
        var target = MakeItem("Iron Sword", stranger, workmanship: 3);
        var gem = MakeGem("Polished Emerald", player.Id);

        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Player?>(player));
        var items = Substitute.For<IItemRepository>();
        items.GetByIdAsync(target.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Item?>(target));
        items.GetByIdAsync(gem.Id, Arg.Any<CancellationToken>()).Returns(Task.FromResult<Item?>(gem));

        var handler = new ImbueWithGemCommandHandler(
            TestContent.Shared, players, items, CreateNotificationService());

        var result = await handler.HandleAsync(
            new ImbueWithGemCommand(player.Id, target.Id, gem.Id, "emerald-earth"),
            CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("Target item not in your inventory");
    }
}
