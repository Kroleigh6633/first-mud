using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;

namespace FirstMud.Domain.Entities;

public class Encounter
{
    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Guid ZoneId { get; private set; }
    public EncounterState State { get; private set; }
    public int CurrentTurnIndex { get; private set; }
    public int RoundNumber { get; private set; }

    private readonly List<Combatant> _combatants = [];
    public IReadOnlyList<Combatant> Combatants => _combatants.AsReadOnly();

    // Ordered list of combatant IDs by initiative (Speed descending)
    private readonly List<Guid> _turnOrder = [];
    public IReadOnlyList<Guid> TurnOrder => _turnOrder.AsReadOnly();

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private Encounter() { }

    public static Encounter Create(
        Guid playerId,
        Guid zoneId,
        IEnumerable<Combatant> playerSide,
        IEnumerable<Combatant> enemySide)
    {
        var encounter = new Encounter
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            ZoneId = zoneId,
            State = EncounterState.InProgress,
            CurrentTurnIndex = 0,
            RoundNumber = 1
        };

        encounter._combatants.AddRange(playerSide);
        encounter._combatants.AddRange(enemySide);

        // Sort by Speed descending; ties broken by insertion order (stable-ish via index)
        var ordered = encounter._combatants
            .OrderByDescending(c => c.Speed)
            .Select(c => c.Id)
            .ToList();

        encounter._turnOrder.AddRange(ordered);

        encounter._domainEvents.Add(new CombatStartedEvent(playerId, encounter.Id));

        return encounter;
    }

    /// <summary>
    /// Returns the combatant whose turn it currently is, skipping defeated combatants.
    /// Returns null if the encounter is over.
    /// </summary>
    public Combatant? CurrentActor
    {
        get
        {
            if (State != EncounterState.InProgress || _turnOrder.Count == 0)
                return null;

            // Return the non-defeated combatant at CurrentTurnIndex
            // (index was already advanced past defeated ones by AdvanceTurn)
            var id = _turnOrder[CurrentTurnIndex];
            return _combatants.FirstOrDefault(c => c.Id == id);
        }
    }

    /// <summary>
    /// Advances to the next alive combatant. Increments RoundNumber when all have acted.
    /// Raises CombatTurnAdvancedEvent.
    /// </summary>
    public void AdvanceTurn()
    {
        if (State != EncounterState.InProgress) return;

        var totalSlots = _turnOrder.Count;
        if (totalSlots == 0) return;

        int steps = 0;
        do
        {
            CurrentTurnIndex = (CurrentTurnIndex + 1) % totalSlots;
            if (CurrentTurnIndex == 0)
                RoundNumber++;

            steps++;
            if (steps > totalSlots)
                break; // all defeated — CheckEndState will handle it
        }
        while (IsCurrentActorDefeated());

        var actorId = _turnOrder[CurrentTurnIndex];
        _domainEvents.Add(new CombatTurnAdvancedEvent(Id, actorId, RoundNumber));
    }

    /// <summary>
    /// Applies damage to the combatant with the given id. Raises CombatDamageDealtEvent.
    /// </summary>
    public void ApplyDamage(Guid targetId, int dmg, Guid attackerId, float elementMultiplier = 1.0f)
    {
        var target = _combatants.FirstOrDefault(c => c.Id == targetId);
        if (target is null) return;

        target.TakeDamage(dmg);
        _domainEvents.Add(new CombatDamageDealtEvent(Id, attackerId, targetId, dmg, elementMultiplier));
    }

    /// <summary>
    /// Checks whether the encounter has ended (all enemies or all player-side defeated).
    /// Transitions State accordingly and raises CombatEndedEvent.
    /// </summary>
    public void CheckEndState()
    {
        if (State != EncounterState.InProgress) return;

        var allEnemiesDefeated = _combatants
            .Where(c => !c.IsPlayerSide)
            .All(c => c.IsDefeated);

        var allPlayerSideDefeated = _combatants
            .Where(c => c.IsPlayerSide)
            .All(c => c.IsDefeated);

        if (allEnemiesDefeated)
        {
            State = EncounterState.Victory;
            _domainEvents.Add(new CombatEndedEvent(Id, PlayerId, EncounterState.Victory));
        }
        else if (allPlayerSideDefeated)
        {
            State = EncounterState.Defeat;
            _domainEvents.Add(new CombatEndedEvent(Id, PlayerId, EncounterState.Defeat));
        }
    }

    /// <summary>
    /// Immediately ends the encounter as Fled.
    /// </summary>
    public void Flee()
    {
        if (State != EncounterState.InProgress) return;

        State = EncounterState.Fled;
        _domainEvents.Add(new CombatEndedEvent(Id, PlayerId, EncounterState.Fled));
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private bool IsCurrentActorDefeated()
    {
        var id = _turnOrder[CurrentTurnIndex];
        var combatant = _combatants.FirstOrDefault(c => c.Id == id);
        return combatant?.IsDefeated ?? false;
    }
}
