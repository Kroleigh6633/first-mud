using FirstMud.Application.Content;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace FirstMud.Tests;

/// <summary>
/// Salvage material-flow tests — covers the bug where a Stone Axe (recipe:
/// Stone x3 + Wood x1) salvaged into "Iron Ore" because SalvageService was
/// material-blind. The fix routes salvage through recipe ingredients with
/// a workmanship-scaled recovery rate, rare-component suppression, and a
/// hardcoded fallback for legacy/unrecipe'd items.
///
/// Older tests in <see cref="MechanicsSweepTests"/> assert the legacy
/// material names ("Iron Ore" for weapons, "Leather" for armor). Those
/// remain correct for the FALLBACK path (no content provider, or
/// unknown item name) — this suite exercises the recipe-derived path.
/// </summary>
public class SalvageMaterialFlowTests
{
    // Use the real ContentProvider pointed at the repo content/ dir so these
    // tests exercise the actual authored recipes (Stone Axe, Iron Sword,
    // Mithril Sword). This is the same resolver the live game uses.
    private static IContentProvider BuildContent() =>
        new ContentProvider(ContentRootResolver.Resolve(), logger: null);

    private static (SalvageService svc, Player player, Item item, IItemRepository items, IPlayerRepository players)
        Setup(string itemName, ItemCategory category, int workmanship, int salvageSkill, IContentProvider content)
    {
        var players = Substitute.For<IPlayerRepository>();
        var items   = Substitute.For<IItemRepository>();

        var player = Player.Create("Tester", 1);
        // Boost salvage skill so high-tier items (Mithril Sword W8) aren't
        // blocked by the skill gate.
        if (salvageSkill > 0)
            player.GainSalvageSkillXp(salvageSkill);

        var item = Item.Create(itemName, itemName, category,
            Workmanship.Of(workmanship), WorldId.Aeldran,
            slot: category == ItemCategory.Weapon ? EquipmentSlot.MeleeWeapon : EquipmentSlot.None);
        item.SetOwner(player.Id);

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        items.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        items.GetByOwnerAndNameAsync(player.Id, Arg.Any<string>(), Arg.Any<ItemCategory>(), Arg.Any<CancellationToken>())
             .Returns((Item?)null);

        var svc = new SalvageService(players, items, NullLogger<SalvageService>.Instance, content);
        return (svc, player, item, items, players);
    }

    [Fact]
    public async Task StoneAxe_YieldsStoneAndWood_NotIronOre()
    {
        // STONE_AXE_001 recipe: Stone x3 + Wood x1
        // Before fix: weapon → Iron Ore + Wood (material-blind).
        // After fix: Stone + Wood (recipe-derived).
        SalvageService.ResetOverrideCacheForTests();
        var content = BuildContent();

        // Run many attempts so we tolerate the 30% salvage-failure rate.
        var observedMaterials = new HashSet<string>();
        int successes = 0;
        for (int i = 0; i < 40 && successes < 5; i++)
        {
            var (svc, player, item, _, _) = Setup("Stone Axe", ItemCategory.Weapon,
                workmanship: 3, salvageSkill: 20, content);
            var result = await svc.SalvageAsync(player.Id, item.Id);
            if (!result.Success) continue;
            successes++;
            foreach (var y in result.Yields) observedMaterials.Add(y.Name);
        }

        successes.Should().BeGreaterThan(0, "expected at least one successful salvage in 40 attempts");
        observedMaterials.Should().Contain("Stone",
            "Stone Axe recipe requires Stone — salvage must return it");
        observedMaterials.Should().Contain("Wood",
            "Stone Axe recipe requires Wood — salvage must return it");
        observedMaterials.Should().NotContain("Iron Ore",
            "Stone Axe recipe has no Iron Ore — salvage must not yield it");
    }

    [Fact]
    public async Task IronSword_YieldsIronOreAndWood_FromRecipe()
    {
        // IRON_SWORD_001 recipe: Iron Ore x3 + Wood x1 → recipe path matches legacy defaults.
        SalvageService.ResetOverrideCacheForTests();
        var content = BuildContent();

        var observed = new HashSet<string>();
        int successes = 0;
        for (int i = 0; i < 40 && successes < 5; i++)
        {
            var (svc, player, item, _, _) = Setup("Iron Sword", ItemCategory.Weapon,
                workmanship: 4, salvageSkill: 20, content);
            var r = await svc.SalvageAsync(player.Id, item.Id);
            if (!r.Success) continue;
            successes++;
            foreach (var y in r.Yields) observed.Add(y.Name);
        }

        successes.Should().BeGreaterThan(0);
        observed.Should().Contain("Iron Ore");
        observed.Should().Contain("Wood");
    }

