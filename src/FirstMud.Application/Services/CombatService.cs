using System.Collections.Concurrent;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Services;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public class CombatService
{
    private readonly ILogger<CombatService> _logger;
    private readonly ConcurrentDictionary<Guid, Encounter> _activeEncounters = new();
    private readonly ConcurrentDictionary<Guid, int> _encounterDangerLevels = new();

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
        CancellationToken ct = default,
        int dangerLevel = 0)
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

        // Stat bonuses:
        // Strength: adds to melee Strike damage (Str / 5)
        // Agility: future dodge chance — tracked but not yet implemented in hit resolution
        // Intellect: adds to Weave Bolt damage (Int / 5)
        // Fortitude: adds bonus HP in combat (Fort / 2)
        // Speed: already affects turn order + Feet equipment bonus
        int statStrikeBonus    = player.Strength / 5;
        int statSpellBonus     = player.Intellect / 5;
        int statFortBonus      = player.Fortitude / 2;

        int strikeBonus   = meleeBonus + rangedBonus + handsBonus + statStrikeBonus;
        int weaveBoltBonus= focusBonus + statSpellBonus;
        int combatMaxHp   = player.MaxHp + headBonusHp + chestBonusHp + legsBonusHp + accBonusHp + statFortBonus;
        int combatSpeed   = player.Speed + feetBonus;

        // Imbue bonuses from equipped weapon (melee, ranged, or focus)
        var equippedWeapon = mw ?? rw ?? fc;
        int elementalImbueBonus = 0;
        float restorationPower  = 0f;
        bool hasWyrdImbue       = false;
        float wyrdPower         = 0f;

        if (equippedWeapon is not null)
        {
            foreach (var imbue in equippedWeapon.Imbues)
            {
                switch (imbue.Type)
                {
                    case Domain.Enums.ImbueType.Fire:
                    case Domain.Enums.ImbueType.Water:
                    case Domain.Enums.ImbueType.Earth:
                    case Domain.Enums.ImbueType.Air:
                        // Each elemental imbue adds a flat bonus: power * base Strike damage
                        elementalImbueBonus += (int)(18 * imbue.Power);
                        break;
                    case Domain.Enums.ImbueType.Restoration:
                        restorationPower = Math.Max(restorationPower, imbue.Power);
                        break;
                    case Domain.Enums.ImbueType.Wyrd:
                        hasWyrdImbue = true;
                        wyrdPower = Math.Max(wyrdPower, imbue.Power);
                        break;
                }
            }
        }

        // Build Strike with embedded imbue effects
        var strikeAbility = new CombatAbility(
            "Strike",
            18 + strikeBonus + elementalImbueBonus,
            0,
            element,
            AbilityTargetType.SingleEnemy,
            AbilityCategory.Attack,
            LifestealPower: restorationPower,
            WyrdProcChance: wyrdPower);

        var playerAbilities = new List<CombatAbility>
        {
            strikeAbility,
            new("Weave Bolt", 30 + weaveBoltBonus, 10, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
            new("Restore", 30, 5, MagicElement.Aether,
                AbilityTargetType.Self, AbilityCategory.Heal)
        };

        // Wyrd imbue: also add a dedicated Wyrd Pulse ability
        if (hasWyrdImbue)
        {
            playerAbilities.Add(new CombatAbility(
                "Wyrd Pulse", (int)(18 * wyrdPower), 5, MagicElement.Aether,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack,
                WyrdProcChance: wyrdPower));
        }

        int combatAgility = player.Agility;

        var playerCombatant = Combatant.Create(
            player.Name,
            CombatantType.Player,
            player.Id,
            combatMaxHp,
            combatSpeed,
            player.PrimaryElement == default ? MagicElement.Aether : player.PrimaryElement,
            isPlayerSide: true,
            player.Level,
            playerAbilities,
            agility: combatAgility);

        var playerSide = new List<Combatant> { playerCombatant };

        foreach (var companion in activeCompanions)
        {
            // Layer-based ability table: higher layers unlock more powerful abilities.
            // HP and Speed also scale with both Level and Layer so layer investment matters.
            var companionAbilities = CompanionAbilityFactory.Build(
                companion.Type, companion.Element, companion.CurrentLayer);

            var companionHp    = 50 + companion.Level * 10 + companion.CurrentLayer * 5;
            var companionSpeed = 6 + companion.Level;

            var companionCombatant = Combatant.Create(
                companion.Name,
                CombatantType.Companion,
                companion.Id,
                companionHp,
                companionSpeed,
                companion.Element,
                isPlayerSide: true,
                companion.Level,
                companionAbilities,
                agility: companion.Level + 5);

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
            template.Abilities,
            agility: template.Speed)).ToList();

        var encounter = Encounter.Create(playerId, zoneId, playerSide, enemySide);
        _activeEncounters[encounter.Id] = encounter;
        _encounterDangerLevels[encounter.Id] = dangerLevel;

        _logger.LogInformation("Combat started: Encounter {EncounterId} for player {PlayerId} in zone {ZoneId}",
            encounter.Id, playerId, zoneId);

        return Task.FromResult(encounter);
    }

    /// <summary>
    /// Executes one action in the encounter: validates it is the actor's turn, resolves ability,
    /// applies damage/heal, advances turn, checks end state.
    /// Returns narration text for the action as the Message.
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

        string actionNarration = string.Empty;

        // Resolve damage/heal
        if (ability.Category == AbilityCategory.Attack)
        {
            var resolvedTarget = ResolveTarget(encounter, currentActor, ability, targetId);
            if (resolvedTarget is null)
                return Task.FromResult((false, "No valid target found.", (Encounter?)null));

            // --- Hit/Miss check ---
            int hitChance = 70 + (currentActor.Level - resolvedTarget.Level) * 5 + currentActor.Speed * 2;
            hitChance = Math.Clamp(hitChance, 10, 95);
            int hitRoll = Random.Shared.Next(1, 101);

            if (hitRoll > hitChance)
            {
                // MISS — deal 0 damage, still advance turn
                actionNarration = $"{currentActor.Name} swings {ability.Name} at {resolvedTarget.Name} — miss!";
                _logger.LogDebug("Actor {ActorId} missed {TargetId} with {Ability} (roll {Roll} > {Chance})",
                    actorId, resolvedTarget.Id, abilityName, hitRoll, hitChance);

                encounter.CheckEndState();
                if (encounter.State == EncounterState.InProgress)
                    encounter.AdvanceTurn();

                return Task.FromResult((true, actionNarration, (Encounter?)encounter));
            }

            // --- Dodge check ---
            float dodgeChance = resolvedTarget.Agility * 1.5f - currentActor.Agility * 0.5f;
            dodgeChance = Math.Clamp(dodgeChance, 0f, 40f);
            int dodgeRoll = Random.Shared.Next(1, 101);

            if (dodgeRoll <= (int)dodgeChance)
            {
                // DODGE — deal 0 damage, still advance turn
                actionNarration = $"{currentActor.Name} lunges at {resolvedTarget.Name} — dodged!";
                _logger.LogDebug("Actor {ActorId} was dodged by {TargetId} with {Ability} (dodge {Chance}%)",
                    actorId, resolvedTarget.Id, abilityName, (int)dodgeChance);

                encounter.CheckEndState();
                if (encounter.State == EncounterState.InProgress)
                    encounter.AdvanceTurn();

                return Task.FromResult((true, actionNarration, (Encounter?)encounter));
            }

            // --- Damage variance (±20%) ---
            int baseDamage = ability.BasePower + currentActor.Level * 2;
            double variance = baseDamage * 0.2;
            double rawActual = baseDamage + (Random.Shared.NextDouble() * variance * 2.0 - variance);
            int variedDamage = Math.Max(1, (int)rawActual);

            // --- Element multiplier ---
            float multiplier = ElementMatchup.GetMultiplier(ability.Element, resolvedTarget.Element);

            // --- Crit check ---
            int critChance = 5 + (currentActor.Level - resolvedTarget.Level) * 2 + currentActor.Speed;
            critChance = Math.Clamp(critChance, 5, 35);
            int critRoll = Random.Shared.Next(1, 101);
            bool isCrit = critRoll <= critChance;
            float critMultiplier = isCrit ? 1.5f : 1.0f;

            int finalDamage = Math.Max(1, (int)(variedDamage * multiplier * critMultiplier));

            encounter.ApplyDamage(resolvedTarget.Id, finalDamage, actorId, multiplier);

            // Build narration
            bool isKill = resolvedTarget.IsDefeated;
            string elementSuffix = multiplier > 1f ? " (super effective!)" : multiplier < 1f ? " (resisted)" : "";
            string killSuffix = isKill ? $" {resolvedTarget.Name} is defeated!" : "";

            actionNarration = isCrit
                ? $"CRITICAL! {currentActor.Name} strikes {resolvedTarget.Name} with {ability.Name} for {finalDamage} damage{elementSuffix}.{killSuffix}"
                : $"{currentActor.Name} strikes {resolvedTarget.Name} with {ability.Name} for {finalDamage} damage{elementSuffix}.{killSuffix}";

            // Lifesteal from Restoration imbue
            if (ability.LifestealPower > 0f)
            {
                var healAmount = (int)(finalDamage * ability.LifestealPower);
                if (healAmount > 0)
                    currentActor.Heal(healAmount);
            }

            // Wyrd imbue: proc chance to reduce enemy Weave (represented as extra damage to target)
            if (ability.WyrdProcChance > 0f && Random.Shared.NextDouble() < ability.WyrdProcChance)
            {
                var wyrdDamage = (int)(finalDamage * ability.WyrdProcChance * 2f);
                if (wyrdDamage > 0)
                    encounter.ApplyDamage(resolvedTarget.Id, wyrdDamage, actorId, 1f);

                _logger.LogDebug("Wyrd proc triggered for actor {ActorId} against {TargetId}", actorId, resolvedTarget.Id);
            }

            _logger.LogDebug("Actor {ActorId} used {Ability} on {TargetId} for {Damage} damage (x{Mult}, crit:{Crit})",
                actorId, abilityName, resolvedTarget.Id, finalDamage, multiplier, isCrit);
        }
        else if (ability.Category == AbilityCategory.Heal)
        {
            var resolvedTarget = ResolveHealTarget(encounter, currentActor, ability, targetId);
            if (resolvedTarget is not null)
            {
                // Heals always succeed — apply ±20% variance
                int baseHeal = ability.BasePower + currentActor.Level * 2;
                if (baseHeal <= 0) baseHeal = currentActor.MaxHp / 4; // fallback: heal 25% max HP
                double variance = baseHeal * 0.2;
                int actualHeal = Math.Max(1, (int)(baseHeal + (Random.Shared.NextDouble() * variance * 2.0 - variance)));
                resolvedTarget.Heal(actualHeal);
                actionNarration = $"{currentActor.Name} uses {ability.Name} on {resolvedTarget.Name}, restoring {actualHeal} HP.";
            }
        }

        encounter.CheckEndState();

        if (encounter.State == EncounterState.InProgress)
            encounter.AdvanceTurn();

        return Task.FromResult((true, actionNarration, (Encounter?)encounter));
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

    /// <summary>Returns the danger level stored when the encounter was created, or 0.</summary>
    public int GetEncounterDangerLevel(Guid encounterId)
        => _encounterDangerLevels.TryGetValue(encounterId, out var dl) ? dl : 0;

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
