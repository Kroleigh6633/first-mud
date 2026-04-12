using System.Collections.Concurrent;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public class CombatService
{
    private readonly ILogger<CombatService> _logger;
    private readonly ConcurrentDictionary<Guid, Encounter> _activeEncounters = new();

    public CombatService(ILogger<CombatService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds and stores a new Encounter from the player, their active companions, and enemy templates.
    /// equippedItems: all items currently equipped by the player, keyed by slot.
    /// Returns the created Encounter.
    /// </summary>
    public Task<Encounter> StartEncounterAsync(
        Guid playerId,
        Guid zoneId,
        Player player,
        IReadOnlyList<Companion> activeCompanions,
        IReadOnlyList<MonsterTemplate> enemies,
        IReadOnlyDictionary<EquipmentSlot, Item>? equippedItems = null,
        CancellationToken ct = default)
    {
        equippedItems ??= new Dictionary<EquipmentSlot, Item>();
        var element = player.PrimaryElement == default ? MagicElement.Aether : player.PrimaryElement;

        // Equipment bonuses per slot
        int meleeBonus    = equippedItems.TryGetValue(EquipmentSlot.MeleeWeapon,  out var mw) ? mw.Workmanship.Value * 3 : 0;
        int rangedBonus   = equippedItems.TryGetValue(EquipmentSlot.RangedWeapon, out var rw) ? rw.Workmanship.Value * 2 : 0;
        int focusBonus    = equippedItems.TryGetValue(EquipmentSlot.Focus,        out var fc) ? fc.Workmanship.Value * 4 : 0;
        int headBonusHp   = equippedItems.TryGetValue(EquipmentSlot.Head,         out var hd) ? hd.Workmanship.Value * 3 : 0;
        int chestBonusHp  = equippedItems.TryGetValue(EquipmentSlot.Chest,        out var ch) ? ch.Workmanship.Value * 5 : 0;
        int legsBonusHp   = equippedItems.TryGetValue(EquipmentSlot.Legs,         out var lg) ? lg.Workmanship.Value * 3 : 0;
        int handsBonus    = equippedItems.TryGetValue(EquipmentSlot.Hands,        out var ha) ? ha.Workmanship.Value * 2 : 0;
        int feetBonus     = equippedItems.TryGetValue(EquipmentSlot.Feet,         out var ft) ? ft.Workmanship.Value * 1 : 0;
        int accBonusHp    = equippedItems.TryGetValue(EquipmentSlot.Accessory,    out var ac) ? ac.Workmanship.Value * 2 : 0;

        int strikeBonus   = meleeBonus + rangedBonus + handsBonus;
        int weaveBoltBonus= focusBonus;
        int combatMaxHp   = player.MaxHp + headBonusHp + chestBonusHp + legsBonusHp + accBonusHp;
        int combatSpeed   = player.Speed + feetBonus;

        var playerAbilities = new List<CombatAbility>
        {
            new("Strike", 18 + strikeBonus, 0, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
            new("Weave Bolt", 30 + weaveBoltBonus, 10, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
            new("Restore", 30, 5, MagicElement.Aether,
                AbilityTargetType.Self, AbilityCategory.Heal)
        };

        var playerCombatant = Combatant.Create(
            player.Name,
            CombatantType.Player,
            player.Id,
            combatMaxHp,
            combatSpeed,
            player.PrimaryElement == default ? MagicElement.Aether : player.PrimaryElement,
            isPlayerSide: true,
            player.Level,
            playerAbilities);

        var playerSide = new List<Combatant> { playerCombatant };

        foreach (var companion in activeCompanions)
        {
            var companionAbilities = new List<CombatAbility>
            {
                new("Slash", 12, 0, companion.Element, AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
                new("Elemental Strike", 20, 8, companion.Element, AbilityTargetType.SingleEnemy, AbilityCategory.Attack)
            };

            var companionHp = 60 + companion.Level * 5;
            var companionSpeed = 8 + companion.Level;

            var companionCombatant = Combatant.Create(
                companion.Name,
                CombatantType.Companion,
                companion.Id,
                companionHp,
                companionSpeed,
                companion.Element,
                isPlayerSide: true,
                companion.Level,
                companionAbilities);

            playerSide.Add(companionCombatant);
        }

        var enemySide = enemies.Select(template => Combatant.Create(
            template.Name,
            CombatantType.Monster,
            Guid.NewGuid(),
            template.Hp,
            template.Speed,
            template.Element,
            isPlayerSide: false,
            template.Level,
            template.Abilities)).ToList();

        var encounter = Encounter.Create(playerId, zoneId, playerSide, enemySide);
        _activeEncounters[encounter.Id] = encounter;

        _logger.LogInformation("Combat started: Encounter {EncounterId} for player {PlayerId} in zone {ZoneId}",
            encounter.Id, playerId, zoneId);

        return Task.FromResult(encounter);
    }

    /// <summary>
    /// Executes one action in the encounter: validates it is the actor's turn, resolves ability,
    /// applies damage/heal, advances turn, checks end state.
    /// </summary>
    public Task<(bool Success, string Message, Encounter? Encounter)> ExecuteActionAsync(
        Guid encounterId,
        Guid actorId,
        string abilityName,
        Guid? targetId,
        CancellationToken ct = default)
    {
        if (!_activeEncounters.TryGetValue(encounterId, out var encounter))
            return Task.FromResult((false, "Encounter not found.", (Encounter?)null));

        if (encounter.State != EncounterState.InProgress)
            return Task.FromResult((false, $"Encounter is not in progress (state: {encounter.State}).", (Encounter?)null));

        var currentActor = encounter.CurrentActor;
        if (currentActor is null)
            return Task.FromResult((false, "No current actor.", (Encounter?)null));

        if (currentActor.Id != actorId)
            return Task.FromResult((false, $"It is not {actorId}'s turn. Current actor is {currentActor.Id}.", (Encounter?)null));

        var ability = currentActor.Abilities.FirstOrDefault(a =>
            string.Equals(a.Name, abilityName, StringComparison.OrdinalIgnoreCase));

        if (ability is null)
            return Task.FromResult((false, $"Ability '{abilityName}' not found on actor.", (Encounter?)null));

        // Resolve damage/heal
        if (ability.Category == AbilityCategory.Attack)
        {
            var resolvedTarget = ResolveTarget(encounter, currentActor, ability, targetId);
            if (resolvedTarget is null)
                return Task.FromResult((false, "No valid target found.", (Encounter?)null));

            int rawPower = ability.BasePower + currentActor.Level * 2;
            float multiplier = ElementMatchup.GetMultiplier(ability.Element, resolvedTarget.Element);
            int finalDamage = (int)(rawPower * multiplier);

            encounter.ApplyDamage(resolvedTarget.Id, finalDamage, actorId, multiplier);

            _logger.LogDebug("Actor {ActorId} used {Ability} on {TargetId} for {Damage} damage (x{Mult})",
                actorId, abilityName, resolvedTarget.Id, finalDamage, multiplier);
        }
        else if (ability.Category == AbilityCategory.Heal)
        {
            var resolvedTarget = ResolveHealTarget(encounter, currentActor, ability, targetId);
            if (resolvedTarget is not null)
            {
                int healAmount = ability.BasePower + currentActor.Level * 2;
                if (healAmount <= 0) healAmount = currentActor.MaxHp / 4; // fallback: heal 25% max HP
                resolvedTarget.Heal(healAmount);
            }
        }

        encounter.CheckEndState();

        if (encounter.State == EncounterState.InProgress)
            encounter.AdvanceTurn();

        return Task.FromResult((true, "Action executed.", (Encounter?)encounter));
    }

    /// <summary>Sets the encounter state to Fled.</summary>
    public Task<(bool Success, string Message, Encounter? Encounter)> FleeAsync(
        Guid encounterId,
        CancellationToken ct = default)
    {
        if (!_activeEncounters.TryGetValue(encounterId, out var encounter))
            return Task.FromResult((false, "Encounter not found.", (Encounter?)null));

        encounter.Flee();
        _logger.LogInformation("Player fled encounter {EncounterId}", encounterId);

        return Task.FromResult((true, "Fled successfully.", (Encounter?)encounter));
    }

    /// <summary>Returns the encounter or null.</summary>
    public Encounter? GetEncounter(Guid encounterId)
        => _activeEncounters.TryGetValue(encounterId, out var enc) ? enc : null;

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static Combatant? ResolveTarget(
        Encounter encounter,
        Combatant actor,
        CombatAbility ability,
        Guid? targetId)
    {
        var enemies = encounter.Combatants
            .Where(c => c.IsPlayerSide != actor.IsPlayerSide && !c.IsDefeated)
            .ToList();

        return ability.TargetType switch
        {
            AbilityTargetType.SingleEnemy =>
                targetId.HasValue
                    ? enemies.FirstOrDefault(c => c.Id == targetId.Value) ?? enemies.FirstOrDefault()
                    : enemies.FirstOrDefault(),
            AbilityTargetType.AllEnemies => enemies.FirstOrDefault(), // caller iterates; here we pick first for simplicity
            _ => null
        };
    }

    private static Combatant? ResolveHealTarget(
        Encounter encounter,
        Combatant actor,
        CombatAbility ability,
        Guid? targetId)
    {
        return ability.TargetType switch
        {
            AbilityTargetType.Self => actor,
            AbilityTargetType.SingleAlly =>
                targetId.HasValue
                    ? encounter.Combatants.FirstOrDefault(c => c.Id == targetId.Value && !c.IsDefeated)
                    : actor,
            _ => actor
        };
    }
}
