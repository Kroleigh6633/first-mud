using System.Text.Json;
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

    // Equipment slot — which slot this item occupies when equipped (None for non-equipment)
    public EquipmentSlot Slot { get; private set; }

    // Lock — prevents the item from being auto-salvaged or bulk-salvaged
    public bool IsLocked { get; private set; }

    // Stacking — Components and Reagents stack; equipment items do not
    public int Quantity { get; private set; } = 1;
    public bool IsStackable => Category == ItemCategory.Component || Category == ItemCategory.Reagent;

    // Imbuing — applied imbues persisted as a JSON string column; exposed via List<AppliedImbue>
    // EF maps this property directly; the Imbues accessor deserializes on demand.
    public string ImbuesJson { get; private set; } = "[]";

    private List<AppliedImbue>? _imbuesCache;
    private List<AppliedImbue> ImbuesList
    {
        get
        {
            if (_imbuesCache is null)
                _imbuesCache = JsonSerializer.Deserialize<List<AppliedImbue>>(ImbuesJson) ?? [];
            return _imbuesCache;
        }
    }

    public IReadOnlyList<AppliedImbue> Imbues => ImbuesList.AsReadOnly();

    /// <summary>
    /// Computed display name incorporating any applied imbue modifiers.
    /// Prefixes (Fire, Water, Earth, Air, Protective, Wyrd) appear before the base name;
    /// suffixes (Fortifying, Restoration) appear after.
    /// The stored <see cref="Name"/> column is never modified.
    /// </summary>
    public string DisplayName
    {
        get
        {
            if (ImbuesList.Count == 0) return Name;

            var prefixes = new List<string>();
            var suffixes = new List<string>();

            foreach (var imbue in ImbuesList)
            {
                switch (imbue.Type)
                {
                    case ImbueType.Fire:        prefixes.Add("Blazing");      break;
                    case ImbueType.Water:       prefixes.Add("Tidal");        break;
                    case ImbueType.Earth:       prefixes.Add("Earthen");      break;
                    case ImbueType.Air:         prefixes.Add("Windswept");    break;
                    case ImbueType.Protective:  prefixes.Add("Warded");       break;
                    case ImbueType.Fortifying:  suffixes.Add("of Might");     break;
                    case ImbueType.Wyrd:        prefixes.Add("Fate-Touched"); break;
                    case ImbueType.Restoration: suffixes.Add("of Mending");   break;
                }
            }

            var result = Name;
            if (prefixes.Count > 0) result = string.Join(" ", prefixes) + " " + result;
            if (suffixes.Count > 0) result = result + " " + string.Join(" ", suffixes);
            return result;
        }
    }

    private void FlushImbues() =>
        ImbuesJson = JsonSerializer.Serialize(ImbuesList);

    public bool IsUnstable { get; private set; }

    /// <summary>Maximum imbue slots based on Workmanship tier.</summary>
    public int MaxImbueSlots => Workmanship.Value switch
    {
        <= 2 => 1,
        <= 4 => 2,
        <= 6 => 3,
        <= 8 => 4,
        _    => 5
    };

    /// <summary>True when more imbues have been applied than the item's MaxImbueSlots.</summary>
    public bool IsOverimbued => ImbuesList.Count > MaxImbueSlots;

    private Item() { }

    public static Item Create(
        string name,
        string description,
        ItemCategory category,
        Workmanship workmanship,
        WorldId originWorld,
        bool isArdweldOrigin = false,
        EquipmentSlot slot = EquipmentSlot.None)
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
            Durability = maxDurability,
            Slot = slot
        };
    }

    /// <summary>Sets the equipment slot directly (used during data migration/seeding).</summary>
    public void SetSlot(EquipmentSlot slot) => Slot = slot;

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

    /// <summary>
    /// Renames the item (used for legacy migration of obsolete material names).
    /// </summary>
    public void Rename(string newName) => Name = newName;

    public void SetOwner(Guid? ownerId) => OwnerId = ownerId;

    public void Degrade(int amount)
    {
        Durability = Math.Max(0, Durability - amount);
        if (Durability == 0) IsSalvageable = false;
    }

    public bool IsBroken => Durability == 0;

    /// <summary>
    /// Fortifying imbue: raise Workmanship by <paramref name="amount"/> points (capped at 10).
    /// </summary>
    public void BoostWorkmanship(int amount)
    {
        var newValue = Math.Clamp(Workmanship.Value + amount, 1, 10);
        Workmanship = ValueObjects.Workmanship.Of(newValue);
    }

    /// <summary>
    /// Applies an imbue to this item.
    /// Marks IsWyrdTouched = true and sets IsUnstable if overimbued after this application.
    /// </summary>
    public void ApplyImbue(ImbueType type, float power)
    {
        ImbuesList.Add(new AppliedImbue(type, power));
        FlushImbues();
        IsWyrdTouched = true;
        if (IsOverimbued)
            IsUnstable = true;
    }

    /// <summary>Removes a random imbue — used on catastrophic failure / instability decay.</summary>
    public void RemoveRandomImbue()
    {
        if (ImbuesList.Count == 0) return;
        var index = Random.Shared.Next(ImbuesList.Count);
        ImbuesList.RemoveAt(index);
        FlushImbues();
        // Re-evaluate stability: no longer overimbued → stable again
        if (!IsOverimbued)
            IsUnstable = false;
    }

    /// <summary>Reduces Workmanship by 1 (min 1) — used on catastrophic imbue failure.</summary>
    public void DegradeWorkmanship()
    {
        var newValue = Math.Max(1, Workmanship.Value - 1);
        Workmanship = ValueObjects.Workmanship.Of(newValue);
    }
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
