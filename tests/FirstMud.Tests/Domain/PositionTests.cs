using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class PositionTests
{
    [Fact]
    public void DistanceTo_ReturnsMaxValue_ForDifferentWorlds()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.ArdweldRemnant, 1, 0, 0);

        a.DistanceTo(b).Should().Be(double.MaxValue);
    }

    [Fact]
    public void DistanceTo_ReturnsMaxValue_ForDifferentZones()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.Aeldran, 2, 0, 0);

        a.DistanceTo(b).Should().Be(double.MaxValue);
    }

    [Fact]
    public void DistanceTo_Returns0_ForSamePosition()
    {
        var a = new Position(WorldId.Aeldran, 1, 5, 5);
        var b = new Position(WorldId.Aeldran, 1, 5, 5);

        a.DistanceTo(b).Should().Be(0);
    }

    [Fact]
    public void DistanceTo_ReturnsSqrt2_ForDiagonalAdjacent()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.Aeldran, 1, 1, 1);

        a.DistanceTo(b).Should().BeApproximately(Math.Sqrt(2), 0.0001);
    }

    [Fact]
    public void DistanceTo_Returns1_ForHorizontalAdjacent()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.Aeldran, 1, 1, 0);

        a.DistanceTo(b).Should().BeApproximately(1.0, 0.0001);
    }

    [Fact]
    public void IsAdjacentTo_TrueForSamePosition()
    {
        var a = new Position(WorldId.Aeldran, 1, 5, 5);
        var b = new Position(WorldId.Aeldran, 1, 5, 5);

        a.IsAdjacentTo(b).Should().BeTrue();
    }

    [Fact]
    public void IsAdjacentTo_TrueForAllEightNeighbors()
    {
        var center = new Position(WorldId.Aeldran, 1, 5, 5);

        var neighbors = new[]
        {
            new Position(WorldId.Aeldran, 1, 4, 4), // NW
            new Position(WorldId.Aeldran, 1, 5, 4), // N
            new Position(WorldId.Aeldran, 1, 6, 4), // NE
            new Position(WorldId.Aeldran, 1, 4, 5), // W
            new Position(WorldId.Aeldran, 1, 6, 5), // E
            new Position(WorldId.Aeldran, 1, 4, 6), // SW
            new Position(WorldId.Aeldran, 1, 5, 6), // S
            new Position(WorldId.Aeldran, 1, 6, 6), // SE
        };

        foreach (var neighbor in neighbors)
        {
            center.IsAdjacentTo(neighbor).Should().BeTrue(because: $"({neighbor.X},{neighbor.Y}) is adjacent to (5,5)");
        }
    }

    [Fact]
    public void IsAdjacentTo_FalseForDistance2()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.Aeldran, 1, 2, 0);

        a.IsAdjacentTo(b).Should().BeFalse();
    }

    [Fact]
    public void IsAdjacentTo_FalseForDifferentWorlds()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.ArdweldRemnant, 1, 0, 0);

        a.IsAdjacentTo(b).Should().BeFalse();
    }

    [Fact]
    public void IsAdjacentTo_FalseForDifferentZones()
    {
        var a = new Position(WorldId.Aeldran, 1, 0, 0);
        var b = new Position(WorldId.Aeldran, 2, 0, 0);

        a.IsAdjacentTo(b).Should().BeFalse();
    }
}
