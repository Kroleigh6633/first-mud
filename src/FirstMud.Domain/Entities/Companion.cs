using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;

namespace FirstMud.Domain.Entities;

public class Companion
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public CompanionType Type { get; private set; }
    public MagicElement Element { get; private set; }
    public int CurrentLayer { get; private set; }
    public int UsageCounter { get; private set; }
    public float DriftAccumulator { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsPermanentlyGone { get; private set; }

    // Monster-specific
    public int EvolutionTier { get; private set; }
    public int Level { get; private set; }
    public string? EvolutionBranch { get; private set; }

    // Relationship depth (affects layer unlocks for Heroes and Wildfolk)
    public int RelationshipDepth { get; private set; }

    // Warning system — tracks how many times player has ignored companion warnings
    public int IgnoredWarnings { get; private set; }

    // Homestead duty — null = adventuring, set = assigned to homestead
    public HomesteadDuty? AssignedDuty { get; private set; }
    public DateTime? DutyStartedAt { get; private set; }

    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private static readonly Dictionary<CompanionType, int[]> LayerThresholds = new()
    {
        [CompanionType.Wildfolk] = [0, 200, 500, 1000, 2000, 4000],
        [CompanionType.HiredHero] = [0, 200, 600, 1200, 2500, 5000],
        [CompanionType.CapturedMonster] = [0, 150, 400, 900, 1800, 3600],
        [CompanionType.BoundShade] = [0, 300, 700, 1500, 3000, 6000],
        [CompanionType.ArdweldConstruct] = [0, 500, 1500, 3000, 6000, 12000],
    };

    private static readonly float DriftRatePerHour = 0.5f;
    private static readonly int MaxIgnoredWarnings = 3;

    private Companion() { }

    public static Companion Create(Guid ownerId, string name, CompanionType type, MagicElement element) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = ownerId,
        Name = name,
        Type = type,
        Element = element,
        CurrentLayer = 1,
        UsageCounter = 0,
        DriftAccumulator = 0f,
        IsActive = false,
        IsPermanentlyGone = false,
        EvolutionTier = 1,
        Level = 1,
        RelationshipDepth = 0,
        IgnoredWarnings = 0
    };

    public void RecordUsage(int usagePoints)
    {
        if (IsPermanentlyGone) return;

        UsageCounter += usagePoints;
        DriftAccumulator = Math.Max(0, DriftAccumulator - usagePoints * 0.1f);

        TryAdvanceLayer();
    }

    public void AccumulateDrift(float hours)
    {
        if (!IsActive)
            DriftAccumulator += hours * DriftRatePerHour;
        else
            DriftAccumulator += hours * DriftRatePerHour * 0.1f;

        if (DriftAccumulator >= 50f && CurrentLayer > 1)
        {
            CurrentLayer--;
            DriftAccumulator = 0f;
            _domainEvents.Add(new CompanionLayerDriftedEvent(Id, OwnerId, CurrentLayer));
        }
    }

    public void DeepRelationshipInteraction()
    {
        RelationshipDepth++;
        TryAdvanceLayer();
    }

    public void RecordIgnoredWarning()
    {
        IgnoredWarnings++;
        if (IgnoredWarnings >= MaxIgnoredWarnings && Type != CompanionType.ArdweldConstruct)
        {
            IsActive = false;
            _domainEvents.Add(new CompanionDepartedEvent(Id, OwnerId, Name));
        }
    }

    public void ResetWarnings() => IgnoredWarnings = 0;

    public void SetActive(bool active) => IsActive = active;

    /// <summary>
    /// Assigns the companion to homestead duty.
    /// The caller must ensure the companion is not in the active adventuring party first.
    /// </summary>
    public void AssignToHomestead(HomesteadDuty duty)
    {
        if (duty == HomesteadDuty.None)
            throw new InvalidOperationException("Use RecallFromHomestead to clear duty.");
        if (IsActive)
            throw new InvalidOperationException($"{Name} is still in the adventuring party. Deactivate first.");
        AssignedDuty = duty;
        DutyStartedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes the companion from homestead duty, allowing them to be re-added to the party.
    /// </summary>
    public void RecallFromHomestead()
    {
        AssignedDuty = null;
        DutyStartedAt = null;
    }

    /// <summary>
    /// Returns the aptitude multiplier (1–3 stars) for a given duty based on companion type.
    /// </summary>
    public int GetAptitude(HomesteadDuty duty) => (Type, duty) switch
    {
        (CompanionType.Wildfolk,         HomesteadDuty.Harvester) => 3,
        (CompanionType.Wildfolk,         HomesteadDuty.Salvager)  => 1,
        (CompanionType.Wildfolk,         HomesteadDuty.Guard)     => 2,
        (CompanionType.Wildfolk,         HomesteadDuty.Crafter)   => 2,
        (CompanionType.CapturedMonster,  HomesteadDuty.Harvester) => 2,
        (CompanionType.CapturedMonster,  HomesteadDuty.Salvager)  => 1,
        (CompanionType.CapturedMonster,  HomesteadDuty.Guard)     => 3,
        (CompanionType.CapturedMonster,  HomesteadDuty.Crafter)   => 1,
        (CompanionType.ArdweldConstruct, HomesteadDuty.Harvester) => 1,
        (CompanionType.ArdweldConstruct, HomesteadDuty.Salvager)  => 3,
        (CompanionType.ArdweldConstruct, HomesteadDuty.Guard)     => 3,
        (CompanionType.ArdweldConstruct, HomesteadDuty.Crafter)   => 3,
        (CompanionType.HiredHero,        HomesteadDuty.Harvester) => 2,
        (CompanionType.HiredHero,        HomesteadDuty.Salvager)  => 2,
        (CompanionType.HiredHero,        HomesteadDuty.Guard)     => 2,
        (CompanionType.HiredHero,        HomesteadDuty.Crafter)   => 2,
        // BoundShade — moderate at everything, not specialized
        (CompanionType.BoundShade,       HomesteadDuty.Harvester) => 2,
        (CompanionType.BoundShade,       HomesteadDuty.Salvager)  => 2,
        (CompanionType.BoundShade,       HomesteadDuty.Guard)     => 2,
        (CompanionType.BoundShade,       HomesteadDuty.Crafter)   => 1,
        _ => 1
    };

    public bool TryEvolve(string branch)
    {
        if (Type != CompanionType.CapturedMonster) return false;
        if (EvolutionTier >= 3) return false;
        if (Level < EvolutionTier * 20) return false;

        EvolutionTier++;
        EvolutionBranch = branch;
        _domainEvents.Add(new MonsterEvolvedEvent(Id, OwnerId, EvolutionTier, branch));
        return true;
    }

    public void MarkPermanentlyGone() => IsPermanentlyGone = true;

    private void TryAdvanceLayer()
    {
        if (CurrentLayer >= 6) return;

        var thresholds = LayerThresholds[Type];
        var nextThreshold = thresholds[CurrentLayer];

        if (UsageCounter >= nextThreshold)
        {
            CurrentLayer++;
            _domainEvents.Add(new CompanionLayerUnlockedEvent(Id, OwnerId, CurrentLayer));
        }
    }
}
