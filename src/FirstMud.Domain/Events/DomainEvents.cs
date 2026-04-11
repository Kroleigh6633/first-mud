using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Domain.Events;

public interface IDomainEvent
{
    Guid EventId { get; }
    DateTimeOffset OccurredAt { get; }
}

public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; } = Guid.NewGuid();
    public DateTimeOffset OccurredAt { get; } = DateTimeOffset.UtcNow;
}

// Player events
public record MagicAffinityRevealedEvent(Guid PlayerId, MagicElement Element, MagicPolarity Polarity) : DomainEvent;
public record PlayerLeveledUpEvent(Guid PlayerId, int NewLevel) : DomainEvent;
public record WeaveCriticalEvent(Guid PlayerId) : DomainEvent;
public record WyrdTangleHighEvent(Guid PlayerId, int TangleLevel) : DomainEvent;
public record PortalUnlockedEvent(Guid PlayerId, WorldId WorldId) : DomainEvent;
public record FactionReputationTierChangedEvent(Guid PlayerId, FactionId FactionId, ReputationTier OldTier, ReputationTier NewTier) : DomainEvent;

// Companion events
public record CompanionLayerUnlockedEvent(Guid CompanionId, Guid OwnerId, int NewLayer) : DomainEvent;
public record CompanionLayerDriftedEvent(Guid CompanionId, Guid OwnerId, int CurrentLayer) : DomainEvent;
public record CompanionDepartedEvent(Guid CompanionId, Guid OwnerId, string CompanionName) : DomainEvent;
public record MonsterEvolvedEvent(Guid CompanionId, Guid OwnerId, int NewEvolutionTier, string Branch) : DomainEvent;

// Crafting events
public record CraftingSucceededEvent(Guid PlayerId, Guid ItemId, string ItemName, bool IsDiscovery) : DomainEvent;
public record CraftingFailedEvent(Guid PlayerId, string RecipeName, CraftingFailureType FailureType) : DomainEvent;

// Combat events (legacy)
public record CombatStartedEvent(Guid PlayerId, Guid EncounterId) : DomainEvent;
public record CombatEndedEvent(Guid EncounterId, Guid PlayerId, EncounterState Outcome) : DomainEvent;
public record PlayerDiedEvent(Guid PlayerId, Position LastPosition) : DomainEvent;

// Turn-based combat events
public record CombatTurnAdvancedEvent(Guid EncounterId, Guid ActingCombatantId, int RoundNumber) : DomainEvent;
public record CombatDamageDealtEvent(Guid EncounterId, Guid AttackerId, Guid TargetId, int Damage, float ElementMultiplier) : DomainEvent;

public enum CraftingFailureType
{
    NearMiss,
    UnexpectedResult,
    ComponentLoss,
    Discovery
}
