using FirstMud.Application;
using FirstMud.Application.Services;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace FirstMud.Tests.Application;

public class CombatServiceTests
{
    private static CombatService CreateService() =>
        new(NullLogger<CombatService>.Instance);

    private static Player CreatePlayer(MagicElement element = MagicElement.Fire)
    {
        var player = Player.Create("Hero", 42);
        player.RevealMagicAffinity(element, MagicPolarity.Shaping);
        return player;
    }

    private static MonsterTemplate BasicMonster() => new(
        "Rat", 50, 6, 1, MagicElement.Earth,
        new[] { new CombatAbility("Bite", 8, 0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack) });

    // -------------------------------------------------------------------------
    // StartEncounterAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task StartEncounterAsync_ReturnsEncounterInProgress()
    {
        var svc = CreateService();
        var player = CreatePlayer();

        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        encounter.State.Should().Be(EncounterState.InProgress);
    }

    [Fact]
    public async Task StartEncounterAsync_StoresEncounter_RetrievableByGetEncounter()
    {
        var svc = CreateService();
        var player = CreatePlayer();

        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        svc.GetEncounter(encounter.Id).Should().NotBeNull();
        svc.GetEncounter(encounter.Id)!.Id.Should().Be(encounter.Id);
    }

    [Fact]
    public async Task StartEncounterAsync_IncludesPlayerAndEnemyCombatants()
    {
        var svc = CreateService();
        var player = CreatePlayer();

        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        encounter.Combatants.Should().Contain(c => c.IsPlayerSide);
        encounter.Combatants.Should().Contain(c => !c.IsPlayerSide);
    }

    // -------------------------------------------------------------------------
    // ExecuteActionAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteActionAsync_ValidAction_ReturnsSuccess()
    {
        var svc = CreateService();
        var player = CreatePlayer();
        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        var actorId = encounter.CurrentActor!.Id;
        var abilityName = encounter.CurrentActor.Abilities[0].Name;

        var (success, _, updated) = await svc.ExecuteActionAsync(encounter.Id, actorId, abilityName, null);

        success.Should().BeTrue();
        updated.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteActionAsync_WrongActor_ReturnsFalse()
    {
        var svc = CreateService();
        var player = CreatePlayer();
        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        var wrongActorId = Guid.NewGuid();

        var (success, message, _) = await svc.ExecuteActionAsync(encounter.Id, wrongActorId, "Strike", null);

        success.Should().BeFalse();
        message.Should().Contain("not");
    }

    [Fact]
    public async Task ExecuteActionAsync_AppliesDamageWithElementBonus()
    {
        var svc = CreateService();
        // Fire player vs Earth monster — Fire > Earth = 1.5x
        var player = CreatePlayer(MagicElement.Fire);
        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        // Find the player combatant (it should be first turn if speed is higher)
        // Ensure player acts first
        var playerCombatant = encounter.Combatants.First(c => c.IsPlayerSide);
        var enemyCombatant = encounter.Combatants.First(c => !c.IsPlayerSide);
        int enemyHpBefore = enemyCombatant.CurrentHp;

        // Make sure the current actor is the player combatant
        // Player speed = 10 (from Player.Create), enemy speed = 6 → player goes first
        encounter.CurrentActor!.Id.Should().Be(playerCombatant.Id);

        var (success, _, updated) = await svc.ExecuteActionAsync(
            encounter.Id, playerCombatant.Id, "Weave Bolt", enemyCombatant.Id);

        success.Should().BeTrue();
        // Damage = (25 + 1*2) * 1.5 = 40.5 → 40; enemy hp should drop
        enemyCombatant.CurrentHp.Should().BeLessThan(enemyHpBefore);
    }

    // -------------------------------------------------------------------------
    // FleeAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task FleeAsync_SetsStateFled()
    {
        var svc = CreateService();
        var player = CreatePlayer();
        var encounter = await svc.StartEncounterAsync(player.Id, Guid.NewGuid(), player, [], new[] { BasicMonster() });

        var (success, _, updated) = await svc.FleeAsync(encounter.Id);

        success.Should().BeTrue();
        updated!.State.Should().Be(EncounterState.Fled);
    }

    [Fact]
    public async Task FleeAsync_UnknownEncounter_ReturnsFalse()
    {
        var svc = CreateService();

        var (success, message, _) = await svc.FleeAsync(Guid.NewGuid());

        success.Should().BeFalse();
        message.Should().Contain("not found");
    }

    // -------------------------------------------------------------------------
    // GetEncounter
    // -------------------------------------------------------------------------

    [Fact]
    public void GetEncounter_Unknown_ReturnsNull()
    {
        var svc = CreateService();

        svc.GetEncounter(Guid.NewGuid()).Should().BeNull();
    }
}
