using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FirstMud.Tests.Content;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace FirstMud.Tests.Application;

/// <summary>
/// Tests for boss-drop wiring in <see cref="LootService"/> (task #140).
/// Verifies that:
///   * Boss monsters roll bonus <c>bossDrops[]</c> entries at the authored
///     <c>dropChance</c>, in addition to the standard biome/equipment pool.
///   * Non-boss monsters skip the boss-drop path entirely.
///   * Bosses without any authored bossDrops drop no bonus items (and no
///     crash).
///   * Rolls can be driven with an injected RNG for deterministic counts.
/// </summary>
public class LootServiceBossDropsTests
{
    private static LootService BuildService(IContentProvider content, IItemRepository items)
    {
        var players = Substitute.For<IPlayerRepository>();
        var salvage = new SalvageService(players, items, NullLogger<SalvageService>.Instance, content);
        return new LootService(items, salvage, content, NullLogger<LootService>.Instance);
    }

    private static IContentProvider RealContent() =>
        new ContentProvider(ContentRootResolver.Resolve());

    [Fact]
    public async Task RollBossDrops_NonBossMonster_ReturnsEmpty_NoPersistence()
    {
        var content = RealContent();
        var items = Substitute.For<IItemRepository>();
        var svc = BuildService(content, items);

        // stray-dog is a tier-0 plains non-boss.
        var awarded = await svc.RollBossDropsAsync(
            new[] { "stray-dog" },
            ownerId: System.Guid.NewGuid(),
            originWorld: WorldId.Aeldran,
            currentInventoryCount: 0,
            maxInventorySlots: 100,
            ct: CancellationToken.None,
            rng: new System.Random(42));

        awarded.Should().BeEmpty();
        await items.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollBossDrops_EmberDrake_AcrossManyRolls_ApproximatesAuthoredRate()
    {
        var content = RealContent();
        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAndNameAsync(Arg.Any<System.Guid>(), Arg.Any<string>(),
            Arg.Any<ItemCategory>(), Arg.Any<CancellationToken>())
            .Returns((Item?)null);

        var svc = BuildService(content, items);

        // Ember Drake bossDrop: Wyrdforged Core @ 0.25. Run N independent
        // rolls (seeded) and verify the observed rate is within ±7 pts of the
        // authored 25% — tolerant enough for test stability, tight enough to
        // catch a dead code path.
        const int N = 400;
        int hits = 0;
        var rng = new System.Random(42);
        for (int i = 0; i < N; i++)
        {
            var awarded = await svc.RollBossDropsAsync(
                new[] { "ember-drake" },
                ownerId: System.Guid.NewGuid(),
                originWorld: WorldId.Aeldran,
                currentInventoryCount: 0,
                maxInventorySlots: 100,
                ct: CancellationToken.None,
                rng: rng);
            if (awarded.Count > 0) hits++;
        }

        double rate = (double)hits / N;
        rate.Should().BeGreaterThan(0.18).And.BeLessThan(0.32,
            $"ember-drake Wyrdforged Core drop rate should cluster around authored 0.25 (observed {rate:P1} over {N} rolls)");
    }

    [Fact]
    public async Task RollBossDrops_BossWithNoAuthoredEntries_IsNoOp()
    {
        // Use a synthetic content root where a boss exists but has no bossDrops
        // authored — verifies the iteration handles the empty case without
        // throwing or persisting.
        var dir = System.IO.Directory.CreateTempSubdirectory("fm-bossdrop-empty-");
        ContentProviderTests.WriteAllPrerequisitesExcept(dir.FullName, "monsters");
        System.IO.File.WriteAllText(System.IO.Path.Combine(dir.FullName, "monsters.json"), """
        {
          "abilities": [
            { "id": "claw", "name": "Claw", "basePower": 6, "weaveCost": 0, "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
          ],
          "monsters": [
            { "id": "synthetic-boss", "name": "Synthetic Boss", "biome": "plains", "tier": 3, "hp": 100, "speed": 5, "level": 4, "element": "Earth", "abilities": ["claw"], "isBoss": true }
          ],
          "bossDrops": []
        }
        """);

        var content = new ContentProvider(dir.FullName);
        var items = Substitute.For<IItemRepository>();
        var svc = BuildService(content, items);

        var awarded = await svc.RollBossDropsAsync(
            new[] { "synthetic-boss" },
            ownerId: System.Guid.NewGuid(),
            originWorld: WorldId.Aeldran,
            currentInventoryCount: 0,
            maxInventorySlots: 100,
            ct: CancellationToken.None,
            rng: new System.Random(1));

        awarded.Should().BeEmpty();
        await items.DidNotReceive().AddAsync(Arg.Any<Item>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RollBossDrops_ByEnemyNames_ResolvesAndFilters()
    {
        var content = RealContent();
        var items = Substitute.For<IItemRepository>();
        items.GetByOwnerAndNameAsync(Arg.Any<System.Guid>(), Arg.Any<string>(),
            Arg.Any<ItemCategory>(), Arg.Any<CancellationToken>())
            .Returns((Item?)null);

        var svc = BuildService(content, items);

        // "Ember Drake" (boss) + "Stray Dog" (non-boss) + "Not A Real Monster" (unknown).
        // Force a deterministic hit by using a custom rng that always returns 0 (< any dropChance).
        var rng = new AlwaysZeroRandom();
        var awarded = await svc.RollBossDropsByEnemyNamesAsync(
            new[] { "Ember Drake", "Stray Dog", "Not A Real Monster" },
            ownerId: System.Guid.NewGuid(),
            originWorld: WorldId.Aeldran,
            currentInventoryCount: 0,
            maxInventorySlots: 100,
            ct: CancellationToken.None,
            rng: rng);

        // Only Ember Drake should contribute — its single authored drop is Wyrdforged Core.
        awarded.Should().ContainSingle()
            .Which.Name.Should().Be("Wyrdforged Core");
    }

    [Fact]
    public void ContentProvider_LoadsBossDrops_FromRealContentFile()
    {
        var content = RealContent();

        var ember = content.GetBossDropsFor("ember-drake");
        ember.Should().ContainSingle(b => b.ItemName == "Wyrdforged Core" && b.DropChance == 0.25);

        var frost = content.GetBossDropsFor("frost-giant");
        frost.Should().ContainSingle(b => b.ItemName == "Starforged Ingot" && b.DropChance == 0.30);

        content.GetBossDropsFor("stray-dog").Should().BeEmpty();

        content.GetMonster("ember-drake")!.IsBoss.Should().BeTrue();
        content.GetMonster("stray-dog")!.IsBoss.Should().BeFalse();
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="System.Random"/> that always returns 0 for <c>NextDouble()</c>,
    /// so every <c>rng.NextDouble() &lt; dropChance</c> predicate evaluates true
    /// for any positive <c>dropChance</c>. Used to force deterministic hit paths.
    /// </summary>
    private sealed class AlwaysZeroRandom : System.Random
    {
        public override double NextDouble() => 0d;
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
    }
}