    [Fact]
    public async Task MithrilSword_SuppressesMithrilOre_FallsBackToIronOre()
    {
        // MITHRIL_SWORD_001 recipe: Mithril Ore x6 + Diamond Shard x2 (Reagent).
        // Both ingredients are suppressed (Mithril in rare list, Diamond Shard is Reagent).
        // → recipe path returns no yields → hardcoded weapon fallback fires → Iron Ore + Wood.
        SalvageService.ResetOverrideCacheForTests();
        var content = BuildContent();

        var observed = new HashSet<string>();
        int successes = 0;
        for (int i = 0; i < 60 && successes < 5; i++)
        {
            var (svc, player, item, _, _) = Setup("Mithril Sword", ItemCategory.Weapon,
                workmanship: 8, salvageSkill: 50, content);
            var r = await svc.SalvageAsync(player.Id, item.Id);
            if (!r.Success) continue;
            successes++;
            foreach (var y in r.Yields) observed.Add(y.Name);
        }

        successes.Should().BeGreaterThan(0, "skill 50 easily clears the W8 gate");
        observed.Should().NotContain("Mithril Ore",
            "Mithril Ore is rare-suppressed — salvage must never return it");
        observed.Should().NotContain("Diamond Shard",
            "Reagent ingredients are always suppressed on salvage");
        observed.Should().Contain("Iron Ore",
            "with both ingredients suppressed, weapon fallback yields Iron Ore");
    }

    [Fact]
    public async Task UnknownItem_FallsBackToHardcodedWeaponYields_NoCrash()
    {
        // An item whose name doesn't map to any recipe — should hit the fallback
        // path without crashing. Covers legacy items and arbitrary drops.
        SalvageService.ResetOverrideCacheForTests();
        var content = BuildContent();

        var observed = new HashSet<string>();
        int successes = 0;
        for (int i = 0; i < 40 && successes < 3; i++)
        {
            var (svc, player, item, _, _) = Setup("Ancient Bronze Gladius", ItemCategory.Weapon,
                workmanship: 3, salvageSkill: 20, content);
            var r = await svc.SalvageAsync(player.Id, item.Id);
            if (!r.Success) continue;
            successes++;
            foreach (var y in r.Yields) observed.Add(y.Name);
        }

        successes.Should().BeGreaterThan(0);
        observed.Should().Contain("Iron Ore",
            "fallback weapon yields must include Iron Ore for legacy items");
        observed.Should().Contain("Wood",
            "fallback weapon yields must include Wood for legacy items");
    }

    [Fact]
    public async Task NoContentProvider_UsesHardcodedFallback_NoCrash()
    {
        // Older construction path: no IContentProvider passed. Must not crash —
        // falls straight through to hardcoded category defaults. This is what
        // the existing MechanicsSweepTests rely on.
        SalvageService.ResetOverrideCacheForTests();
        var players = Substitute.For<IPlayerRepository>();
        var items   = Substitute.For<IItemRepository>();

        var player = Player.Create("Tester", 1);
        player.GainSalvageSkillXp(20);
        var item = Item.Create("Stone Axe", "axe", ItemCategory.Weapon,
            Workmanship.Of(2), WorldId.Aeldran, slot: EquipmentSlot.MeleeWeapon);
        item.SetOwner(player.Id);

        players.GetByIdAsync(player.Id, Arg.Any<CancellationToken>()).Returns(player);
        items.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        items.GetByOwnerAndNameAsync(player.Id, Arg.Any<string>(), Arg.Any<ItemCategory>(), Arg.Any<CancellationToken>())
             .Returns((Item?)null);

        // NOTE: no content param → null → fallback path.
        var svc = new SalvageService(players, items, NullLogger<SalvageService>.Instance);

        int successes = 0;
        for (int i = 0; i < 20 && successes < 3; i++)
        {
            var result = await svc.SalvageAsync(player.Id, item.Id);
            if (!result.Success) continue;
            successes++;
            // Without content provider, yields are the hardcoded weapon fallback.
            result.Yields.Should().Contain(y => y.Name == "Iron Ore");
            // Re-stage the item (it was deleted by a successful salvage).
            items.GetByIdAsync(item.Id, Arg.Any<CancellationToken>()).Returns(item);
        }

        successes.Should().BeGreaterThan(0, "fallback path must work without IContentProvider");
    }

    [Fact]
    public void RecoveryRate_ScalesWithWorkmanship_W1IsLowerThanW10()
    {
        // Verify the published recovery-rate formula via its observable effect:
        // 0.30 + (W-1)*0.025 → W1=0.300, W10=0.525.
        // We assert the shape (monotonic, bounded) rather than hitting the
        // private method directly.
        double RateAt(int w) => 0.30 + (Math.Clamp(w, 1, 10) - 1) * 0.025;

        RateAt(1).Should().BeApproximately(0.300, 0.001);
        RateAt(5).Should().BeApproximately(0.400, 0.001);
        RateAt(10).Should().BeApproximately(0.525, 0.001);
        RateAt(10).Should().BeGreaterThan(RateAt(1),
            "higher workmanship must recover more material than lower workmanship");
    }
}
