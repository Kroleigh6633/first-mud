using FirstMud.GameServer.Services;
using FluentAssertions;

namespace FirstMud.Tests.Services;

public class ZoneGridLayoutTests
{
    [Fact]
    public void SameGuid_AlwaysProducesSamePosition()
    {
        var id = Guid.NewGuid();

        var (x1, y1) = ZoneGridLayout.GetPosition(id);
        var (x2, y2) = ZoneGridLayout.GetPosition(id);

        x1.Should().Be(x2);
        y1.Should().Be(y2);
    }

    [Fact]
    public void XPosition_IsWithinGridWidth()
    {
        for (var i = 0; i < 50; i++)
        {
            var (x, _) = ZoneGridLayout.GetPosition(Guid.NewGuid());
            x.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(ZoneGridLayout.GridWidth);
        }
    }

    [Fact]
    public void YPosition_IsWithinGridHeight()
    {
        for (var i = 0; i < 50; i++)
        {
            var (_, y) = ZoneGridLayout.GetPosition(Guid.NewGuid());
            y.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(ZoneGridLayout.GridHeight);
        }
    }

    [Fact]
    public void TwentyDistinctGuids_ProduceSomeDistinctPositions()
    {
        // 40×20 = 800 possible cells, 20 zones. Expect at least 10 unique positions
        // (collision probability is very low for 20 items in 800 cells).
        var positions = Enumerable.Range(0, 20)
            .Select(_ => ZoneGridLayout.GetPosition(Guid.NewGuid()))
            .ToHashSet();

        positions.Count.Should().BeGreaterThan(10,
            "20 random GUIDs spread across 800 cells should yield many distinct positions");
    }

    [Fact]
    public void EmptyGuid_ReturnsValidPosition()
    {
        var (x, y) = ZoneGridLayout.GetPosition(Guid.Empty);

        x.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(ZoneGridLayout.GridWidth);
        y.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(ZoneGridLayout.GridHeight);
    }

    [Fact]
    public void AllZeroBytes_StillProducesValidPosition()
    {
        var id = new Guid(new byte[16]);
        var (x, y) = ZoneGridLayout.GetPosition(id);

        x.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(ZoneGridLayout.GridWidth);
        y.Should().BeGreaterThanOrEqualTo(0).And.BeLessThan(ZoneGridLayout.GridHeight);
    }
}
