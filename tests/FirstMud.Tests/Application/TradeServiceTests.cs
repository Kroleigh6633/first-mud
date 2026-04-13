using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class TradeServiceTests
{
    private const string MarenNpcId = "maren";
    private const int MarenZoneNumber = 7; // aeldran-7-starting-road

    private static Player CreatePlayerAt(int zoneNumber = MarenZoneNumber)
    {
        var player = Player.Create("TradeTester", 1);
        player.Move(new Position(WorldId.Aeldran, zoneNumber, 0, 0));
        return player;
    }

    private static TradeService BuildService(
        IPlayerRepository players,
        IItemRepository items)
    {
        return new TradeService(TestContent.Shared, players, items, new TradeStockStore());
    }

    [Fact]
    public async Task GetVendorInventoryAsync_ReturnsStockWithPrices()
    {
        var svc = BuildService(
            Substitute.For<IPlayerRepository>(),
            Substitute.For<IItemRepository>());

        var snapshot = await svc.GetVendorInventoryAsync(MarenNpcId);

        snapshot.Should().NotBeNull();
        snapshot!.Stock.Should().NotBeEmpty();
        snapshot.Stock.Should().OnlyContain(s => s.BuyPrice >= 1 && s.SellPrice >= 1);
        snapshot.Stock.Should().OnlyContain(s => s.BuyPrice >= s.SellPrice, "vendors should have positive spread");
    }

    [Fact]
    public async Task BuyItemAsync_DeductsGold_AndAddsItem()
    {
        var players = Substitute.For<IPlayerRepository>();
        var items = Substitute.For<IItemRepository>();
        var player = CreatePlayerAt();
        player.AddGold(1000);
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = BuildService(players, items);
        var snapshot = await svc.GetVendorInventoryAsync(MarenNpcId);
        var stockedLine = snapshot!.Stock.First(s => s.Category == ItemCategory.Component || s.Category == ItemCategory.Reagent);

        var goldBefore = player.Gold;
        var result = await svc.BuyItemAsync(player.Id, MarenNpcId, stockedLine.ItemName, 1);

        result.Success.Should().BeTrue(result.Message);
        player.Gold.Should().Be(goldBefore - stockedLine.BuyPrice);
        await items.Received().AddAsync(Arg.Is<Item>(i => i.Name == stockedLine.ItemName), Arg.Any<CancellationToken>());
        await players.Received().UpdateAsync(player, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuyItemAsync_InsufficientGold_Fails()
    {
        var players = Substitute.For<IPlayerRepository>();
        var items = Substitute.For<IItemRepository>();
        var player = CreatePlayerAt(); // starts with 0 gold
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = BuildService(players, items);
        var snapshot = await svc.GetVendorInventoryAsync(MarenNpcId);
        var line = snapshot!.Stock.First();

        var result = await svc.BuyItemAsync(player.Id, MarenNpcId, line.ItemName, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("gold");
        player.Gold.Should().Be(0);
        await items.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuyItemAsync_WrongZone_Fails()
    {
        var players = Substitute.For<IPlayerRepository>();
        var items = Substitute.For<IItemRepository>();
        var player = CreatePlayerAt(zoneNumber: 999); // not at the vendor
        player.AddGold(10000);
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = BuildService(players, items);
        var snapshot = await svc.GetVendorInventoryAsync(MarenNpcId);
        var line = snapshot!.Stock.First();

        var result = await svc.BuyItemAsync(player.Id, MarenNpcId, line.ItemName, 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("zone");
    }

    [Fact]
    public async Task SellItemAsync_CreditsGold_AndRemovesItem()
    {
        var players = Substitute.For<IPlayerRepository>();
        var items = Substitute.For<IItemRepository>();
        var player = CreatePlayerAt();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var ore = Item.Create("Iron Ore", "ore", ItemCategory.Component, Workmanship.Of(1), WorldId.Aeldran);
        ore.SetOwner(player.Id);
        ore.AddQuantity(4); // now quantity = 5
        items.GetByOwnerAsync(player.Id, Arg.Any<CancellationToken>())
            .Returns(new List<Item> { ore });

        var svc = BuildService(players, items);
        var expectedUnit = svc.ComputeSellPrice("Iron Ore", ItemCategory.Component, 1);

        var goldBefore = player.Gold;
        var result = await svc.SellItemAsync(player.Id, MarenNpcId, "Iron Ore", 3);

        result.Success.Should().BeTrue(result.Message);
        player.Gold.Should().Be(goldBefore + expectedUnit * 3);
        ore.Quantity.Should().Be(2);
        await players.Received().UpdateAsync(player, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BuyItemAsync_UnknownVendor_Fails()
    {
        var players = Substitute.For<IPlayerRepository>();
        var items = Substitute.For<IItemRepository>();
        var svc = BuildService(players, items);

        var result = await svc.BuyItemAsync(Guid.NewGuid(), "no-such-npc", "Iron Ore", 1);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("vendor");
    }
}
