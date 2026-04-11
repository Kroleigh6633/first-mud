using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.IntegrationTests.Fixtures;
using FluentAssertions;

namespace FirstMud.IntegrationTests.Repositories;

[Collection("SqlServer")]
[Trait("Category", "Integration")]
public sealed class ZoneRepositoryTests
{
    private readonly SqlServerFixture _fixture;

    public ZoneRepositoryTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAndGet_RoundTripsAsciiSymbolAndLootTableIds()
    {
        var uniqueZoneId = Math.Abs(Guid.NewGuid().GetHashCode() % 900000) + 100000;

        await using var writeCtx = _fixture.CreateContext();

        var zone = Zone.Create(
            WorldId.Aeldran,
            uniqueZoneId,
            "Test Zone",
            "A zone created by an integration test.",
            "%",
            dangerLevel: 5,
            lootTableIds: ["LOOT_A", "LOOT_B"]);

        await writeCtx.Zones.AddAsync(zone);
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var loaded = await readCtx.Zones
            .FirstOrDefaultAsync(z => z.WorldId == WorldId.Aeldran && z.ZoneId == uniqueZoneId);

        loaded.Should().NotBeNull();
        loaded!.AsciiSymbol.Should().Be("%");
        loaded.LootTableIds.Should().BeEquivalentTo(["LOOT_A", "LOOT_B"]);
        loaded.DangerLevel.Should().Be(5);
    }

    [Fact]
    public async Task GetByWorld_ReturnsAllZonesInThatWorld()
    {
        var id1 = Math.Abs(Guid.NewGuid().GetHashCode() % 900000) + 200000;
        var id2 = id1 + 1;

        await using var writeCtx = _fixture.CreateContext();
        await writeCtx.Zones.AddRangeAsync(
            Zone.Create(WorldId.Aeldran, id1, "Multi Zone A", "Desc A", "a", dangerLevel: 1),
            Zone.Create(WorldId.Aeldran, id2, "Multi Zone B", "Desc B", "b", dangerLevel: 2));
        await writeCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();
        var zones = await readCtx.Zones
            .Where(z => z.WorldId == WorldId.Aeldran)
            .ToListAsync();

        zones.Should().Contain(z => z.ZoneId == id1, because: "zone A was just inserted");
        zones.Should().Contain(z => z.ZoneId == id2, because: "zone B was just inserted");
        zones.Should().OnlyContain(z => z.WorldId == WorldId.Aeldran,
            because: "the query filtered to Aeldran");
    }
}
