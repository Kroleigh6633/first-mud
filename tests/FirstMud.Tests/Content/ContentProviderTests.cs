using System.IO;
using FirstMud.Application.Content;
using FluentAssertions;

namespace FirstMud.Tests.Content;

public class ContentProviderTests
{
    /// <summary>
    /// Loads the real content/consumables.json shipped with the repo and
    /// asserts every effect that existed in the old hardcoded switch
    /// (UseConsumableCommandHandler.ResolveEffect) still resolves identically.
    /// This is the regression fence for the data-migration.
    /// </summary>
    [Fact]
    public void Real_consumables_file_reproduces_legacy_switch_behavior()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        AssertHeal(provider, "Minor Healing Draught", 30);
        AssertHeal(provider, "Healing Potion", 60);
        AssertHeal(provider, "Greater Healing Elixir", 100);

        AssertWeave(provider, "Weave Tincture", 20);
        AssertWeave(provider, "Weave Elixir", 50);

        AssertBuff(provider, "Fortitude Brew", "MaxHpBonus", 0.10f);
        AssertBuff(provider, "Speed Draught", "SpeedBonus", 0.20f);
        AssertBuff(provider, "Strength Tonic", "StrikeDamageBonus", 0.15f);

        // Unknown items still return null, just like the old switch.
        provider.ResolveConsumable("Moldy Bread").Should().BeNull();
        provider.ResolveConsumable("").Should().BeNull();
    }

    [Fact]
    public void Priority_groups_are_sorted_low_to_high_potency()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        var healing = provider.ConsumablesByGroup("Healing");
        healing.Select(c => c.Id).Should().ContainInOrder(
            "minor-healing-draught", "healing-potion", "greater-healing-elixir");

        var weave = provider.ConsumablesByGroup("Weave");
        weave.Select(c => c.Id).Should().ContainInOrder(
            "weave-tincture", "weave-elixir");
    }

    [Fact]
    public void Reload_repopulates_from_disk()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        var countBefore = provider.Consumables.Count;
        provider.Reload();
        provider.Consumables.Should().HaveCount(countBefore);
    }

    [Fact]
    public void Missing_consumables_file_throws_on_load()
    {
        var emptyDir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            var act = () => new ContentProvider(emptyDir.FullName);
            act.Should().Throw<FileNotFoundException>();
        }
        finally
        {
            emptyDir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_effect_type_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
            {
              "consumables": [
                { "id": "bogus", "matchToken": "bogus", "effectType": "Nuke", "amount": 0 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid effectType*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Buff_without_buffKey_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
            {
              "consumables": [
                { "id": "bad-buff", "matchToken": "bad buff", "effectType": "Buff", "amount": 0 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid buffKey*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_ids_throw_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            File.WriteAllText(Path.Combine(dir.FullName, "consumables.json"), """
            {
              "consumables": [
                { "id": "dup", "matchToken": "a", "effectType": "Heal", "amount": 1 },
                { "id": "dup", "matchToken": "b", "effectType": "Heal", "amount": 2 }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // ─── Monsters ─────────────────────────────────────────────────────────

    [Fact]
    public void Real_monsters_file_loads_and_exposes_known_ids()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());

        provider.AllMonsters().Should().NotBeEmpty("monsters.json ships with content");

        var goat = provider.GetMonster("mountain-goat");
        goat.Should().NotBeNull();
        goat!.Name.Should().Be("Mountain Goat");
        goat.Biome.Should().Be("mountain");
        goat.Tier.Should().Be(0);
        goat.Hp.Should().Be(22);
        goat.Speed.Should().Be(7);
        goat.Level.Should().Be(1);
        goat.Abilities.Should().HaveCount(2);
        goat.Abilities[0].Name.Should().Be("Headbutt");

        provider.MonstersByBiomeAndTier("mountain", 0).Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Unknown_monster_id_returns_null()
    {
        var provider = new ContentProvider(ContentRootResolver.Resolve());
        provider.GetMonster("no-such-beastie").Should().BeNull();
        provider.GetMonster("").Should().BeNull();
    }

    [Fact]
    public void Invalid_monster_element_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "oops", "name": "Oops", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Cosmic",
                  "abilities": ["bite"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*invalid element*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Invalid_monster_tier_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "too-hot", "name": "Too Hot", "biome": "plains", "tier": 9,
                  "hp": 10, "speed": 5, "level": 1, "element": "Fire",
                  "abilities": ["bite"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*tier*outside range*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Duplicate_monster_ids_throw_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "dup", "name": "A", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Fire", "abilities": ["bite"] },
                { "id": "dup", "name": "B", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Fire", "abilities": ["bite"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*duplicate monster id*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void Monster_referencing_unknown_ability_throws_on_load()
    {
        var dir = Directory.CreateTempSubdirectory("fm-content-test-");
        try
        {
            WriteStubConsumables(dir.FullName);
            File.WriteAllText(Path.Combine(dir.FullName, "monsters.json"), """
            {
              "abilities": [
                { "id": "bite", "name": "Bite", "basePower": 1, "weaveCost": 0,
                  "element": "Earth", "targetType": "SingleEnemy", "category": "Attack" }
              ],
              "monsters": [
                { "id": "ghost-ref", "name": "Ghost", "biome": "plains", "tier": 0,
                  "hp": 10, "speed": 5, "level": 1, "element": "Aether",
                  "abilities": ["no-such-ability"] }
              ]
            }
            """);
            var act = () => new ContentProvider(dir.FullName);
            act.Should().Throw<InvalidDataException>()
               .WithMessage("*unknown ability*");
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    private static void WriteStubConsumables(string dir)
    {
        File.WriteAllText(Path.Combine(dir, "consumables.json"), """
        {
          "consumables": [
            { "id": "x", "matchToken": "x", "effectType": "Heal", "amount": 1 }
          ]
        }
        """);
    }

    // ─── helpers ──────────────────────────────────────────────────────────

    private static void AssertHeal(ContentProvider p, string itemName, int expected)
    {
        var c = p.ResolveConsumable(itemName);
        c.Should().NotBeNull($"'{itemName}' should resolve");
        c!.EffectType.Should().Be("Heal");
        c.Amount.Should().Be(expected);
    }

    private static void AssertWeave(ContentProvider p, string itemName, int expected)
    {
        var c = p.ResolveConsumable(itemName);
        c.Should().NotBeNull();
        c!.EffectType.Should().Be("RestoreWeave");
        c.Amount.Should().Be(expected);
    }

    private static void AssertBuff(ContentProvider p, string itemName, string key, float value)
    {
        var c = p.ResolveConsumable(itemName);
        c.Should().NotBeNull();
        c!.EffectType.Should().Be("Buff");
        c.BuffKey.Should().Be(key);
        c.BuffValue.Should().BeApproximately(value, 0.0001f);
    }
}
