using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class ItemTests
{
    private static Item CreateDefaultItem(int workmanshipValue = 5)
    {
        return Item.Create(
            "Test Sword",
            "A test sword",
            ItemCategory.Weapon,
            Workmanship.Of(workmanshipValue),
            WorldId.Aeldran);
    }

    [Fact]
    public void Create_SetsSalvageableTrue()
    {
        var item = CreateDefaultItem();

        item.IsSalvageable.Should().BeTrue();
    }

    [Fact]
    public void Create_SetsDurabilityEqualsMaxDurability()
    {
        var item = CreateDefaultItem();

        item.Durability.Should().Be(item.MaxDurability);
    }

    [Fact]
    public void Create_MaxDurabilityIsWorkmanshipValueTimes10()
    {
        var item = CreateDefaultItem(workmanshipValue: 7);

        item.MaxDurability.Should().Be(70);
    }

    [Fact]
    public void Create_MaxDurability_W1_Is10()
    {
        var item = CreateDefaultItem(workmanshipValue: 1);

        item.MaxDurability.Should().Be(10);
    }

    [Fact]
    public void Create_MaxDurability_W10_Is100()
    {
        var item = CreateDefaultItem(workmanshipValue: 10);

        item.MaxDurability.Should().Be(100);
    }

    [Fact]
    public void Degrade_ReducesDurability()
    {
        var item = CreateDefaultItem(workmanshipValue: 5); // MaxDurability = 50

        item.Degrade(10);

        item.Durability.Should().Be(40);
    }

    [Fact]
    public void Degrade_To0_SetsSalvageableFalse()
    {
        var item = CreateDefaultItem(workmanshipValue: 1); // MaxDurability = 10

        item.Degrade(10);

        item.IsSalvageable.Should().BeFalse();
    }

    [Fact]
    public void Degrade_BeyondZero_StaysAtZero()
    {
        var item = CreateDefaultItem(workmanshipValue: 1); // MaxDurability = 10

        item.Degrade(20);

        item.Durability.Should().Be(0);
    }

    [Fact]
    public void IsBroken_TrueWhenDurabilityIsZero()
    {
        var item = CreateDefaultItem(workmanshipValue: 1);

        item.Degrade(10);

        item.IsBroken.Should().BeTrue();
    }

    [Fact]
    public void IsBroken_FalseWhenDurabilityAboveZero()
    {
        var item = CreateDefaultItem(workmanshipValue: 5);

        item.IsBroken.Should().BeFalse();
    }

    [Fact]
    public void ApplyTaperImbue_SetsMagicalPropertiesCorrectly()
    {
        var item = CreateDefaultItem();

        item.ApplyTaperImbue(TaperType.Shaping, TaperQuality.Pristine, MagicElement.Fire, MagicPolarity.Shaping);

        item.AppliedTaper.Should().Be(TaperType.Shaping);
        item.TaperQuality.Should().Be(TaperQuality.Pristine);
        item.MagicalElement.Should().Be(MagicElement.Fire);
        item.MagicalPolarity.Should().Be(MagicPolarity.Shaping);
    }

    [Fact]
    public void ApplyTaperImbue_WyrdTaper_SetsIsWyrdTouchedTrue()
    {
        var item = CreateDefaultItem();

        item.ApplyTaperImbue(TaperType.Wyrd, TaperQuality.Pristine, MagicElement.Aether, MagicPolarity.TwiceBorn);

        item.IsWyrdTouched.Should().BeTrue();
    }

    [Fact]
    public void ApplyTaperImbue_NonWyrdTaper_DoesNotSetIsWyrdTouched()
    {
        var item = CreateDefaultItem();

        item.ApplyTaperImbue(TaperType.Shaping, TaperQuality.Pristine, MagicElement.Fire, MagicPolarity.Shaping);

        item.IsWyrdTouched.Should().BeFalse();
    }

    [Fact]
    public void Create_SetsCorrectName()
    {
        var item = Item.Create("My Sword", "A fine blade", ItemCategory.Weapon,
            Workmanship.Of(5), WorldId.Aeldran);

        item.Name.Should().Be("My Sword");
    }

    [Fact]
    public void Create_SetsCorrectCategory()
    {
        var item = Item.Create("Test Armor", "Fine plate", ItemCategory.Armor,
            Workmanship.Of(5), WorldId.Aeldran);

        item.Category.Should().Be(ItemCategory.Armor);
    }

    [Fact]
    public void Create_SetsIsArdweldOrigin()
    {
        var item = Item.Create("Construct Part", "Ardweld-made", ItemCategory.AutomationPart,
            Workmanship.Of(8), WorldId.ArdweldRemnant, isArdweldOrigin: true);

        item.IsArdweldOrigin.Should().BeTrue();
    }
}
