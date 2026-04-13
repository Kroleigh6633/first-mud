using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Domain.Services;

/// <summary>
/// Builds the full ability list for a companion based on their Type, Element, and current Layer (1-6).
/// Higher layers unlock progressively more powerful abilities.
/// Strategy: the player chooses WHICH companions to bring; companions auto-execute their best
/// available action so the player never micro-manages them in combat.
/// </summary>
public static class CompanionAbilityFactory
{
    /// <summary>
    /// Returns all combat abilities available to a companion at or below their current layer.
    /// Ability base powers scale with both companion level and bond layer so higher investment
    /// meaningfully increases companion combat contribution.
    /// </summary>
    public static IReadOnlyList<CombatAbility> Build(CompanionType type, MagicElement element, int layer, int companionLevel = 1)
    {
        layer = Math.Clamp(layer, 1, 6);
        companionLevel = Math.Max(1, companionLevel);
        return type switch
        {
            CompanionType.Wildfolk         => BuildWildfolk(element, layer, companionLevel),
            CompanionType.CapturedMonster  => BuildCapturedMonster(element, layer, companionLevel),
            CompanionType.ArdweldConstruct => BuildArdweldConstruct(element, layer, companionLevel),
            CompanionType.HiredHero        => BuildHiredHero(element, layer, companionLevel),
            CompanionType.BoundShade       => BuildHiredHero(element, layer, companionLevel), // BoundShade uses HiredHero table as fallback
            _                             => BuildWildfolk(element, layer, companionLevel),
        };
    }

    /// <summary>
    /// Scales a companion ability's base power by level and bond layer.
    /// Formula: basePower + (companionLevel * 2) + (layer * 3)
    /// A Bond-4 Level-10 companion does substantially more than a Bond-1 Level-1 companion.
    /// </summary>
    private static int ScaleAbilityPower(int basePower, int companionLevel, int layer)
        => basePower + (companionLevel * 2) + (layer * 3);

    // -------------------------------------------------------------------------
    // Wildfolk — balanced, nature allies
    // Layer 1: Basic attack (element-matched)
    // Layer 2: + Heal Ally (small)
    // Layer 3: + Elemental Strike (medium damage)
    // Layer 4: + Group Buff (+10% damage to party — modelled as a buff ability)
    // Layer 5: + Resurrect (revive fallen ally at 25% HP)
    // Layer 6: + Elemental Storm (AOE damage ultimate)
    // -------------------------------------------------------------------------

