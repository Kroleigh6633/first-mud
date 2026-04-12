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
    public int AutoSalvageWeaponThreshold { get; private set; }
    public int AutoSalvageArmorThreshold { get; private set; }

    // Position
    public Position Position { get; private set; } = new(WorldId.Aeldran, 1, 0, 0);

    // Portal home — saved position to return to after visiting homestead
    public Position? SavedReturnPosition { get; private set; }

    // Carry capacity
    private int _maxInventorySlots = 20;
    public int MaxInventorySlots => _maxInventorySlots;

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

    // Equipment — 9-slot dictionary backed by a JSON column
    // EF Core maps this directly; callers should treat it as read-only via GetEquipped/Equip/Unequip.
    public Dictionary<EquipmentSlot, Guid> EquippedItems { get; private set; } = [];

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

    /// <summary>
    /// Returns the starting stats (Str, Agi, Int, Fort, Spd, MaxHp) for a given element archetype.
    /// Total attribute points are ~55 for every element.
    /// </summary>
    public static (int Str, int Agi, int Int, int Fort, int Spd, int MaxHp) GetArchetypeStats(MagicElement element) =>
        element switch
        {
            MagicElement.Fire   => (14, 10,  8, 12, 11, 100),
            MagicElement.Water  => ( 8, 12, 14, 10, 11,  90),
            MagicElement.Earth  => (12,  8, 10, 14,  6, 120),
            MagicElement.Air    => ( 8, 14, 12,  8, 13,  85),
            MagicElement.Aether => (10, 10, 13, 10, 12,  95),
            _                   => (10, 10, 10, 10, 10, 100),
        };

    public static Player Create(string name, int craftingSeed)
    {
        // Randomly assign a primary element so every new character has a distinct archetype.
        var elements = Enum.GetValues<MagicElement>();
        var element = elements[Random.Shared.Next(elements.Length)];
        var (str, agi, intel, fort, spd, maxHp) = GetArchetypeStats(element);

        var player = new Player
        {
            Id = Guid.NewGuid(),
            Name = name,
            Level = 1,
            Experience = 0,
            PrimaryElement = element,
            Strength = str,
            Agility = agi,
            Intellect = intel,
            Fortitude = fort,
            Speed = spd,
            CraftingSkill = 1,
            SalvageSkill = 1,
            MaxHp = maxHp,
            CurrentHp = maxHp,
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

        // Re-apply archetype stats if the revealed element differs from the creation assignment.
        if (element != PrimaryElement)
        {
            var (str, agi, intel, fort, spd, maxHp) = GetArchetypeStats(element);
            Strength = str;
            Agility = agi;
            Intellect = intel;
            Fortitude = fort;
            Speed = spd;
            MaxHp = maxHp;
            CurrentHp = maxHp;
        }

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

    /// <summary>Checks whether the player can carry one more item.</summary>
    public bool CanCarryMore(int currentItemCount) => currentItemCount < _maxInventorySlots;

    /// <summary>Saves current position and teleports player to the homestead world position.</summary>
    public void PortalHome(Position homesteadPosition)
    {
        SavedReturnPosition = Position;
        Position = homesteadPosition;
    }

    /// <summary>Restores saved return position. Clears SavedReturnPosition.</summary>
    public void PortalBack()
    {
        if (SavedReturnPosition is null) return;
        Position = SavedReturnPosition;
        SavedReturnPosition = null;
    }

    /// <summary>Heals the player by the given amount, capped at MaxHp.</summary>
    public void HealHp(int amount)
    {
        CurrentHp = Math.Min(MaxHp, CurrentHp + amount);
    }

    /// <summary>
    /// Sets CurrentHp directly (e.g. to sync back the result of a combat encounter).
    /// Value is clamped to [1, MaxHp] — use 1 as minimum so the player is never stored at 0 HP.
    /// </summary>
    public void SetCurrentHp(int hp)
    {
        CurrentHp = Math.Clamp(hp, 1, MaxHp);
    }

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

    /// <summary>
    /// Returns the item ID currently equipped in the given slot, or null if empty.
    /// </summary>
    public Guid? GetEquipped(EquipmentSlot slot) =>
        EquippedItems.TryGetValue(slot, out var id) ? id : null;

    /// <summary>
    /// Equips an item into its designated slot.
    /// Returns the previously-equipped item id in that slot (for swap), or null if the slot was empty.
    /// Items with Slot == None fall back to Accessory slot.
    /// </summary>
    public Guid? Equip(Item item)
    {
        var slot = item.Slot == EquipmentSlot.None ? EquipmentSlot.Accessory : item.Slot;
        return Equip(slot, item.Id);
    }

    /// <summary>
    /// Equips an item by slot and id directly.
    /// Returns the previous item id in that slot, or null if it was empty.
    /// </summary>
    public Guid? Equip(EquipmentSlot slot, Guid itemId)
    {
        EquippedItems.TryGetValue(slot, out var previous);
        EquippedItems[slot] = itemId;
        return previous == Guid.Empty ? null : previous;
    }

    /// <summary>
    /// Unequips the item in the given slot. Returns the unequipped item id, or null if slot was empty.
    /// </summary>
    public Guid? Unequip(EquipmentSlot slot)
    {
        if (!EquippedItems.TryGetValue(slot, out var removed))
            return null;
        EquippedItems.Remove(slot);
        return removed;
    }

    /// <summary>
    /// Reassigns starting stats based on the player's current PrimaryElement.
    /// Used by the seeder to fix players created before the element-based stat system.
    /// </summary>
    public void ReassignArchetypeStats()
    {
        var (str, agi, intel, fort, spd, maxHp) = GetArchetypeStats(PrimaryElement == default ? MagicElement.Aether : PrimaryElement);
        Strength = str;
        Agility = agi;
        Intellect = intel;
        Fortitude = fort;
        Speed = spd;
        MaxHp = maxHp;
        CurrentHp = Math.Min(CurrentHp, maxHp);
    }

    public void GainSalvageSkillXp(int amount)
    {
        SalvageSkill = Math.Max(1, SalvageSkill + amount);
    }

    public void GainCraftingSkillXp(int amount)
    {
        CraftingSkill = Math.Max(1, CraftingSkill + amount);
    }

    /// <summary>
    /// Sets the auto-salvage threshold for the given category (weapon or armor).
    /// A maxWorkmanship of 0 disables auto-salvage for that category.
    /// </summary>
    public void SetAutoSalvageThreshold(string category, int maxWorkmanship)
    {
        var clamped = Math.Clamp(maxWorkmanship, 0, 10);
        if (string.Equals(category, "weapon", StringComparison.OrdinalIgnoreCase))
            AutoSalvageWeaponThreshold = clamped;
        else if (string.Equals(category, "armor", StringComparison.OrdinalIgnoreCase))
            AutoSalvageArmorThreshold = clamped;
    }

    public ReputationTier GetReputationTier(FactionId factionId) =>
        _reputations.FirstOrDefault(r => r.FactionId == factionId)?.Score.Tier ?? ReputationTier.Unknown;

    /// <summary>
    /// Returns the per-level stat gains (Str, Agi, Int, Fort, Spd, MaxHp) for a given element archetype.
    /// Gains are weighted to reinforce each archetype's strengths.
    /// </summary>
    public static (int Str, int Agi, int Int, int Fort, int Spd, int MaxHp) GetArchetypeLevelGains(MagicElement element) =>
        element switch
        {
            MagicElement.Fire   => (2, 1, 1, 2, 1, 12),
            MagicElement.Water  => (1, 1, 2, 1, 1,  8),
            MagicElement.Earth  => (2, 1, 1, 2, 0, 15),
            MagicElement.Air    => (1, 2, 1, 1, 2,  7),
            MagicElement.Aether => (1, 1, 2, 1, 1, 10),
            _                   => (1, 1, 1, 1, 1, 10),
        };

    private void ApplyLevelUp()
    {
        var (strGain, agiGain, intGain, fortGain, spdGain, hpGain) =
            GetArchetypeLevelGains(PrimaryElement == default ? MagicElement.Aether : PrimaryElement);

        MaxHp += hpGain;
        CurrentHp = MaxHp;
        Weave = Weave.ExpandMaximum(5).RestoreFull();
        Strength += strGain;
        Agility += agiGain;
        Intellect += intGain;
        Fortitude += fortGain;
        Speed += spdGain;
        MaxActionPoints += 2;
        ActionPoints = MaxActionPoints;
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
