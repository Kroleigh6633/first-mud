using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class ReputationScoreTests
{
    [Fact]
    public void Zero_HasPointsEqualToZero()
    {
        ReputationScore.Zero.Points.Should().Be(0);
    }

    [Fact]
    public void Add_500_ProducesPoints500()
    {
        var result = ReputationScore.Zero.Add(500);

        result.Points.Should().Be(500);
    }

    [Fact]
    public void Subtract_200_FromZero_ProducesNegative200()
    {
        var result = ReputationScore.Zero.Subtract(200);

        result.Points.Should().Be(-200);
    }

    [Fact]
    public void Tier_Unknown_AtZeroPoints()
    {
        ReputationScore.Zero.Tier.Should().Be(ReputationTier.Unknown);
    }

    [Fact]
    public void Tier_Known_At1Point()
    {
        ReputationScore.Of(1).Tier.Should().Be(ReputationTier.Known);
    }

    [Fact]
    public void Tier_Known_At999Points()
    {
        ReputationScore.Of(999).Tier.Should().Be(ReputationTier.Known);
    }

    [Fact]
    public void Tier_Trusted_At1000Points()
    {
        ReputationScore.Of(1000).Tier.Should().Be(ReputationTier.Trusted);
    }

    [Fact]
    public void Tier_Honored_At3000Points()
    {
        ReputationScore.Of(3000).Tier.Should().Be(ReputationTier.Honored);
    }

    [Fact]
    public void Tier_Bound_At6000Points()
    {
        ReputationScore.Of(6000).Tier.Should().Be(ReputationTier.Bound);
    }

    [Fact]
    public void Tier_Hostile_AtNegative501Points()
    {
        ReputationScore.Of(-501).Tier.Should().Be(ReputationTier.Hostile);
    }

    [Fact]
    public void Tier_Wary_AtNegative1Point()
    {
        ReputationScore.Of(-1).Tier.Should().Be(ReputationTier.Wary);
    }

    [Fact]
    public void IsRising_TrueWhenCurrentPointsHigherThanPrevious()
    {
        var previous = ReputationScore.Of(100);
        var current = ReputationScore.Of(200);

        current.IsRising(previous).Should().BeTrue();
    }

    [Fact]
    public void IsRising_FalseWhenCurrentPointsSameAsPrevious()
    {
        var previous = ReputationScore.Of(100);
        var current = ReputationScore.Of(100);

        current.IsRising(previous).Should().BeFalse();
    }

    [Fact]
    public void IsSlipping_TrueWhenCurrentPointsLowerThanPrevious()
    {
        var previous = ReputationScore.Of(200);
        var current = ReputationScore.Of(100);

        current.IsSlipping(previous).Should().BeTrue();
    }

    [Fact]
    public void IsSlipping_FalseWhenCurrentPointsHigherThanPrevious()
    {
        var previous = ReputationScore.Of(100);
        var current = ReputationScore.Of(200);

        current.IsSlipping(previous).Should().BeFalse();
    }
}
