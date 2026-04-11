using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class ElementMatchupTests
{
    [Fact]
    public void FireBeatsEarth_Returns1_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Fire, MagicElement.Earth)
            .Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public void EarthBeatsAir_Returns1_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Earth, MagicElement.Air)
            .Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public void AirBeatsWater_Returns1_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Air, MagicElement.Water)
            .Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public void WaterBeatsFire_Returns1_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Water, MagicElement.Fire)
            .Should().BeApproximately(1.5f, 0.001f);
    }

    [Fact]
    public void EarthLosesToFire_Returns0_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Earth, MagicElement.Fire)
            .Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void AirLosesToEarth_Returns0_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Air, MagicElement.Earth)
            .Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void WaterLosesToAir_Returns0_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Water, MagicElement.Air)
            .Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void FireLosesToWater_Returns0_5()
    {
        ElementMatchup.GetMultiplier(MagicElement.Fire, MagicElement.Water)
            .Should().BeApproximately(0.5f, 0.001f);
    }

    [Fact]
    public void SameElement_Returns1_0()
    {
        ElementMatchup.GetMultiplier(MagicElement.Fire, MagicElement.Fire)
            .Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void AetherAttacker_AlwaysReturns1_0()
    {
        ElementMatchup.GetMultiplier(MagicElement.Aether, MagicElement.Fire)
            .Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void AetherDefender_AlwaysReturns1_0()
    {
        ElementMatchup.GetMultiplier(MagicElement.Fire, MagicElement.Aether)
            .Should().BeApproximately(1.0f, 0.001f);
    }

    [Fact]
    public void AetherVsAether_Returns1_0()
    {
        ElementMatchup.GetMultiplier(MagicElement.Aether, MagicElement.Aether)
            .Should().BeApproximately(1.0f, 0.001f);
    }
}
