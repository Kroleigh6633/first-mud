using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class WeaveTests
{
    [Fact]
    public void Create_SetsCurrentEqualsMaximum()
    {
        var weave = Weave.Create(100);

        weave.Current.Should().Be(100);
        weave.Maximum.Should().Be(100);
    }

    [Fact]
    public void Spend_ReducesCurrent()
    {
        var weave = Weave.Create(100);

        var result = weave.Spend(30);

        result.Current.Should().Be(70);
    }

    [Fact]
    public void Spend_NeverGoesBelowZero()
    {
        var weave = Weave.Create(100);

        var result = weave.Spend(150);

        result.Current.Should().Be(0);
    }

    [Fact]
    public void Spend_MoreThanCurrentResultsInZero()
    {
        var weave = Weave.Create(50);
        weave = weave.Spend(40); // now at 10

        var result = weave.Spend(20); // try to spend 20 from 10

        result.Current.Should().Be(0);
    }

    [Fact]
    public void Restore_IncreasesCurrent()
    {
        var weave = Weave.Create(100).Spend(60);

        var result = weave.Restore(30);

        result.Current.Should().Be(70);
    }

    [Fact]
    public void Restore_NeverExceedsMaximum()
    {
        var weave = Weave.Create(100).Spend(10);

        var result = weave.Restore(50);

        result.Current.Should().Be(100);
    }

    [Fact]
    public void RestoreFull_SetsCurrentEqualsMaximum()
    {
        var weave = Weave.Create(100).Spend(80);

        var result = weave.RestoreFull();

        result.Current.Should().Be(result.Maximum);
        result.Current.Should().Be(100);
    }

    [Fact]
    public void ExpandMaximum_IncreasesMaximumByAmount()
    {
        var weave = Weave.Create(100);

        var result = weave.ExpandMaximum(5);

        result.Maximum.Should().Be(105);
    }

    [Fact]
    public void ExpandMaximum_DoesNotChangeCurrentAboveOldCurrent()
    {
        var weave = Weave.Create(100);

        var result = weave.ExpandMaximum(5);

        // Current stays at 100 (old maximum), Maximum is now 105
        result.Current.Should().Be(100);
        result.Maximum.Should().Be(105);
    }

    [Fact]
    public void IsEmpty_TrueWhenCurrentIsZero()
    {
        var weave = Weave.Create(100).Spend(100);

        weave.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_FalseWhenCurrentIsAboveZero()
    {
        var weave = Weave.Create(100).Spend(99);

        weave.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsCritical_TrueWhenCurrentAtOrBelowTenPercent()
    {
        var weave = Weave.Create(100).Spend(90); // 10 remaining = 10%

        weave.IsCritical.Should().BeTrue();
    }

    [Fact]
    public void IsCritical_TrueWhenCurrentBelowTenPercent()
    {
        var weave = Weave.Create(100).Spend(95); // 5 remaining = 5%

        weave.IsCritical.Should().BeTrue();
    }

    [Fact]
    public void IsCritical_FalseWhenCurrentAboveTenPercent()
    {
        var weave = Weave.Create(100).Spend(89); // 11 remaining = 11%

        weave.IsCritical.Should().BeFalse();
    }

    [Fact]
    public void IsLow_TrueWhenCurrentAtOrBelowTwentyFivePercent()
    {
        var weave = Weave.Create(100).Spend(75); // 25 remaining = 25%

        weave.IsLow.Should().BeTrue();
    }

    [Fact]
    public void IsLow_FalseWhenCurrentAboveTwentyFivePercent()
    {
        var weave = Weave.Create(100).Spend(74); // 26 remaining = 26%

        weave.IsLow.Should().BeFalse();
    }

    [Fact]
    public void VisibleState_Full_WhenAtOrAbove75Percent()
    {
        var weave = Weave.Create(100); // 100% = Full

        weave.VisibleState.Should().Be(WeaveState.Full);
    }

    [Fact]
    public void VisibleState_Steady_WhenBetween50And75Percent()
    {
        var weave = Weave.Create(100).Spend(40); // 60% = Steady

        weave.VisibleState.Should().Be(WeaveState.Steady);
    }

    [Fact]
    public void VisibleState_Strained_WhenBetween25And50Percent()
    {
        var weave = Weave.Create(100).Spend(65); // 35% = Strained

        weave.VisibleState.Should().Be(WeaveState.Strained);
    }

    [Fact]
    public void VisibleState_Critical_WhenBetween10And25Percent()
    {
        var weave = Weave.Create(100).Spend(85); // 15% = Critical

        weave.VisibleState.Should().Be(WeaveState.Critical);
    }

    [Fact]
    public void VisibleState_Depleted_WhenBelow10Percent()
    {
        var weave = Weave.Create(100).Spend(95); // 5% = Depleted

        weave.VisibleState.Should().Be(WeaveState.Depleted);
    }

    [Fact]
    public void Spend_IsImmutable_OriginalUnchanged()
    {
        var original = Weave.Create(100);

        var _ = original.Spend(50);

        original.Current.Should().Be(100);
        original.Maximum.Should().Be(100);
    }

    [Fact]
    public void Restore_IsImmutable_OriginalUnchanged()
    {
        var spent = Weave.Create(100).Spend(60);

        var _ = spent.Restore(30);

        spent.Current.Should().Be(40);
    }
}