    private static IReadOnlyList<CombatAbility> BuildWildfolk(MagicElement element, int layer, int companionLevel)
    {
        var abilities = new List<CombatAbility>
        {
            new($"{element} Touch", ScaleAbilityPower(14, companionLevel, layer), 0, element, AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
        };

        if (layer >= 2)
            abilities.Add(new("Nature Mend", ScaleAbilityPower(18, companionLevel, layer), 4, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Heal));

        if (layer >= 3)
            abilities.Add(new($"{element} Strike", ScaleAbilityPower(28, companionLevel, layer), 6, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack));

        if (layer >= 4)
            abilities.Add(new("Pack Bond", ScaleAbilityPower(20, companionLevel, layer), 8, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Buff));

        if (layer >= 5)
            abilities.Add(new("Wild Revive", ScaleAbilityPower(25, companionLevel, layer), 12, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Revive));

        if (layer >= 6)
            abilities.Add(new($"{element} Storm", ScaleAbilityPower(55, companionLevel, layer), 15, element,
                AbilityTargetType.AllEnemies, AbilityCategory.Attack));

        return abilities.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // CapturedMonster — raw power, offense-focused
    // Layer 1: Claw (physical)
    // Layer 2: + Elemental Breath (element-matched, medium)
    // Layer 3: + Frenzy (high attack, self-weaken)
    // Layer 4: + Terrify (reduce enemy output)
    // Layer 5: + Devour (lifesteal attack)
    // Layer 6: + Rampage (triple-hit ultimate)
    // -------------------------------------------------------------------------

    private static IReadOnlyList<CombatAbility> BuildCapturedMonster(MagicElement element, int layer, int companionLevel)
    {
        var abilities = new List<CombatAbility>
        {
            new("Claw", ScaleAbilityPower(16, companionLevel, layer), 0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
        };

        if (layer >= 2)
            abilities.Add(new($"{element} Breath", ScaleAbilityPower(26, companionLevel, layer), 5, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack));

        if (layer >= 3)
            abilities.Add(new("Frenzy", ScaleAbilityPower(38, companionLevel, layer), 0, MagicElement.Earth,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack));

        if (layer >= 4)
            abilities.Add(new("Terrify", ScaleAbilityPower(12, companionLevel, layer), 8, MagicElement.Aether,
                AbilityTargetType.SingleEnemy, AbilityCategory.Debuff));

        if (layer >= 5)
            abilities.Add(new("Devour", ScaleAbilityPower(32, companionLevel, layer), 6, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal));

        if (layer >= 6)
            abilities.Add(new("Rampage", ScaleAbilityPower(60, companionLevel, layer), 10, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack));

        return abilities.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // ArdweldConstruct — defensive, support
    // Layer 1: Shield Bash (low damage)
    // Layer 2: + Protect (redirect damage)
    // Layer 3: + Repair (heal self)
    // Layer 4: + Fortify (party HP boost)
    // Layer 5: + Reflect (return damage)
    // Layer 6: + Aegis (absorb hits ultimate)
    // -------------------------------------------------------------------------

    private static IReadOnlyList<CombatAbility> BuildArdweldConstruct(MagicElement element, int layer, int companionLevel)
    {
        var abilities = new List<CombatAbility>
        {
            new("Shield Bash", ScaleAbilityPower(10, companionLevel, layer), 0, element, AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
        };

        if (layer >= 2)
            abilities.Add(new("Protect", 0, 6, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Buff));

        if (layer >= 3)
            abilities.Add(new("Repair", ScaleAbilityPower(22, companionLevel, layer), 5, MagicElement.Aether,
                AbilityTargetType.Self, AbilityCategory.Heal));

        if (layer >= 4)
            abilities.Add(new("Fortify", 0, 10, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Buff));

        if (layer >= 5)
            abilities.Add(new("Reflect", 0, 8, element,
                AbilityTargetType.Self, AbilityCategory.Buff));

        if (layer >= 6)
            abilities.Add(new("Aegis", 0, 14, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Buff));

        return abilities.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // HiredHero — versatile, jack of all trades
    // Layer 1: Sword Strike
    // Layer 2: + Quick Shot (ranged)
    // Layer 3: + Battle Cry (party buff)
    // Layer 4: + Tactical Strike (targets weakest enemy)
    // Layer 5: + Rally (party heal)
    // Layer 6: + Commander (extra actions ultimate — modelled as AoE buff)
    // -------------------------------------------------------------------------

    private static IReadOnlyList<CombatAbility> BuildHiredHero(MagicElement element, int layer, int companionLevel)
    {
        var abilities = new List<CombatAbility>
        {
            new("Sword Strike", ScaleAbilityPower(16, companionLevel, layer), 0, element, AbilityTargetType.SingleEnemy, AbilityCategory.Attack),
        };

        if (layer >= 2)
            abilities.Add(new("Quick Shot", ScaleAbilityPower(20, companionLevel, layer), 0, MagicElement.Air,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack));

        if (layer >= 3)
            abilities.Add(new("Battle Cry", 0, 8, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Buff));

        if (layer >= 4)
            abilities.Add(new("Tactical Strike", ScaleAbilityPower(30, companionLevel, layer), 6, element,
                AbilityTargetType.SingleEnemy, AbilityCategory.Attack));

        if (layer >= 5)
            abilities.Add(new("Rally", ScaleAbilityPower(20, companionLevel, layer), 10, MagicElement.Aether,
                AbilityTargetType.SingleAlly, AbilityCategory.Heal));

        if (layer >= 6)
            abilities.Add(new("Commander", 0, 15, MagicElement.Aether,
                AbilityTargetType.AllEnemies, AbilityCategory.Buff));

        return abilities.AsReadOnly();
    }

    // -------------------------------------------------------------------------
    // Companion AI helpers — used by CombatHelpers when auto-playing companions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selects the best ability for a companion to use given the current encounter state.
    /// Priority:
    ///   1. Heal a low-HP ally if companion has a heal and an ally is below 30% HP
    ///   2. Buff if companion has a buff and it hasn't been used (heuristic: use once per encounter)
    ///   3. Use strongest attack on first living enemy
    /// </summary>
    public static (CombatAbility Ability, bool TargetAlly) SelectBestAction(
        IReadOnlyList<CombatAbility> abilities,
        IReadOnlyList<(Guid Id, int Hp, int MaxHp, bool IsPlayerSide)> allCombatants,
        bool hasUsedBuff)
    {
        var allies = allCombatants.Where(c => c.IsPlayerSide && c.Hp > 0).ToList();
        var enemies = allCombatants.Where(c => !c.IsPlayerSide && c.Hp > 0).ToList();

        // Priority 1: heal if any ally critically low
        var healAbilities = abilities.Where(a => a.Category is AbilityCategory.Heal or AbilityCategory.Revive).ToList();
        if (healAbilities.Count > 0)
        {
            var criticalAlly = allies.FirstOrDefault(a => (float)a.Hp / Math.Max(a.MaxHp, 1) < 0.30f);
            if (criticalAlly != default)
                return (healAbilities.OrderByDescending(a => a.BasePower).First(), true);
        }

        // Priority 2: one-time buff
        var buffAbilities = abilities.Where(a => a.Category == AbilityCategory.Buff).ToList();
        if (!hasUsedBuff && buffAbilities.Count > 0 && allies.Count > 0)
            return (buffAbilities.First(), true);

        // Priority 3: strongest attack
        var attackAbilities = abilities.Where(a => a.Category is AbilityCategory.Attack or AbilityCategory.Lifesteal).ToList();
        if (attackAbilities.Count > 0)
            return (attackAbilities.OrderByDescending(a => a.BasePower).First(), false);

        // Fallback: any ability
        var first = abilities.FirstOrDefault();
        return first is not null ? (first, false) : default;
    }
}
