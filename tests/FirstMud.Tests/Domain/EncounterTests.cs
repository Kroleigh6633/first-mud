using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;

namespace FirstMud.Tests.Domain;

public class EncounterTests
{
    private static CombatAbility Attack(MagicElement el = MagicElement.Fire) =>
        new("Strike", 10, 0, el, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

    private static Combatant MakePlayer(int speed = 10, int hp = 100) =>
        Combatant.Create("Player", CombatantType.Player, Guid.NewGuid(),
            hp, speed, MagicElement.Fire, isPlayerSide: true, level: 1, new[] { Attack() });

    private static Combatant MakeCompanion(int speed = 8) =>
        Combatant.Create("Companion", CombatantType.Companion, Guid.NewGuid(),
            80, speed, MagicElement.Water, isPlayerSide: true, level: 1, new[] { Attack(MagicElement.Water) });

    private static Combatant MakeEnemy(int speed = 6, int hp = 60) =>
        Combatant.Create("Rat", CombatantType.Monster, Guid.NewGuid(),
            hp, speed, MagicElement.Earth, isPlayerSide: false, level: 1, new[] { Attack(MagicElement.Earth) });

    // -------------------------------------------------------------------------
    // Creation
    // -------------------------------------------------------------------------

    [Fact]
    public void Create_SetsStateToInProgress()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });

        encounter.State.Should().Be(EncounterState.InProgress);
    }

    [Fact]
    public void Create_SetsCombatants()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });

        encounter.Combatants.Should().HaveCount(2);
    }

    [Fact]
    public void Create_RaisesCombatStartedEvent()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });

        encounter.DomainEvents.Should().ContainSingle(e => e is CombatStartedEvent);
    }

    [Fact]
    public void Create_TurnOrderSortedBySpeedDescending()
    {
        var fast = MakePlayer(speed: 15);
        var slow = MakeEnemy(speed: 5);

        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { fast }, new[] { slow });

        encounter.TurnOrder[0].Should().Be(fast.Id);
        encounter.TurnOrder[1].Should().Be(slow.Id);
    }

    [Fact]
    public void Create_RoundNumberStartsAt1()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });

        encounter.RoundNumber.Should().Be(1);
    }

    // -------------------------------------------------------------------------
    // Turn advancement
    // -------------------------------------------------------------------------

    [Fact]
    public void AdvanceTurn_MovesToNextCombatant()
    {
        var player = MakePlayer(speed: 10);
        var enemy = MakeEnemy(speed: 6);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });

        var firstActorId = encounter.CurrentActor!.Id;
        encounter.AdvanceTurn();

        encounter.CurrentActor!.Id.Should().NotBe(firstActorId);
    }

    [Fact]
    public void AdvanceTurn_IncrementsRoundAfterAllAct()
    {
        var player = MakePlayer(speed: 10);
        var enemy = MakeEnemy(speed: 6);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });

        encounter.AdvanceTurn(); // enemy acts
        encounter.AdvanceTurn(); // wraps back — round 2

        encounter.RoundNumber.Should().Be(2);
    }

    [Fact]
    public void AdvanceTurn_RaisesCombatTurnAdvancedEvent()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });
        encounter.ClearDomainEvents();

        encounter.AdvanceTurn();

        encounter.DomainEvents.Should().ContainSingle(e => e is CombatTurnAdvancedEvent);
    }

    [Fact]
    public void AdvanceTurn_SkipsDefeatedCombatants()
    {
        // player (speed 10) -> companion (speed 8) -> enemy (speed 6)
        var player = MakePlayer(speed: 10);
        var companion = MakeCompanion(speed: 8);
        var enemy = MakeEnemy(speed: 6);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player, companion }, new[] { enemy });

        // Defeat the companion
        encounter.ApplyDamage(companion.Id, 999, player.Id);

        // advance from player's turn
        encounter.AdvanceTurn();

        // Should skip the defeated companion and land on enemy
        encounter.CurrentActor!.Id.Should().Be(enemy.Id);
    }

    // -------------------------------------------------------------------------
    // ApplyDamage
    // -------------------------------------------------------------------------

    [Fact]
    public void ApplyDamage_ReducesTargetHp()
    {
        var player = MakePlayer(hp: 100);
        var enemy = MakeEnemy(hp: 60);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });

        encounter.ApplyDamage(enemy.Id, 20, player.Id);

        enemy.CurrentHp.Should().Be(40);
    }

    [Fact]
    public void ApplyDamage_RaisesCombatDamageDealtEvent()
    {
        var player = MakePlayer();
        var enemy = MakeEnemy();
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });
        encounter.ClearDomainEvents();

        encounter.ApplyDamage(enemy.Id, 10, player.Id, 1.5f);

        var evt = encounter.DomainEvents.OfType<CombatDamageDealtEvent>().Single();
        evt.Damage.Should().Be(10);
        evt.ElementMultiplier.Should().BeApproximately(1.5f, 0.001f);
    }

    // -------------------------------------------------------------------------
    // CheckEndState
    // -------------------------------------------------------------------------

    [Fact]
    public void CheckEndState_SetsVictory_WhenAllEnemiesDefeated()
    {
        var player = MakePlayer();
        var enemy = MakeEnemy(hp: 10);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });

        encounter.ApplyDamage(enemy.Id, 999, player.Id);
        encounter.CheckEndState();

        encounter.State.Should().Be(EncounterState.Victory);
    }

    [Fact]
    public void CheckEndState_SetsDefeat_WhenAllPlayerSideDefeated()
    {
        var player = MakePlayer(hp: 10);
        var enemy = MakeEnemy();
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });

        encounter.ApplyDamage(player.Id, 999, enemy.Id);
        encounter.CheckEndState();

        encounter.State.Should().Be(EncounterState.Defeat);
    }

    [Fact]
    public void CheckEndState_RaisesCombatEndedEvent_OnVictory()
    {
        var player = MakePlayer();
        var enemy = MakeEnemy(hp: 10);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });
        encounter.ClearDomainEvents();

        encounter.ApplyDamage(enemy.Id, 999, player.Id);
        encounter.CheckEndState();

        var evt = encounter.DomainEvents.OfType<CombatEndedEvent>().Single();
        evt.Outcome.Should().Be(EncounterState.Victory);
    }

    [Fact]
    public void CheckEndState_RaisesCombatEndedEvent_OnDefeat()
    {
        var player = MakePlayer(hp: 10);
        var enemy = MakeEnemy();
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });
        encounter.ClearDomainEvents();

        encounter.ApplyDamage(player.Id, 999, enemy.Id);
        encounter.CheckEndState();

        var evt = encounter.DomainEvents.OfType<CombatEndedEvent>().Single();
        evt.Outcome.Should().Be(EncounterState.Defeat);
    }

    [Fact]
    public void CheckEndState_DoesNothing_WhenStillInProgress()
    {
        var player = MakePlayer();
        var enemy = MakeEnemy();
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });
        encounter.ClearDomainEvents();

        encounter.CheckEndState();

        encounter.State.Should().Be(EncounterState.InProgress);
        encounter.DomainEvents.Should().NotContain(e => e is CombatEndedEvent);
    }

    // -------------------------------------------------------------------------
    // Flee
    // -------------------------------------------------------------------------

    [Fact]
    public void Flee_SetsStateFled()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });

        encounter.Flee();

        encounter.State.Should().Be(EncounterState.Fled);
    }

    [Fact]
    public void Flee_RaisesCombatEndedEvent_WithFledOutcome()
    {
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { MakePlayer() }, new[] { MakeEnemy() });
        encounter.ClearDomainEvents();

        encounter.Flee();

        var evt = encounter.DomainEvents.OfType<CombatEndedEvent>().Single();
        evt.Outcome.Should().Be(EncounterState.Fled);
    }

    [Fact]
    public void Flee_DoesNothing_WhenAlreadyEnded()
    {
        var player = MakePlayer();
        var enemy = MakeEnemy(hp: 1);
        var encounter = Encounter.Create(Guid.NewGuid(), Guid.NewGuid(),
            new[] { player }, new[] { enemy });
        encounter.ApplyDamage(enemy.Id, 999, player.Id);
        encounter.CheckEndState(); // sets Victory
        encounter.ClearDomainEvents();

        encounter.Flee();

        encounter.State.Should().Be(EncounterState.Victory); // unchanged
        encounter.DomainEvents.Should().BeEmpty();
    }
}
