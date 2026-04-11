using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class ReputationServiceTests
{
    private static Player CreatePlayer() => Player.Create("Tester", 42);

    // ---- tier advancement ----

    [Fact]
    public async Task AdjustReputationAsync_GainingEnoughPoints_AdvancesToTrustedTier()
    {
        // Known tier is [1, 1000), Trusted tier is [1000, 3000).
        // Starting from 0 (Unknown), adding 1000 crosses into Trusted.
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        await svc.AdjustReputationAsync(player.Id, FactionId.AshenCourt, 1000);

        player.GetReputationTier(FactionId.AshenCourt).Should().Be(ReputationTier.Trusted);
    }

    [Fact]
    public async Task AdjustReputationAsync_LosingPointsBelowKnownThreshold_DemotesToKnownOrBelow()
    {
        // Start at Known (score 500), subtract 600 → score −100 → Wary tier
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        // First gain some rep
        await svc.AdjustReputationAsync(player.Id, FactionId.AshenCourt, 500);
        // Then lose more
        await svc.AdjustReputationAsync(player.Id, FactionId.AshenCourt, -600);

        player.GetReputationTier(FactionId.AshenCourt).Should().Be(ReputationTier.Wary);
    }

    [Fact]
    public async Task AdjustReputationAsync_PlayerNotFound_ThrowsInvalidOperationException()
    {
        var players = Substitute.For<IPlayerRepository>();
        players.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((Player?)null);

        var svc = new ReputationService(players);

        Func<Task> act = () => svc.AdjustReputationAsync(Guid.NewGuid(), FactionId.AshenCourt, 100);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public async Task GetTierAsync_ReturnsCorrectTier_ForZeroPoints()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        var tier = await svc.GetTierAsync(player.Id, FactionId.AshenCourt);

        tier.Should().Be(ReputationTier.Unknown);
    }

    // ---- faction tension propagation ----

    [Fact]
    public async Task ApplyQuestReputationRewardsAsync_GainingCaervornRep_DecreasesThornwoodBy50Percent()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        // Apply 200 rep to HouseCaervorn
        await svc.ApplyQuestReputationRewardsAsync(player.Id, FactionId.HouseCaervorn, 200);

        // Thornwood should have dropped by 50% of 200 = -100 (penalty)
        var thornwoodRep = player.Reputations.First(r => r.FactionId == FactionId.ThornwoodCovens);
        thornwoodRep.Score.Points.Should().Be(-100);
    }

    [Fact]
    public async Task ApplyQuestReputationRewardsAsync_GainingGolvariRep_DecreasesGravenguardBy40Percent()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        await svc.ApplyQuestReputationRewardsAsync(player.Id, FactionId.Golvari, 200);

        // Gravenguard should have dropped by 40% of 200 = -80
        var gravenguardRep = player.Reputations.First(r => r.FactionId == FactionId.Gravenguard);
        gravenguardRep.Score.Points.Should().Be(-80);
    }

    [Fact]
    public async Task ApplyQuestReputationRewardsAsync_GainingFairgeanRep_DecreasesEmeraldCompactBy20Percent()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        await svc.ApplyQuestReputationRewardsAsync(player.Id, FactionId.Fairgean, 200);

        // EmeraldCompact should have dropped by 20% of 200 = -40
        var compactRep = player.Reputations.First(r => r.FactionId == FactionId.EmeraldCompact);
        compactRep.Score.Points.Should().Be(-40);
    }

    [Fact]
    public async Task ApplyQuestReputationRewardsAsync_NonTensionFaction_DoesNotAffectOtherFactions()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        // AshenCourt has no tension pairs defined
        await svc.ApplyQuestReputationRewardsAsync(player.Id, FactionId.AshenCourt, 500);

        // All other factions should remain at 0
        var others = player.Reputations.Where(r => r.FactionId != FactionId.AshenCourt);
        others.Should().OnlyContain(r => r.Score.Points == 0);
    }

    [Fact]
    public async Task MeetsTierRequirementAsync_ReturnsFalse_WhenTierBelowRequired()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        // Player starts at Unknown, check if meets Trusted
        var result = await svc.MeetsTierRequirementAsync(player.Id, FactionId.AshenCourt, ReputationTier.Trusted);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MeetsTierRequirementAsync_ReturnsTrue_WhenTierMeetsOrExceedsRequired()
    {
        var players = Substitute.For<IPlayerRepository>();
        var player = CreatePlayer();
        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new ReputationService(players);

        // Gain enough for Trusted
        await svc.AdjustReputationAsync(player.Id, FactionId.AshenCourt, 1000);

        var result = await svc.MeetsTierRequirementAsync(player.Id, FactionId.AshenCourt, ReputationTier.Known);

        result.Should().BeTrue();
    }
}
