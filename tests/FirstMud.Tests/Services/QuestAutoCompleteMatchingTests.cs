using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FirstMud.GameServer.Services;
using FluentAssertions;

namespace FirstMud.Tests.Services;

/// <summary>
/// Regression tests for the playtest bug: quest completion never fired because
/// the regex-based ExtractItemKeyword path couldn't reliably match the English
/// title noun ("healing herbs") against player inventory ("Sage", "Fire Moss").
/// The typed <c>targetItemNames</c> field + FindMatchingItemsByNames gives
/// procgen an end-to-end deterministic match path.
/// </summary>
public class QuestAutoCompleteMatchingTests
{
    private static Item MakeStack(string name, int qty)
    {
        var item = Item.Create(name, $"test {name}", ItemCategory.Component,
            Workmanship.Of(1), WorldId.Aeldran);
        if (qty > 1) item.AddQuantity(qty - 1);
        return item;
    }

    [Fact]
    public void FindMatchingItemsByNames_ExactCaseInsensitiveMatch()
    {
        var items = new List<Item>
        {
            MakeStack("Sage", 5),
            MakeStack("Mint", 2),
            MakeStack("Oak Wood", 10),
        };

        var result = QuestAutoCompleteService.FindMatchingItemsByNames(items, new[] { "sage" });
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Sage");
    }

    [Fact]
    public void FindMatchingItemsByNames_MultiWordItemName_ExactMatch()
    {
        var items = new List<Item>
        {
            MakeStack("Fire Moss", 3),
            MakeStack("Sage", 1),
        };

        var result = QuestAutoCompleteService.FindMatchingItemsByNames(items, new[] { "Fire Moss" });
        result.Should().HaveCount(1);
        result[0].Name.Should().Be("Fire Moss");
    }

    [Fact]
    public void FindMatchingItemsByNames_NoMatchingItem_ReturnsEmpty()
    {
        var items = new List<Item>
        {
            MakeStack("Sage", 5),
        };

        var result = QuestAutoCompleteService.FindMatchingItemsByNames(items, new[] { "Thornroot" });
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindMatchingItemsByNames_EmptyTargetList_ReturnsEmpty()
    {
        var items = new List<Item> { MakeStack("Sage", 5) };
        var result = QuestAutoCompleteService.FindMatchingItemsByNames(items, Array.Empty<string>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void FindMatchingItemsByNames_IgnoresWhitespaceAndCase()
    {
        var items = new List<Item> { MakeStack("Sage", 5) };
        var result = QuestAutoCompleteService.FindMatchingItemsByNames(items, new[] { "  SAGE  " });
        result.Should().HaveCount(1);
    }
}
