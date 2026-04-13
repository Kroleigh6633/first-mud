using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Services;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Application.Services;

/// <summary>
/// Deterministic combat resolver used by design-time simulation tools.
///
/// Mirrors the per-round math of <see cref="CombatService"/> exactly
/// (hit/dodge/damage/crit/element formulas) but accepts an injected
/// <see cref="Random"/> so runs are reproducible under a seed, and has no
/// dependency on SignalR, repositories, narration, or services.
///
/// Used by <c>tools/design/encounter-sim</c>. NOT wired into the live game
/// loop — the game path still goes through <see cref="CombatService"/>.
///
/// Keep this file in lock-step with <see cref="CombatService.ExecuteActionAsync"/>.
/// If combat math changes there, change it here too (or both call a shared helper).
/// </summary>
public sealed class CombatSimulationService
{
    private readonly Random _rng;

    public CombatSimulationService(Random rng)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
    }

    // ─── Party composition ──────────────────────────────────────────────────

    public sealed record PartyMember(
        CompanionType Type,
        MagicElement Element,
        int Layer,
        int Level);

    public sealed record SimulationResult(
        Outcome Outcome,
        int Rounds,
        int PlayerHpRemaining,
        int PlayerMaxHp,
        int DamageTakenByPlayer,
        IReadOnlyDictionary<string, int> DamageByCompanion,
        string? MvpCompanionName);

    public enum Outcome { Victory, Defeat, Timeout }

    // ─── Build sides (mirrors CombatService.StartEncounterAsync, no gear) ───

    public Encounter BuildEncounter(
        MagicElement playerElement,
        int playerLevel,
        IReadOnlyList<PartyMember> companions,
        IReadOnlyList<MonsterTemplate> enemies)
    {
        // Archetype starting stats then apply per-level gains (playerLevel-1 times).
        var (str0, agi0, int0, fort0, spd0, maxHp0) = Player.GetArchetypeStats(playerElement);
        var (strGain, agiGain, intGain, fortGain, spdGain, hpGain) =
            Player.GetArchetypeLevelGains(playerElement);

        int strength  = str0  + strGain  * (playerLevel - 1);
        int agility   = agi0  + agiGain  * (playerLevel - 1);
        int intellect = int0  + intGain  * (playerLevel - 1);
        int fortitude = fort0 + fortGain * (playerLevel - 1);
        int speed     = spd0  + spdGain  * (playerLevel - 1);
        int effMaxHp  = maxHp0 + hpGain  * (playerLevel - 1);

        int statStrikeBonus = strength / 5;
        int statSpellBonus  = intellect / 5;
        int statFortBonus   = fortitude / 2;

        int combatMaxHp = effMaxHp + statFortBonus;
        int combatSpeed = speed;

        int levelScaledStrikeBase  = 18 + (playerLevel * 2);
        int levelScaledBoltBase    = 30 + (playerLevel * 3);
        int levelScaledRestoreBase = 30 + (playerLevel * 2);

        var strikeAbility = new CombatAbility(
            "Strike",
            levelScaledStrikeBase + statStrikeBonus,
            0,
            playerElement,
            AbilityTargetType.SingleEnemy,
            AbilityCategory.Attack);

        var playerAbilities = new List<CombatAbility>
        {
            strikeAbility,
            new("Weave Bolt", levelScaledBoltBase + statSpellBonus, 10, playerElement,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
            new("Restore", levelScaledRestoreBase, 5, MagicElement.Aether,
                AbilityTargetType.Self, AbilityCategory.Heal),
        };

        var playerCombatant = Combatant.Create(
            "Player",
            CombatantType.Player,
            Guid.NewGuid(),
            combatMaxHp,
            combatSpeed,
            playerElement,
            isPlayerSide: true,
            playerLevel,
            playerAbilities,
            agility: agility);

        var playerSide = new List<Combatant> { playerCombatant };

        foreach (var c in companions)
        {
            var abilities = CompanionAbilityFactory.Build(c.Type, c.Element, c.Layer, c.Level);
            var companionHp    = 50 + c.Level * 10 + c.Layer * 5;
            var companionSpeed = 6 + c.Level;

            playerSide.Add(Combatant.Create(
                $"{c.Type}-{c.Element}-L{c.Layer}",
                CombatantType.Companion,
                Guid.NewGuid(),
                companionHp,
                companionSpeed,
                c.Element,
                isPlayerSide: true,
                c.Level,
                abilities,
                agility: c.Level + 5));
        }

        var enemySide = enemies.Select(t => Combatant.Create(
            t.Name,
            CombatantType.Monster,
            Guid.NewGuid(),
            t.Hp,
            t.Speed,
            t.Element,
            isPlayerSide: false,
            t.Level,
            t.Abilities,
            agility: t.Speed)).ToList();

        return Encounter.Create(Guid.NewGuid(), Guid.NewGuid(), playerSide, enemySide);
    }

    // ─── Round loop ─────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a single encounter to completion (victory/defeat) or returns Timeout
    /// after <paramref name="maxRounds"/>. Deterministic per the injected RNG.
    /// </summary>
    public SimulationResult Run(Encounter encounter, int maxRounds = 60)
    {
        var playerStart = encounter.Combatants.First(c => c.CombatantType == CombatantType.Player);
        int playerMaxHp = playerStart.MaxHp;
        var damageByCompanion = new Dictionary<Guid, int>();
        var companionName = new Dictionary<Guid, string>();
        foreach (var c in encounter.Combatants.Where(c => c.CombatantType == CombatantType.Companion))
        {
            damageByCompanion[c.Id] = 0;
            companionName[c.Id] = c.Name;
        }

        var buffUsed = new HashSet<Guid>();
        int safety = 0;

        while (encounter.State == EncounterState.InProgress && encounter.RoundNumber <= maxRounds && safety++ < 2000)
        {
            var actor = encounter.CurrentActor;
            if (actor is null) break;

            CombatAbility? chosen;
            Guid? targetId = null;

            if (actor.CombatantType == CombatantType.Player)
            {
                chosen = PickPlayerAction(actor);
                targetId = encounter.Combatants.FirstOrDefault(c => !c.IsPlayerSide && !c.IsDefeated)?.Id;
            }
            else if (actor.CombatantType == CombatantType.Companion)
            {
                var snapshot = encounter.Combatants
                    .Select(c => (c.Id, c.CurrentHp, c.MaxHp, c.IsPlayerSide))
                    .ToList();
                var (ability, targetAlly) =
                    CompanionAbilityFactory.SelectBestAction(actor.Abilities, snapshot, buffUsed.Contains(actor.Id));
                chosen = ability;
                if (chosen?.Category == AbilityCategory.Buff)
                    buffUsed.Add(actor.Id);
                if (targetAlly)
                {
                    targetId = encounter.Combatants
                        .Where(c => c.IsPlayerSide && !c.IsDefeated)
                        .OrderBy(c => (float)c.CurrentHp / Math.Max(c.MaxHp, 1))
                        .FirstOrDefault()?.Id;
                }
                else
                {
                    targetId = encounter.Combatants
                        .FirstOrDefault(c => !c.IsPlayerSide && !c.IsDefeated)?.Id;
                }
            }
            else // Monster
            {
                chosen = actor.Abilities
                    .Where(a => a.Category == AbilityCategory.Attack)
                    .OrderByDescending(a => a.BasePower)
                    .FirstOrDefault() ?? actor.Abilities.FirstOrDefault();
                targetId = encounter.Combatants.FirstOrDefault(c => c.IsPlayerSide && !c.IsDefeated)?.Id;
            }

            if (chosen is null)
            {
                encounter.CheckEndState();
                if (encounter.State == EncounterState.InProgress) encounter.AdvanceTurn();
                continue;
            }

            ResolveAction(encounter, actor, chosen, targetId, damageByCompanion);

            encounter.CheckEndState();
            if (encounter.State == EncounterState.InProgress)
                encounter.AdvanceTurn();
        }

        var playerFinal = encounter.Combatants.First(c => c.CombatantType == CombatantType.Player);
        Outcome outcome = encounter.State switch
        {
            EncounterState.Victory => Outcome.Victory,
            EncounterState.Defeat  => Outcome.Defeat,
            _                      => Outcome.Timeout,
        };

        Guid? mvp = damageByCompanion.Count > 0
            ? damageByCompanion.OrderByDescending(kv => kv.Value).First().Key
            : (Guid?)null;

        var damageByName = damageByCompanion.ToDictionary(
            kv => companionName[kv.Key],
            kv => kv.Value);

        return new SimulationResult(
            outcome,
            encounter.RoundNumber,
            playerFinal.CurrentHp,
            playerMaxHp,
            playerMaxHp - playerFinal.CurrentHp,
            damageByName,
            mvp.HasValue ? companionName[mvp.Value] : null);
    }

    private static CombatAbility PickPlayerAction(Combatant player)
    {
        // Auto-player: prefer Strike (no weave cost). Never flees or uses Restore in sim.
        return player.Abilities.FirstOrDefault(a =>
                   a.Category == AbilityCategory.Attack && a.WeaveCost == 0)
            ?? player.Abilities.First(a => a.Category == AbilityCategory.Attack);
    }

    // Mirrors CombatService.ExecuteActionAsync resolution with injected RNG.
    private void ResolveAction(
        Encounter encounter,
        Combatant actor,
        CombatAbility ability,
        Guid? targetId,
        Dictionary<Guid, int> damageByCompanion)
    {
        if (ability.Category == AbilityCategory.Attack || ability.Category == AbilityCategory.Lifesteal)
        {
            var target = encounter.Combatants.FirstOrDefault(c => c.Id == targetId && !c.IsDefeated);
            if (target is null) return;

            int hitChance = 70 + (actor.Level - target.Level) * 5 + actor.Speed * 2;
            hitChance = Math.Clamp(hitChance, 10, 95);
            if (_rng.Next(1, 101) > hitChance) return; // miss

            float dodgeChance = target.Agility * 1.5f - actor.Agility * 0.5f;
            dodgeChance = Math.Clamp(dodgeChance, 0f, 40f);
            if (_rng.Next(1, 101) <= (int)dodgeChance) return; // dodged

            int baseDamage = ability.BasePower + actor.Level * 2;
            double variance = baseDamage * 0.2;
            double rawActual = baseDamage + (_rng.NextDouble() * variance * 2.0 - variance);
            int variedDamage = Math.Max(1, (int)rawActual);

            float mult = ElementMatchup.GetMultiplier(ability.Element, target.Element);

            int critChance = 5 + (actor.Level - target.Level) * 2 + actor.Speed;
            critChance = Math.Clamp(critChance, 5, 35);
            bool isCrit = _rng.Next(1, 101) <= critChance;
            float critMult = isCrit ? 1.5f : 1.0f;

            int finalDamage = Math.Max(1, (int)(variedDamage * mult * critMult));
            encounter.ApplyDamage(target.Id, finalDamage, actor.Id, mult);

            if (actor.CombatantType == CombatantType.Companion && !target.IsPlayerSide)
                damageByCompanion[actor.Id] = damageByCompanion.GetValueOrDefault(actor.Id) + finalDamage;

            if (ability.LifestealPower > 0f)
            {
                var heal = (int)(finalDamage * ability.LifestealPower);
                if (heal > 0) actor.Heal(heal);
            }
        }
        else if (ability.Category == AbilityCategory.Heal)
        {
            var target = encounter.Combatants.FirstOrDefault(c => c.Id == targetId && !c.IsDefeated) ?? actor;
            int baseHeal = ability.BasePower + actor.Level * 2;
            if (baseHeal <= 0) baseHeal = actor.MaxHp / 4;
            double variance = baseHeal * 0.2;
            int healed = Math.Max(1, (int)(baseHeal + (_rng.NextDouble() * variance * 2.0 - variance)));
            target.Heal(healed);
        }
        // Buff/Debuff/Revive: no-op in sim (parity with CombatService which narrates but does not
        // implement mechanical effects for these categories yet).
    }
}
