using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class PlayerTests
{
    [Fact]
    public void Create_InitializesWithLevel1()
    {
        var player = Player.Create("TestPlayer", 42);

        player.Level.Should().Be(1);
    }

    [Fact]
    public void Create_InitializesWithHp100()
    {
        var player = Player.Create("TestPlayer", 42);

        player.CurrentHp.Should().Be(100);
        player.MaxHp.Should().Be(100);
    }

    [Fact]
    public void Create_InitializesAllReputationsAtUnknownTier()
    {
        var player = Player.Create("TestPlayer", 42);

        foreach (FactionId factionId in Enum.GetValues<FactionId>())
        {
            player.GetReputationTier(factionId).Should().Be(ReputationTier.Unknown,
                because: $"faction {factionId} should start at Unknown");
        }
    }

    [Fact]
    public void Create_Initializes7FactionReputations()
    {
        var player = Player.Create("TestPlayer", 42);

        player.Reputations.Should().HaveCount(7);
    }

    [Fact]
    public void GainExperience_IncreasesExperience()
    {
        var player = Player.Create("TestPlayer", 42);

        player.GainExperience(50);

        player.Experience.Should().Be(50);
    }

    [Fact]
    public void GainExperience_LevelsUpWhenThresholdExceeded()
    {
        var player = Player.Create("TestPlayer", 42);

        // Level 2 requires experience such that sqrt(exp/100) >= 1, i.e. exp >= 100
        player.GainExperience(100);

        player.Level.Should().Be(2);
    }

    [Fact]
    public void GainExperience_RaisesPlayerLeveledUpEvent_WhenLevelUp()
    {
        var player = Player.Create("TestPlayer", 42);

        player.GainExperience(100);

        player.DomainEvents.Should().ContainSingle(e => e is PlayerLeveledUpEvent);
        var evt = (PlayerLeveledUpEvent)player.DomainEvents.First(e => e is PlayerLeveledUpEvent);
        evt.PlayerId.Should().Be(player.Id);
        evt.NewLevel.Should().Be(2);
    }

    [Fact]
    public void GainExperience_DoesNotRaiseLevelUpEvent_WhenNoLevelChange()
    {
        var player = Player.Create("TestPlayer", 42);

        player.GainExperience(50);

        player.DomainEvents.Should().NotContain(e => e is PlayerLeveledUpEvent);
    }

    [Fact]
    public void AdjustReputation_Thornwood_Plus1200_SetsTiersCorrectly()
    {
        var player = Player.Create("TestPlayer", 42);

        player.AdjustReputation(FactionId.ThornwoodCovens, 1200);

        player.GetReputationTier(FactionId.ThornwoodCovens).Should().Be(ReputationTier.Trusted);
    }

    [Fact]
    public void AdjustReputation_Thornwood_Plus1200_ReducesHouseCaervornBy600()
    {
        var player = Player.Create("TestPlayer", 42);

        player.AdjustReputation(FactionId.ThornwoodCovens, 1200);

        // Tension multiplier = -0.5, so HouseCaervorn gets delta = (int)(1200 * -0.5) = -600
        // Score for HouseCaervorn = -600, which is Hostile
        var caervornRep = player.Reputations.First(r => r.FactionId == FactionId.HouseCaervorn);
        caervornRep.Score.Points.Should().Be(-600);
    }

    [Fact]
    public void UnlockPortal_AddsToUnlockedPortals()
    {
        var player = Player.Create("TestPlayer", 42);

        player.UnlockPortal(WorldId.Aeldran);

        player.UnlockedPortals.Should().Contain(WorldId.Aeldran);
    }

    [Fact]
    public void UnlockPortal_RaisesPortalUnlockedEvent()
    {
        var player = Player.Create("TestPlayer", 42);

        player.UnlockPortal(WorldId.Aeldran);

        player.DomainEvents.Should().ContainSingle(e => e is PortalUnlockedEvent);
        var evt = (PortalUnlockedEvent)player.DomainEvents.First(e => e is PortalUnlockedEvent);
        evt.PlayerId.Should().Be(player.Id);
        evt.WorldId.Should().Be(WorldId.Aeldran);
    }

    [Fact]
    public void UnlockPortal_SecondCallSameWorld_DoesNotDuplicate()
    {
        var player = Player.Create("TestPlayer", 42);

        player.UnlockPortal(WorldId.Aeldran);
        player.UnlockPortal(WorldId.Aeldran);

        player.UnlockedPortals.Should().HaveCount(1);
    }

    [Fact]
    public void UnlockPortal_SecondCallSameWorld_DoesNotRaiseEventAgain()
    {
        var player = Player.Create("TestPlayer", 42);

        player.UnlockPortal(WorldId.Aeldran);
        player.UnlockPortal(WorldId.Aeldran);

        player.DomainEvents.Count(e => e is PortalUnlockedEvent).Should().Be(1);
    }

    [Fact]
    public void TryAddActiveCompanion_ReturnsTrueForFirst3()
    {
        var player = Player.Create("TestPlayer", 42);

        var r1 = player.TryAddActiveCompanion(Guid.NewGuid());
        var r2 = player.TryAddActiveCompanion(Guid.NewGuid());
        var r3 = player.TryAddActiveCompanion(Guid.NewGuid());

        r1.Should().BeTrue();
        r2.Should().BeTrue();
        r3.Should().BeTrue();
    }

    [Fact]
    public void TryAddActiveCompanion_ReturnsFalseOnFourth()
    {
        var player = Player.Create("TestPlayer", 42);
        player.TryAddActiveCompanion(Guid.NewGuid());
        player.TryAddActiveCompanion(Guid.NewGuid());
        player.TryAddActiveCompanion(Guid.NewGuid());

        var result = player.TryAddActiveCompanion(Guid.NewGuid());

        result.Should().BeFalse();
    }

    [Fact]
    public void SpendWeave_DepletesWeaveCorrectly()
    {
        var player = Player.Create("TestPlayer", 42);
        var initialMax = player.Weave.Maximum;

        player.SpendWeave(30);

        player.Weave.Current.Should().Be(initialMax - 30);
    }

    [Fact]
    public void SpendWeave_RaisesWeaveCriticalEvent_WhenWeaveCritical()
    {
        var player = Player.Create("TestPlayer", 42);
        // Weave starts at 100/100. Need to spend 91+ to go <= 9 (critical is <= 10%)

        player.SpendWeave(91); // 9 remaining = 9% which is <= 10%

        player.DomainEvents.Should().Contain(e => e is WeaveCriticalEvent);
    }

    [Fact]
    public void SpendWeave_DoesNotRaiseCriticalEvent_WhenNotCritical()
    {
        var player = Player.Create("TestPlayer", 42);

        player.SpendWeave(50); // 50 remaining = 50%

        player.DomainEvents.Should().NotContain(e => e is WeaveCriticalEvent);
    }

    [Fact]
    public void RevealMagicAffinity_SetsElementAndPolarity()
    {
        var player = Player.Create("TestPlayer", 42);

        player.RevealMagicAffinity(MagicElement.Fire, MagicPolarity.Shaping);

        player.PrimaryElement.Should().Be(MagicElement.Fire);
        player.Polarity.Should().Be(MagicPolarity.Shaping);
        player.ElementRevealed.Should().BeTrue();
        player.PolarityRevealed.Should().BeTrue();
    }

    [Fact]
    public void RevealMagicAffinity_RaisesMagicAffinityRevealedEvent()
    {
        var player = Player.Create("TestPlayer", 42);

        player.RevealMagicAffinity(MagicElement.Fire, MagicPolarity.Shaping);

        player.DomainEvents.Should().ContainSingle(e => e is MagicAffinityRevealedEvent);
        var evt = (MagicAffinityRevealedEvent)player.DomainEvents.First(e => e is MagicAffinityRevealedEvent);
        evt.Element.Should().Be(MagicElement.Fire);
        evt.Polarity.Should().Be(MagicPolarity.Shaping);
    }

    [Fact]
    public void RevealMagicAffinity_SecondCall_DoesNothing()
    {
        var player = Player.Create("TestPlayer", 42);
        player.RevealMagicAffinity(MagicElement.Fire, MagicPolarity.Shaping);
        player.ClearDomainEvents();

        player.RevealMagicAffinity(MagicElement.Water, MagicPolarity.Unmaking);

        // Should still be Fire/Shaping
        player.PrimaryElement.Should().Be(MagicElement.Fire);
        player.Polarity.Should().Be(MagicPolarity.Shaping);
        player.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void AccumulateWyrdTangle_IncreasesWyrdTangle()
    {
        var player = Player.Create("TestPlayer", 42);

        player.AccumulateWyrdTangle(25);

        player.WyrdTangle.Should().Be(25);
    }

    [Fact]
    public void ResolveWyrdTangle_DecreasesWyrdTangle()
    {
        var player = Player.Create("TestPlayer", 42);
        player.AccumulateWyrdTangle(50);

        player.ResolveWyrdTangle(20);

        player.WyrdTangle.Should().Be(30);
    }

    [Fact]
    public void ResolveWyrdTangle_NeverGoesBelowZero()
    {
        var player = Player.Create("TestPlayer", 42);
        player.AccumulateWyrdTangle(10);

        player.ResolveWyrdTangle(50);

        player.WyrdTangle.Should().Be(0);
    }

    [Fact]
    public void DomainEvents_ClearedByClearDomainEvents()
    {
        var player = Player.Create("TestPlayer", 42);
        player.GainExperience(100); // produces a level-up event

        player.ClearDomainEvents();

        player.DomainEvents.Should().BeEmpty();
    }
}
