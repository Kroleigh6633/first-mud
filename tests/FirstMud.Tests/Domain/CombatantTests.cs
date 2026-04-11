using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class CombatantTests
{
    private static CombatAbility BasicAttack => new(
        "Strike", 10, 0, MagicElement.Fire, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

    private static Combatant CreateCombatant(int maxHp = 100, int speed = 10) =>
        Combatant.Create("Hero", CombatantType.Player, Guid.NewGuid(),
            maxHp, speed, MagicElement.Fire, isPlayerSide: true, level: 1,
            new[] { BasicAttack });

    [Fact]
    public void Create_SetsMaxHpAndCurrentHpEqual()
    {
        var c = CreateCombatant(80);
        c.MaxHp.Should().Be(80);
        c.CurrentHp.Should().Be(80);
    }

    [Fact]
    public void Create_IsNotDefeatedOnCreation()
    {
        var c = CreateCombatant();
        c.IsDefeated.Should().BeFalse();
    }

    [Fact]
    public void Create_HasAbilities()
    {
        var c = CreateCombatant();
        c.Abilities.Should().HaveCount(1);
    }

    [Fact]
    public void TakeDamage_ReducesCurrentHp()
    {
        var c = CreateCombatant(100);
        c.TakeDamage(30);
        c.CurrentHp.Should().Be(70);
    }

    [Fact]
    public void TakeDamage_DoesNotGoBelowZero()
    {
        var c = CreateCombatant(100);
        c.TakeDamage(999);
        c.CurrentHp.Should().Be(0);
    }

    [Fact]
    public void TakeDamage_SetsIsDefeated_WhenHpReachesZero()
    {
        var c = CreateCombatant(50);
        c.TakeDamage(50);
        c.IsDefeated.Should().BeTrue();
    }

    [Fact]
    public void TakeDamage_NegativeAmount_DoesNotHeal()
    {
        var c = CreateCombatant(100);
        c.TakeDamage(20);
        c.TakeDamage(-10); // should be treated as 0
        c.CurrentHp.Should().Be(80);
    }

    [Fact]
    public void Heal_IncreasesCurrentHp()
    {
        var c = CreateCombatant(100);
        c.TakeDamage(40);
        c.Heal(20);
        c.CurrentHp.Should().Be(80);
    }

    [Fact]
    public void Heal_DoesNotExceedMaxHp()
    {
        var c = CreateCombatant(100);
        c.TakeDamage(10);
        c.Heal(999);
        c.CurrentHp.Should().Be(100);
    }

    [Fact]
    public void Create_AssignsNewGuidId()
    {
        var a = CreateCombatant();
        var b = CreateCombatant();
        a.Id.Should().NotBe(b.Id);
    }
}
