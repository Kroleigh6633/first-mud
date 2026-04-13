using FirstMud.Application.Content;
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
    private readonly PartyScalingCurve _partyScaling;

    public CombatSimulationService(Random rng)
        : this(rng, new PartyScalingCurve(0.0)) { }

    /// <summary>
    /// Overload used by the encounter-sim tool and progression-sim so the
    /// sim applies the same party-scaling buff the live game does. Pass
    /// <see cref="PartyScalingCurve"/> with ScalingPerTier=0 for legacy /
    /// baseline sims that want the pre-fix numbers.
    /// </summary>
    public CombatSimulationService(Random rng, PartyScalingCurve partyScaling)
    {
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _partyScaling = partyScaling ?? throw new ArgumentNullException(nameof(partyScaling));
    }

    // ─── Party composition ──────────────────────────────────────────────────

    public sealed record PartyMember(
        CompanionType Type,
        MagicElement Element,
        int Layer,
        int Level);

    /// <summary>
    /// Sim-side proxy for player loadout axes the live game tracks separately.
    /// Keeps playbooks honest: gear and imbue should affect the fight
    /// DIFFERENTLY from raw level so a `gear-only` and `imbue-only` playbook
    /// don't collapse into the level curve.
    ///
    /// Multipliers (documented in tools/design/playbooks/README.md):
    ///   - GearTier t: +15% weapon damage per tier (Strike + bolt BasePower),
    ///                 and player takes (1 - 0.07*t) incoming damage (armor).
    ///                 Gear does NOT scale HP pool.
    ///   - ImbueLevel i: +20% ability power per imbue level on magical abilities
    ///                   (Weave Bolt / Restore / elemental abilities) PLUS a flat
    ///                   +4*i to every ability base. Imbues do NOT scale HP.
    ///   - CompanionCount / CompanionLayer: already modelled via the party list.
    /// </summary>
    public sealed record CombatContext(
        int PlayerLevel,
        MagicElement PlayerElement,
        int GearTier,
        int ImbueLevel,
        IReadOnlyList<PartyMember> Companions,
        IReadOnlyList<MonsterTemplate> Enemies);

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

    /// <summary>
    /// Context-aware overload. Applies gear-tier and imbue-level effects
    /// DISTINCTLY from raw playerLevel so playbooks can isolate each axis.
    /// See <see cref="CombatContext"/> for the multiplier documentation.
    ///
    /// Implementation: builds the level-based encounter via the legacy path
    /// then re-creates the player combatant with gear- and imbue-tuned
    /// abilities and agility. Combatant is immutable post-creation, so the
    /// player is rebuilt rather than mutated.
    /// </summary>
    public Encounter BuildEncounter(CombatContext ctx)
    {
        var encounter = BuildEncounter(ctx.PlayerElement, ctx.PlayerLevel, ctx.Companions, ctx.Enemies);
        if (ctx.GearTier == 0 && ctx.ImbueLevel == 0) return encounter;

        var oldPlayer = encounter.Combatants.First(c => c.CombatantType == CombatantType.Player);

        double gearMult  = 1.0 + 0.15 * ctx.GearTier;
        double imbueMult = 1.0 + 0.20 * ctx.ImbueLevel;
        int flatImbue    = 4 * ctx.ImbueLevel;

        var scaled = new List<CombatAbility>(oldPlayer.Abilities.Count);
        foreach (var ab in oldPlayer.Abilities)
        {
            double mult = ab.Name == "Strike" ? gearMult
                        : ab.Category == AbilityCategory.Attack ? imbueMult
                        : 1.0;
            int newBase = (int)Math.Round(ab.BasePower * mult) + flatImbue;
            scaled.Add(ab with { BasePower = newBase });
        }

        // Gear mitigation proxy: bump Agility by +3 per gear tier (raises dodge
        // chance in ResolveAction without modifying the HP pool).
        int newAgility = oldPlayer.Agility + 3 * ctx.GearTier;

        var newPlayer = Combatant.Create(
            oldPlayer.Name,
            CombatantType.Player,
            oldPlayer.SourceEntityId,
            oldPlayer.MaxHp,
            oldPlayer.Speed,
            oldPlayer.Element,
            isPlayerSide: true,
            oldPlayer.Level,
            scaled,
            agility: newAgility);

        // Rebuild encounter with the new player + existing companions/enemies.
        var playerSide = new List<Combatant> { newPlayer };
        playerSide.AddRange(encounter.Combatants.Where(c => c.CombatantType == CombatantType.Companion));
        var enemySide = encounter.Combatants.Where(c => !c.IsPlayerSide).ToList();
        return Encounter.Create(Guid.NewGuid(), Guid.NewGuid(), playerSide, enemySide);
    }

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

        // Party scaling: symmetric buff vs monster danger curve.
        var avgLayer = PartyScaling.AvgCompanionLayer(companions.Select(c => c.Layer).ToList());
        var partyFactor = PartyScaling.Factor(_partyScaling, playerLevel, avgLayer);
        combatMaxHp = (int)(combatMaxHp * partyFactor);

        var strikeAbility = new CombatAbility(
            "Strike",
            levelScaledStrikeBase + statStrikeBonus,
            0,
            playerElement,
            AbilityTargetType.SingleEnemy,
            AbilityCategory.Attack);

        var playerAbilities = PartyScaling.ScaleAbilities(new List<CombatAbility>
        {
            strikeAbility,
            new("Weave Bolt", levelScaledBoltBase + statSpellBonus, 10, playerElement,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
            new("Restore", levelScaledRestoreBase, 5, MagicElement.Aether,
                AbilityTargetType.Self, AbilityCategory.Heal),
        }, partyFactor);

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

        // Disambiguate companion names so parties with duplicate (Type,Element,Layer)
        // don't collide downstream when we materialize the damage dictionary by name.
        // Before this, two L5-Earth CapturedMonsters both produced
        // "CapturedMonster-Earth-L5" and Run()'s final ToDictionary() threw
        // "An item with the same key has already been added. Key: CapturedMonster-Earth-L5".
        int companionIndex = 0;
        foreach (var c in companions)
        {
            companionIndex++;
            var abilities = CompanionAbilityFactory.Build(c.Type, c.Element, c.Layer, c.Level);
            var companionHp    = 50 + c.Level * 10 + c.Layer * 5;
            var companionSpeed = 6 + c.Level;

            companionHp = (int)(companionHp * partyFactor);
            var scaledAbilities = PartyScaling.ScaleAbilities(abilities, partyFactor);

            playerSide.Add(Combatant.Create(
                $"{c.Type}-{c.Element}-L{c.Layer}#{companionIndex}",
                CombatantType.Companion,
                Guid.NewGuid(),
                companionHp,
                companionSpeed,
                c.Element,
                isPlayerSide: true,
                c.Level,
                scaledAbilities,
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
    ///
    /// <paramref name="dangerLevel"/> mirrors the live game's multi-attack rule
    /// (<see cref="FirstMud.GameServer.Handlers.CombatHelpers.ProcessEnemyTurnsAsync"/>):
    /// danger 7-8 gives enemies 2 actions/turn, danger 9-10 gives 3. Required
    /// or the sim drastically under-estimates mid/high-danger difficulty vs.
    /// what the live game actually spawns.
    /// </summary>
    public SimulationResult Run(Encounter encounter, int maxRounds = 60, int dangerLevel = 0)
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

            // Multi-attack: mirror CombatHelpers.ProcessEnemyTurnsAsync's
            // dangerLevel switch so the sim matches the live game.
            int actionsPerTurn = actor.CombatantType == CombatantType.Monster
                ? dangerLevel switch { >= 9 => 3, >= 7 => 2, _ => 1 }
                : 1;

            for (int a = 0; a < actionsPerTurn; a++)
            {
                if (encounter.State != EncounterState.InProgress) break;
                // Re-pick target each sub-action so a dead target isn't hit twice.
                if (actor.CombatantType == CombatantType.Monster)
                    targetId = encounter.Combatants.FirstOrDefault(c => c.IsPlayerSide && !c.IsDefeated)?.Id;
                ResolveAction(encounter, actor, chosen, targetId, damageByCompanion);
                encounter.CheckEndState();
            }

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

        // Defensive aggregation: we now guarantee unique names via the #index
        // suffix in BuildEncounter, but keep this grouping so a future caller
        // that constructs its own encounter with duplicate combatant names
        // still gets a sane result instead of a hard crash.
        var damageByName = damageByCompanion
            .GroupBy(kv => companionName[kv.Key])
            .ToDictionary(g => g.Key, g => g.Sum(kv => kv.Value));

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
