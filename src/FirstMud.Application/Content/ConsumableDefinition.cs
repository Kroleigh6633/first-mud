namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven consumable definition, loaded from content/consumables.json.
///
/// This is the canonical source of truth for consumable effects — the old
/// hardcoded switch statements in UseConsumableCommandHandler.ResolveEffect
/// and ConsumableHelper.ResolveEffect have been replaced by lookups against
/// these records.
/// </summary>
public sealed record ConsumableDefinition(
    string Id,
    string MatchToken,
    string EffectType,          // "Heal" | "RestoreWeave" | "Buff"
    int Amount,
    string? BuffKey,
    float BuffValue,
    string? PriorityGroup,      // "Healing" | "Weave" | "Buff"
    int PriorityRank);
