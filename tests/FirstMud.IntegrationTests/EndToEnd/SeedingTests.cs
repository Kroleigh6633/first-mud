using FirstMud.Domain.Enums;
using FirstMud.GameServer.Services;
using FirstMud.Infrastructure.Neo4j;
using FirstMud.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FirstMud.IntegrationTests.EndToEnd;

/// <summary>
/// End-to-end tests that exercise <see cref="StartupSeeder"/> against real
/// SQL Server and Neo4j containers.
/// </summary>
[Collection("FullStack")]
[Trait("Category", "Integration")]
public sealed class SeedingTests
{
    private readonly FullStackFixture _fixture;

    public SeedingTests(FullStackFixture fixture)
    {
        _fixture = fixture;
    }

    private StartupSeeder CreateSeeder()
    {
        var db = _fixture.SqlServer.CreateContext();
        var driverWrapper = _fixture.Neo4j.CreateDriverWrapper();
        var questRepo = new QuestGraphRepository(driverWrapper);
        var loreSeeder = new LoreSeeder(driverWrapper);
        var logger = NullLogger<StartupSeeder>.Instance;

        return new StartupSeeder(db, questRepo, loreSeeder, logger);
    }

    [Fact]
    public async Task RunSeedTwice_ProducesSameNumberOfZonesAndRecipes()
    {
        // First run
        await CreateSeeder().SeedAsync(CancellationToken.None);

        await using var ctx1 = _fixture.SqlServer.CreateContext();
        var zoneCount1 = await ctx1.Zones.CountAsync();
        var recipeCount1 = await ctx1.Recipes.CountAsync();

        // Second run — seeder is idempotent via AnyAsync guards
        await CreateSeeder().SeedAsync(CancellationToken.None);

        await using var ctx2 = _fixture.SqlServer.CreateContext();
        var zoneCount2 = await ctx2.Zones.CountAsync();
        var recipeCount2 = await ctx2.Recipes.CountAsync();

        zoneCount2.Should().Be(zoneCount1,
            because: "re-running the seeder should not add duplicate zones");
        recipeCount2.Should().Be(recipeCount1,
            because: "re-running the seeder should not add duplicate recipes");
    }

    [Fact]
    public async Task AfterSeeding_DevPlayerKiraAshwoodExistsWithSeed42()
    {
        await CreateSeeder().SeedAsync(CancellationToken.None);

        await using var ctx = _fixture.SqlServer.CreateContext();
        var kira = await ctx.Players.FirstOrDefaultAsync(p => p.Name == "Kira Ashwood");

        kira.Should().NotBeNull(because: "StartupSeeder creates dev player 'Kira Ashwood'");
        kira!.CraftingSeed.Should().Be(42, because: "craftingSeed is hard-coded to 42 in the seeder");
    }

    [Fact]
    public async Task AfterSeeding_NineAeldranZonesArePresent()
    {
        await CreateSeeder().SeedAsync(CancellationToken.None);

        await using var ctx = _fixture.SqlServer.CreateContext();
        var aeldranZoneCount = await ctx.Zones
            .CountAsync(z => z.WorldId == WorldId.Aeldran && z.ZoneId <= 9);

        aeldranZoneCount.Should().Be(9,
            because: "StartupSeeder seeds exactly 9 Aeldran zones with ZoneIds 1–9");
    }
}
