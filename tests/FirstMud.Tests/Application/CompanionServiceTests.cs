using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FluentAssertions;
using NSubstitute;

namespace FirstMud.Tests.Application;

public class CompanionServiceTests
{
    private static Companion CreateCompanion(Guid? ownerId = null, bool active = false)
    {
        var companion = Companion.Create(
            ownerId ?? Guid.NewGuid(),
            "TestCompanion",
            CompanionType.Wildfolk,
            MagicElement.Fire);
        if (active)
            companion.SetActive(true);
        return companion;
    }

    private static Player CreatePlayer() => Player.Create("Tester", 42);

    // ---- RecordCombatUsageAsync ----

    [Fact]
    public async Task RecordCombatUsageAsync_ValidCompanion_PersistsUpdatedCompanion()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var companion = CreateCompanion();
        companionRepo.GetByIdAsync(companion.Id, Arg.Any<CancellationToken>()).Returns(companion);

        var svc = new CompanionService(companionRepo, playerRepo);

        await svc.RecordCombatUsageAsync(companion.Id, 50);

        companion.UsageCounter.Should().Be(50);
        await companionRepo.Received(1).UpdateAsync(companion, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordCombatUsageAsync_CompanionNotFound_ThrowsInvalidOperationException()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        companionRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Companion?)null);

        var svc = new CompanionService(companionRepo, playerRepo);

        Func<Task> act = () => svc.RecordCombatUsageAsync(Guid.NewGuid(), 50);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not found*");
    }

    // ---- TickDriftAsync ----

    [Fact]
    public async Task TickDriftAsync_CallsAccumulateDrift_OnAllCompanionsOwnedByPlayer()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var ownerId = Guid.NewGuid();
        var c1 = CreateCompanion(ownerId);
        var c2 = CreateCompanion(ownerId);
        IReadOnlyList<Companion> companions = new List<Companion> { c1, c2 }.AsReadOnly();
        companionRepo.GetByOwnerAsync(ownerId, Arg.Any<CancellationToken>()).Returns(companions);

        var svc = new CompanionService(companionRepo, playerRepo);

        await svc.TickDriftAsync(ownerId, 10f);

        // Both companions are inactive by default → drift = 10 * 0.5 = 5
        c1.DriftAccumulator.Should().BeApproximately(5f, 0.001f);
        c2.DriftAccumulator.Should().BeApproximately(5f, 0.001f);
        await companionRepo.Received(1).UpdateManyAsync(Arg.Any<IEnumerable<Companion>>(), Arg.Any<CancellationToken>());
    }

    // ---- AttemptCaptureAsync ----

    [Fact]
    public async Task AttemptCaptureAsync_RollAbove60_ReturnsNewCompanion()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        playerRepo.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new CompanionService(companionRepo, playerRepo);

        var result = await svc.AttemptCaptureAsync(player.Id, "Goblin", MagicElement.Earth, 61);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Goblin");
        result.Type.Should().Be(CompanionType.CapturedMonster);
        await companionRepo.Received(1).AddAsync(result, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AttemptCaptureAsync_RollAtOrBelow60_ReturnsNull()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        playerRepo.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);

        var svc = new CompanionService(companionRepo, playerRepo);

        var result = await svc.AttemptCaptureAsync(player.Id, "Goblin", MagicElement.Earth, 60);

        result.Should().BeNull();
        await companionRepo.DidNotReceive().AddAsync(Arg.Any<Companion>(), Arg.Any<CancellationToken>());
    }

    // ---- AssignToActiveSlotAsync ----

    [Fact]
    public async Task AssignToActiveSlotAsync_WhenActiveLimit3Reached_ReturnsFalse()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();

        // Fill all 3 active slots
        player.TryAddActiveCompanion(Guid.NewGuid());
        player.TryAddActiveCompanion(Guid.NewGuid());
        player.TryAddActiveCompanion(Guid.NewGuid());

        var newCompanionId = Guid.NewGuid();
        var newCompanion = CreateCompanion(player.Id);

        playerRepo.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        companionRepo.GetByIdAsync(newCompanionId, Arg.Any<CancellationToken>()).Returns(newCompanion);

        var svc = new CompanionService(companionRepo, playerRepo);

        var result = await svc.AssignToActiveSlotAsync(player.Id, newCompanionId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task AssignToActiveSlotAsync_WhenSlotAvailable_SetsCompanionActiveAndReturnsTrue()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        var companion = CreateCompanion(player.Id);

        playerRepo.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        companionRepo.GetByIdAsync(companion.Id, Arg.Any<CancellationToken>()).Returns(companion);

        var svc = new CompanionService(companionRepo, playerRepo);

        var result = await svc.AssignToActiveSlotAsync(player.Id, companion.Id);

        result.Should().BeTrue();
        companion.IsActive.Should().BeTrue();
        player.ActiveCompanionIds.Should().Contain(companion.Id);
        await playerRepo.Received(1).UpdateAsync(player, Arg.Any<CancellationToken>());
        await companionRepo.Received(1).UpdateAsync(companion, Arg.Any<CancellationToken>());
    }

    // ---- RemoveFromActiveSlotAsync ----

    [Fact]
    public async Task RemoveFromActiveSlotAsync_RemovesFromPlayerSlotAndSetsInactive()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var player = CreatePlayer();
        var companion = CreateCompanion(player.Id, active: true);
        player.TryAddActiveCompanion(companion.Id);

        playerRepo.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        companionRepo.GetByIdAsync(companion.Id, Arg.Any<CancellationToken>()).Returns(companion);

        var svc = new CompanionService(companionRepo, playerRepo);

        await svc.RemoveFromActiveSlotAsync(player.Id, companion.Id);

        companion.IsActive.Should().BeFalse();
        player.ActiveCompanionIds.Should().NotContain(companion.Id);
        await playerRepo.Received(1).UpdateAsync(player, Arg.Any<CancellationToken>());
        await companionRepo.Received(1).UpdateAsync(companion, Arg.Any<CancellationToken>());
    }

    // ---- RecordWarningIgnoredAsync ----

    [Fact]
    public async Task RecordWarningIgnoredAsync_AfterThreeWarnings_MarksCompanionPermanentlyGone()
    {
        var companionRepo = Substitute.For<ICompanionRepository>();
        var playerRepo = Substitute.For<IPlayerRepository>();

        var companion = CreateCompanion();
        companionRepo.GetByIdAsync(companion.Id, Arg.Any<CancellationToken>()).Returns(companion);

        var svc = new CompanionService(companionRepo, playerRepo);

        // First two warnings — companion departs on the 3rd ignored warning (domain logic)
        await svc.RecordWarningIgnoredAsync(companion.Id);
        await svc.RecordWarningIgnoredAsync(companion.Id);
        await svc.RecordWarningIgnoredAsync(companion.Id); // this triggers departure

        // After 3 warnings on a Wildfolk companion it departs (IsActive=false)
        // Then the service marks it permanently gone if !IsActive && !IsPermanentlyGone
        companion.IsPermanentlyGone.Should().BeTrue();
        await companionRepo.Received(3).UpdateAsync(companion, Arg.Any<CancellationToken>());
    }
}
