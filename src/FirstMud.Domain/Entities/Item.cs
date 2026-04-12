using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.Domain.Entities;

public class Item
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public ItemCategory Category { get; private set; }
    public Workmanship Workmanship { get; private set; } = ValueObjects.Workmanship.Of(1);
    public MagicElement? MagicalElement { get; private set; }
    public MagicPolarity? MagicalPolarity { get; private set; }
    public TaperType? AppliedTaper { get; private set; }
    public TaperQuality? TaperQuality { get; private set; }
    public bool IsWyrdTouched { get; private set; }
    public bool IsArdweldOrigin { get; private set; }
    public int Durability { get; private set; }
    public int MaxDurability { get; private set; }
    public bool IsSalvageable { get; private set; }
    public WorldId OriginWorld { get; private set; }

    // Discovery tracking — first player to craft this variant
    public string? DiscoveredByPlayerName { get; private set; }

    // Ownership — null means world loot / homestead storage
    public Guid? OwnerId { get; private set; }

    // Lock — prevents the item from being auto-salvaged or bulk-salvaged
    public bool IsLocked { get; private set; }

    // Stacking — Components and Reagents stack; equipment items do not
    public int Quantity { get; private set; } = 1;
    public bool IsStackable => Category == ItemCategory.Component || Category == ItemCategory.Reagent;

    private Item() { }

    public static Item Create(
        string name,
        string description,
        ItemCategory category,
        Workmanship workmanship,
        WorldId originWorld,
        bool isArdweldOrigin = false)
    {
        var maxDurability = workmanship.Value * 10;
        return new Item
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            Category = category,
            Workmanship = workmanship,
            OriginWorld = originWorld,
            IsArdweldOrigin = isArdweldOrigin,
            IsSalvageable = true,
            MaxDurability = maxDurability,
            Durability = maxDurability
        };
    }

    public void ApplyTaperImbue(TaperType taperType, TaperQuality quality, MagicElement element, MagicPolarity polarity)
    {
        AppliedTaper = taperType;
        TaperQuality = quality;
        MagicalElement = element;
        MagicalPolarity = polarity;
        if (taperType == TaperType.Wyrd) IsWyrdTouched = true;
    }

    public void AddQuantity(int amount)
    {
        if (amount <= 0) return;
        Quantity += amount;
    }

    /// <summary>
    /// Attempts to remove <paramref name="amount"/> units from this stack.
    /// Returns true if successful; <paramref name="remaining"/> is the leftover quantity (≥0).
    /// Returns false if the stack does not have enough units.
    /// </summary>
    public bool TryRemoveQuantity(int amount, out int remaining)
    {
        remaining = 0;
        if (amount <= 0 || amount > Quantity) return false;
        Quantity -= amount;
        remaining = Quantity;
        return true;
    }

    public void ToggleLock() => IsLocked = !IsLocked;

    public void MarkDiscoveredBy(string playerName) => DiscoveredByPlayerName = playerName;

    public void SetOwner(Guid? ownerId) => OwnerId = ownerId;

    public void Degrade(int amount)
    {
        Durability = Math.Max(0, Durability - amount);
        if (Durability == 0) IsSalvageable = false;
    }

    public bool IsBroken => Durability == 0;
}

public enum ItemCategory
{
    Weapon,
    Armor,
    Accessory,
    Reagent,
    Component,
    Consumable,
    QuestItem,
    Artifact,
    AutomationPart
}
