using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class WorkmanshipTests
{
    [Fact]
    public void Of_5_CreatesWithValue5()
    {
        var w = Workmanship.Of(5);

        w.Value.Should().Be(5);
    }

    [Fact]
    public void Of_0_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Workmanship.Of(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Of_11_ThrowsArgumentOutOfRangeException()
    {
        var act = () => Workmanship.Of(11);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Of_1_Succeeds()
    {
        var w = Workmanship.Of(1);

        w.Value.Should().Be(1);
    }

    [Fact]
    public void Of_10_Succeeds()
    {
        var w = Workmanship.Of(10);

        w.Value.Should().Be(10);
    }

    [Fact]
    public void IsExceptional_TrueForValue8()
    {
        Workmanship.Of(8).IsExceptional.Should().BeTrue();
    }

    [Fact]
    public void IsExceptional_TrueForValue9()
    {
        Workmanship.Of(9).IsExceptional.Should().BeTrue();
    }

    [Fact]
    public void IsExceptional_TrueForValue10()
    {
        Workmanship.Of(10).IsExceptional.Should().BeTrue();
    }

    [Fact]
    public void IsExceptional_FalseForValue7()
    {
        Workmanship.Of(7).IsExceptional.Should().BeFalse();
    }

    [Fact]
    public void IsMasterwork_TrueOnlyForValue10()
    {
        Workmanship.Of(10).IsMasterwork.Should().BeTrue();
    }

    [Fact]
    public void IsMasterwork_FalseForValue9()
    {
        Workmanship.Of(9).IsMasterwork.Should().BeFalse();
    }

    [Fact]
    public void IsMasterwork_FalseForValue1()
    {
        Workmanship.Of(1).IsMasterwork.Should().BeFalse();
    }

    [Fact]
    public void Combine_TwoW5Items_Skill0_ProducesApproximatelyW5()
    {
        var a = Workmanship.Of(5);
        var b = Workmanship.Of(5);

        var result = Workmanship.Combine(a, b, 0);

        // base = (5+5)/2 = 5, skillBonus = 0/20 = 0, result = 5
        result.Value.Should().Be(5);
    }

    [Fact]
    public void Combine_TwoW9Items_HighSkill_ProducesW9OrW10()
    {
        var a = Workmanship.Of(9);
        var b = Workmanship.Of(9);

        var result = Workmanship.Combine(a, b, 20);

        // base = (9+9)/2 = 9, skillBonus = 20/20 = 1, result = round(10) = 10
        result.Value.Should().BeGreaterThanOrEqualTo(9);
        result.Value.Should().BeLessThanOrEqualTo(10);
    }

    [Fact]
    public void Combine_HighSkillBonusIsCappedAt10()
    {
        var a = Workmanship.Of(10);
        var b = Workmanship.Of(10);

        var result = Workmanship.Combine(a, b, 100);

        result.Value.Should().Be(10);
    }

    [Fact]
    public void Combine_LowWorkmanshipIsClamped()
    {
        var a = Workmanship.Of(1);
        var b = Workmanship.Of(1);

        var result = Workmanship.Combine(a, b, 0);

        result.Value.Should().Be(1);
    }
}
