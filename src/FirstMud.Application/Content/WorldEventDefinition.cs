using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Data-driven world-event template loaded from content/world-events.json.
///
/// World-events are long-period narrative beats fired by the world-state machine
/// (market shifts, faction sweeps, seasonal weather, Ashen Court moves). Runtime
/// scheduling (tick cadence, priority arbitration, durable world-state flag writes)
/// lives in C#. This record is the *authoring surface* only: identity,
/// trigger shape, duration, effect list, narrative flavour.
///
/// Today no event runtime consumes these records — they ship as pure data and
/// a later ticket wires a <c>WorldEventService</c> that enumerates
/// <see cref="IContentProvider.AllEvents"/> and maps event ids to live behaviour.
/// See <c>docs/design/world-events.md</c> for the canonical 12-event catalog.
/// </summary>
public sealed record WorldEventDefinition(
    string Id,
    string DisplayName,
    string Family,
    int Priority,
    string Description,
    WorldEventTrigger Trigger,
    WorldEventDuration Duration,
    IReadOnlyList<WorldEventEffect> Effects,
    IReadOnlyList<WorldEventEffect> OnExpire,
    IReadOnlyList<FactionId> FactionTags,
    IReadOnlyList<string> ZoneTags,
    IReadOnlyList<string> QuestTags,
    string? WorldStateFlag,
    bool OneTime);

/// <summary>
/// How the world-state machine decides when to fire an event. <see cref="Kind"/>
/// discriminates: most kinds use one or two of the numeric/string fields, and
/// every kind optionally carries <see cref="Prose"/> — the original design-doc
/// sentence, preserved verbatim so the runtime implementor has the canonical
/// intent when the structured fields don't fully capture it.
/// </summary>
public sealed record WorldEventTrigger(
    string Kind,
    int? EveryDays,
    int? EveryMinutes,
    int? Day,
    FactionId? FactionId,
    ReputationTier? MinTier,
    int? Min,
    string? QuestId,
    string? Outcome,
    string? Prose);

/// <summary>
/// How long an event stays active once fired. Exactly one of
/// <see cref="Days"/> / <see cref="Minutes"/> / <see cref="Permanent"/> is set.
/// </summary>
public sealed record WorldEventDuration(
    int? Days,
    int? Minutes,
    bool Permanent);

/// <summary>
/// One state-change an event applies when it fires (or when it expires, via
/// <see cref="WorldEventDefinition.OnExpire"/>). <see cref="Type"/> discriminates:
/// the remaining fields are optional and the runtime is expected to ignore
/// fields that don't belong to a given effect type.
/// </summary>
public sealed record WorldEventEffect(
    string Type,
    string? ZoneId,
    string? NpcId,
    string? NpcName,
    FactionId? FactionId,
    string? Item,
    double? Multiplier,
    int? Delta,
    string? Flag,
    string? Value,
    string? Text,
    bool? Available,
    string? QuestId);
