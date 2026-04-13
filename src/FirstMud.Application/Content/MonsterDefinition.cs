using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven monster template loaded from content/monsters.json.
///
/// This is the canonical source of truth for monster base stats and ability
/// loadouts. The old hardcoded MonsterTemplate[][] pools in MonsterFactory
/// are now built from these records. Pack-size selection, HP/speed/power
/// scaling with danger, and boss composition remain in MonsterFactory — only
/// the static stat blocks were configified.
/// </summary>
public sealed record MonsterDefinition(
    string Id,
    string Name,
    string Biome,
    int Tier,
    int Hp,
    int Speed,
    int Level,
    MagicElement Element,
    IReadOnlyList<CombatAbility> Abilities);
