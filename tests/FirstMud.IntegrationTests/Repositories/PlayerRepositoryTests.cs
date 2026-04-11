using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FirstMud.IntegrationTests.Fixtures;
using FluentAssertions;

namespace FirstMud.IntegrationTests.Repositories;

/// <summary>
/// Integration tests for player persistence via <see cref="GameDbContext"/>.
/// Exercises the same queries that <c>PlayerRepository</c> uses internally.
/// </summary>
[Collection("SqlServer")]
[Trait("Category", "Integration")]
public sealed class PlayerRepositoryTests
{
    private readonly SqlServerFixture _fixture;

    public PlayerRepositoryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAndGetById_RehydratesPlayerWithValueObjects()
    {
        await using var writeCtx = _fixture.CreateContext();

        var player = Player.Create("Aldric_" + Guid.NewGuid().ToString("N")[..8], craftingSeed: 7);
        await writeCtx.Players.AddAsync(player);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var loaded = await readCtx.Players
            .Include(p => p.Reputations)
            .FirstOrDefaultAsync(p => p.Id == player.Id);

        loaded.Should().NotBeNull();
        loaded!.Id.Should().Be(player.Id);
        loaded.Name.Should().Be(player.Name);
        loaded.CraftingSeed.Should().Be(7);
        loaded.Weave.Current.Should().Be(player.Weave.Current);
        loaded.Weave.Maximum.Should().Be(player.Weave.Maximum);
        loaded.Position.World.Should().Be(WorldId.Aeldran);
        loaded.Position.ZoneId.Should().Be(1);
    }

    [Fact]
    public async Task UpdateWeave_PersistsChangedWeaveValues()
    {
        await using var writeCtx = _fixture.CreateContext();

        var player = Player.Create("Mira_" + Guid.NewGuid().ToString("N")[..8], craftingSeed: 3);
        await writeCtx.Players.AddAsync(player);
        await writeCtx.SaveChangesAsync();

        await using var updateCtx = _fixture.CreateContext();
        var toUpdate = await updateCtx.Players
            .Include(p => p.Reputations)
            .FirstAsync(p => p.Id == player.Id);
        toUpdate.SpendWeave(40);
        updateCtx.Players.Update(toUpdate);
        await updateCtx.SaveChangesAsync();

        await using var verifyCtx = _fixture.CreateContext();
        var verified = await verifyCtx.Players.FirstAsync(p => p.Id == player.Id);

        verified.Weave.Current.Should().Be(60);
    }

    [Fact]
    public async Task GetByName_ReturnsCorrectPlayer()
    {
        await using var writeCtx = _fixture.CreateContext();

        var uniqueName = "Drest_" + Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create(uniqueName, craftingSeed: 99);
        await writeCtx.Players.AddAsync(player);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var found = await readCtx.Players
            .Include(p => p.Reputations)
            .FirstOrDefaultAsync(p => p.Name == uniqueName);

        found.Should().NotBeNull();
        found!.Id.Should().Be(player.Id);
        found.Name.Should().Be(uniqueName);
    }

    [Fact]
    public async Task ReputationNavigation_PersistsAndLoadsAllFactionRows()
    {
        await using var writeCtx = _fixture.CreateContext();

        var player = Player.Create("Elowen_" + Guid.NewGuid().ToString("N")[..8], craftingSeed: 11);
        player.AdjustReputation(FactionId.ThornwoodCovens, 500);
        await writeCtx.Players.AddAsync(player);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var loaded = await readCtx.Players
            .Include(p => p.Reputations)
            .FirstOrDefaultAsync(p => p.Id == player.Id);

        loaded.Should().NotBeNull();
        loaded!.Reputations.Should().HaveCount(7,
            because: "Player.Create seeds one reputation row per FactionId enum value");

        var thornRep = loaded.Reputations.FirstOrDefault(r => r.FactionId == FactionId.ThornwoodCovens);
        thornRep.Should().NotBeNull();
        thornRep!.Score.Points.Should().Be(500);
    }
}
