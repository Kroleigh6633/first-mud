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
