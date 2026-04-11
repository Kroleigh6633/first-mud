using FirstMud.Domain.Enums;
using FirstMud.Domain.Events;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Domain.Entities;

public class Player
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int Level { get; private set; }
    public int Experience { get; private set; }

    // Magic
    public MagicElement PrimaryElement { get; private set; }
    public MagicPolarity Polarity { get; private set; }
    public Weave Weave { get; private set; } = Weave.Create(100);
    public bool ElementRevealed { get; private set; }
    public bool PolarityRevealed { get; private set; }

    // Wyrd
    public int WyrdTangle { get; private set; }
    public bool HasWyrdThread { get; private set; }

    // Stats
    public int Strength { get; private set; }
    public int Agility { get; private set; }
    public int Intellect { get; private set; }
    public int Fortitude { get; private set; }
    public int Speed { get; private set; }
    public int CraftingSkill { get; private set; }
    public int SalvageSkill { get; private set; }

    // Position
    public Position Position { get; private set; } = new(WorldId.Aeldran, 1, 0, 0);

    // Per-player crafting seed (never exposed to client directly)
    public int CraftingSeed { get; private set; }

    // Combat
    public int CurrentHp { get; private set; }
    public int MaxHp { get; private set; }
    public int ActionPoints { get; private set; }
    public int MaxActionPoints { get; private set; }

    // Reputation
    private readonly List<PlayerFactionReputation> _reputations = [];
    public IReadOnlyList<PlayerFactionReputation> Reputations => _reputations.AsReadOnly();

    // Companions (active slots — max 3)
    private readonly List<Guid> _activeCompanionIds = [];
    public IReadOnlyList<Guid> ActiveCompanionIds => _activeCompanionIds.AsReadOnly();

    // Unlocked portals
    private readonly HashSet<WorldId> _unlockedPortals = [];
    public IReadOnlyCollection<WorldId> UnlockedPortals => _unlockedPortals;

    // Domain events
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    private Player() { }

    public static Player Create(string name, int craftingSeed)
    {
        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = name,
            Level = 1,
            Experience = 0,
            Strength = 10,
            Agility = 10,
            Intellect = 10,
            Fortitude = 10,
            Speed = 10,
            CraftingSkill = 1,
            SalvageSkill = 1,
            MaxHp = 100,
            CurrentHp = 100,
            MaxActionPoints = 10,
            ActionPoints = 10,
            CraftingSeed = craftingSeed,
            WyrdTangle = 0,
            HasWyrdThread = false,
            ElementRevealed = false,
            PolarityRevealed = false
        };

        player.Weave = Weave.Create(100);

        foreach (FactionId factionId in Enum.GetValues<FactionId>())
            player._reputations.Add(PlayerFactionReputation.Create(player.Id, factionId));

        return player;
    }

    public void RevealMagicAffinity(MagicElement element, MagicPolarity polarity)
    {
        if (ElementRevealed) return;

        PrimaryElement = element;
        Polarity = polarity;
        ElementRevealed = true;
        PolarityRevealed = true;

        _domainEvents.Add(new MagicAffinityRevealedEvent(Id, element, polarity));
    }

    public void SpendWeave(int amount)
    {
        Weave = Weave.Spend(amount);
        if (Weave.IsCritical)
            _domainEvents.Add(new WeaveCriticalEvent(Id));
    }

    public void RestoreWeave(int amount) => Weave = Weave.Restore(amount);

    public void GainExperience(int amount)
    {
        Experience += amount;
        var newLevel = CalculateLevel(Experience);
        if (newLevel > Level)
        {
            Level = newLevel;
            ApplyLevelUp();
            _domainEvents.Add(new PlayerLeveledUpEvent(Id, Level));
        }
    }

    public void AdjustReputation(FactionId factionId, int delta)
    {
        var rep = _reputations.FirstOrDefault(r => r.FactionId == factionId);
        if (rep is null) return;

        var previousTier = rep.Score.Tier;
        rep.Adjust(delta);

        if (rep.Score.Tier != previousTier)
            _domainEvents.Add(new FactionReputationTierChangedEvent(Id, factionId, previousTier, rep.Score.Tier));

        ApplyFactionTensions(factionId, delta);
    }

    public void Move(Position newPosition) => Position = newPosition;

    public void UnlockPortal(WorldId worldId)
    {
        if (_unlockedPortals.Add(worldId))
            _domainEvents.Add(new PortalUnlockedEvent(Id, worldId));
    }

    public void AccumulateWyrdTangle(int amount)
    {
        WyrdTangle += amount;
        if (WyrdTangle >= 100)
            _domainEvents.Add(new WyrdTangleHighEvent(Id, WyrdTangle));
    }

    public void ResolveWyrdTangle(int amount) =>
        WyrdTangle = Math.Max(0, WyrdTangle - amount);

    public bool TryAddActiveCompanion(Guid companionId)
    {
        if (_activeCompanionIds.Count >= 3) return false;
        if (_activeCompanionIds.Contains(companionId)) return false;
        _activeCompanionIds.Add(companionId);
        return true;
    }

    public void RemoveActiveCompanion(Guid companionId) =>
        _activeCompanionIds.Remove(companionId);

    public ReputationTier GetReputationTier(FactionId factionId) =>
        _reputations.FirstOrDefault(r => r.FactionId == factionId)?.Score.Tier ?? ReputationTier.Unknown;

    private void ApplyLevelUp()
    {
        MaxHp += 10;
        CurrentHp = MaxHp;
        Weave = Weave.ExpandMaximum(5).RestoreFull();
        Intellect += 1;
        Fortitude += 1;
    }

    private void ApplyFactionTensions(FactionId changedFaction, int delta)
    {
        var tensionPairs = new Dictionary<(FactionId, FactionId), float>
        {
            [(FactionId.ThornwoodCovens, FactionId.HouseCaervorn)] = -0.5f,
            [(FactionId.HouseCaervorn, FactionId.ThornwoodCovens)] = -0.5f,
            [(FactionId.Golvari, FactionId.Gravenguard)] = -0.4f,
            [(FactionId.Gravenguard, FactionId.Golvari)] = -0.4f,
            [(FactionId.Fairgean, FactionId.EmeraldCompact)] = -0.2f,
            [(FactionId.EmeraldCompact, FactionId.Fairgean)] = -0.2f,
        };

        foreach (var ((faction, affected), multiplier) in tensionPairs)
        {
            if (faction != changedFaction) continue;
            var tensionDelta = (int)(delta * multiplier);
            if (tensionDelta == 0) continue;
            _reputations.FirstOrDefault(r => r.FactionId == affected)?.Adjust(tensionDelta);
        }
    }

    private static int CalculateLevel(int experience) =>
        (int)(1 + Math.Sqrt(experience / 100.0));
}
