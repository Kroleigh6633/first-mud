using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven NPC template loaded from <c>content/npcs.json</c>.
///
/// Sourced from the canonical voice docs in
/// <c>docs/design/npc-voices.md</c> and <c>npc-voices-batch2.md</c>. Voice
/// tells are pulled verbatim from those documents — the design docs remain
/// authoritative for tone; this record is plumbing.
///
/// There is no live <c>Npc</c> domain entity yet. This catalog is purely
/// additive metadata that downstream systems (dialogue trees, quest givers,
/// faction-introduction reputation offsets, world rendering) can consume as
/// they come online.
/// </summary>
public sealed record NpcDefinition(
    string Id,
    string DisplayName,
    FactionId? FactionId,
    string HomeZoneId,
    NpcRole Role,
    string ShortDescription,
    IReadOnlyList<string> VoiceTells,
    string? DialogueRootId,
    int? StartingReputation);

/// <summary>
/// Coarse role classification for catalog browsing. Not intended as a
/// behavior dispatch — runtime systems pick behavior off other fields.
/// </summary>
public enum NpcRole
{
    Questgiver,
    Shopkeeper,
    Civilian,
    Patrol,
    Landmark,
}
