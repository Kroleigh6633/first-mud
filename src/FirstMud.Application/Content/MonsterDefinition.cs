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
    IReadOnlyList<CombatAbility> Abilities,
    bool IsBoss = false);

/// <summary>
/// Bonus kill-drop entry keyed to a boss monster. Loaded from the top-level
/// <c>bossDrops[]</c> array in <c>content/monsters.json</c> (introduced in
/// task #134). Bosses roll these AFTER the standard biome/equipment loot pool
/// — boss drops are additive, not a replacement.
/// </summary>
/// <param name="MonsterId">Boss monster id (must exist in the monsters registry and be flagged <c>isBoss</c>).</param>
/// <param name="ItemName">Item name to award on a successful roll. Resolved against loot-table drop-pool entries for category / description; unknown items default to Component.</param>
/// <param name="DropChance">Independent per-entry probability in (0, 1].</param>
public sealed record BossDropDefinition(
    string MonsterId,
    string ItemName,
    double DropChance);
