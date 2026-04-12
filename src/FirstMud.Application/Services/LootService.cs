using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public record LootDropResult(bool Dropped, Item? Item, string Message, bool AutoSalvaged = false);

public class LootService
{
    private readonly IItemRepository _itemRepository;
    private readonly SalvageService _salvageService;
    private readonly ILogger<LootService> _logger;

    // -------------------------------------------------------------------------
    // Static loot templates — all 9 equipment slots + materials
    // -------------------------------------------------------------------------
    private static readonly LootTemplate[] Templates =
    [
        // Melee Weapons
        new("Iron Dagger",          "A short blade honed from iron. Reliable in close quarters.",                ItemCategory.Weapon,    EquipmentSlot.MeleeWeapon,  MinWork: 1, MaxWork: 3),
        new("Iron Sword",           "A straight iron sword, workhorse of the Aeldran roads.",                    ItemCategory.Weapon,    EquipmentSlot.MeleeWeapon,  MinWork: 2, MaxWork: 5),
        new("Stone Axe",            "A heavy axe with a hand-knapped stone head. Brutal and primitive.",          ItemCategory.Weapon,    EquipmentSlot.MeleeWeapon,  MinWork: 1, MaxWork: 3),
        new("Battle Hammer",        "A two-handed maul fitted with an iron-capped head. Bone-breaking.",          ItemCategory.Weapon,    EquipmentSlot.MeleeWeapon,  MinWork: 3, MaxWork: 6),
        new("War Pick",             "A compact pick with a hardened iron point. Punches through plate.",          ItemCategory.Weapon,    EquipmentSlot.MeleeWeapon,  MinWork: 2, MaxWork: 5),

        // Ranged Weapons
        new("Thornwood Bow",        "A recurve bow carved from resilient thornwood.",                             ItemCategory.Weapon,    EquipmentSlot.RangedWeapon, MinWork: 2, MaxWork: 4),
        new("Hunter's Crossbow",    "A compact crossbow favored by Aeldran forest scouts.",                       ItemCategory.Weapon,    EquipmentSlot.RangedWeapon, MinWork: 3, MaxWork: 6),
        new("Sling",                "A simple sling of woven sinew. Cheap but accurate.",                         ItemCategory.Weapon,    EquipmentSlot.RangedWeapon, MinWork: 1, MaxWork: 2),

        // Focus (magic weapons)
        new("Oak Wand",             "A slender oak wand that channels Weave with little resistance.",             ItemCategory.Weapon,    EquipmentSlot.Focus,        MinWork: 1, MaxWork: 3),
        new("Crystal Focus",        "A multifaceted crystal sphere that amplifies magical resonance.",             ItemCategory.Weapon,    EquipmentSlot.Focus,        MinWork: 3, MaxWork: 6),
        new("Ashwood Staff",        "A tall ashwood staff worn smooth by long use. Reliable conduit.",            ItemCategory.Weapon,    EquipmentSlot.Focus,        MinWork: 2, MaxWork: 5),

        // Head
        new("Leather Cap",          "Cured hide shaped into a simple helm.",                                     ItemCategory.Armor,     EquipmentSlot.Head,         MinWork: 1, MaxWork: 3),
        new("Scale Coif",           "Overlapping iron scales form a tight hood.",                                ItemCategory.Armor,     EquipmentSlot.Head,         MinWork: 3, MaxWork: 6),
        new("Iron Helm",            "A forged iron helm with a narrow visor. Heavy but protective.",              ItemCategory.Armor,     EquipmentSlot.Head,         MinWork: 4, MaxWork: 7),
        new("Fur Hood",             "A thick fur hood sewn from heavy pelts. Warm and surprisingly resilient.",   ItemCategory.Armor,     EquipmentSlot.Head,         MinWork: 1, MaxWork: 3),

        // Chest
        new("Leather Vest",         "A padded leather body vest offering moderate protection.",                   ItemCategory.Armor,     EquipmentSlot.Chest,        MinWork: 2, MaxWork: 4),
        new("Chain Shirt",          "Interlocked iron rings form a flexible hauberk.",                            ItemCategory.Armor,     EquipmentSlot.Chest,        MinWork: 3, MaxWork: 6),
        new("Padded Gambeson",      "Layers of quilted cloth that absorb shock surprisingly well.",                ItemCategory.Armor,     EquipmentSlot.Chest,        MinWork: 1, MaxWork: 3),

        // Legs
        new("Leather Leggings",     "Thick leather greaves strapped over the thighs and shins.",                  ItemCategory.Armor,     EquipmentSlot.Legs,         MinWork: 1, MaxWork: 3),
        new("Iron Greaves",         "Solid iron plates that guard the legs from knee to ankle.",                   ItemCategory.Armor,     EquipmentSlot.Legs,         MinWork: 3, MaxWork: 6),
        new("Padded Trousers",      "Quilted trousers reinforced at vulnerable joints.",                           ItemCategory.Armor,     EquipmentSlot.Legs,         MinWork: 1, MaxWork: 2),

        // Hands
        new("Leather Gloves",       "Supple leather gauntlets offering grip and minor protection.",                ItemCategory.Armor,     EquipmentSlot.Hands,        MinWork: 1, MaxWork: 3),
        new("Iron Vambraces",       "Hinged iron arm guards strapped over the forearms.",                         ItemCategory.Armor,     EquipmentSlot.Hands,        MinWork: 3, MaxWork: 6),
        new("Wrapped Handguards",   "Strips of heavy cloth wound tight around the knuckles and wrists.",          ItemCategory.Armor,     EquipmentSlot.Hands,        MinWork: 1, MaxWork: 2),

        // Feet
        new("Leather Boots",        "Sturdy boots of thick-tanned hide. Dependable on rough trails.",             ItemCategory.Armor,     EquipmentSlot.Feet,         MinWork: 1, MaxWork: 3),
        new("Iron Sabatons",        "Articulated iron foot armor. Loud on stone, solid everywhere.",               ItemCategory.Armor,     EquipmentSlot.Feet,         MinWork: 3, MaxWork: 6),
        new("Traveler's Sandals",   "Light sandals of braided leather. Fast and quiet.",                           ItemCategory.Armor,     EquipmentSlot.Feet,         MinWork: 1, MaxWork: 2),

        // Accessories
        new("Bone Ring",            "A ring carved from yellowed bone. Carries a faint Wyrd resonance.",          ItemCategory.Accessory, EquipmentSlot.Accessory,    MinWork: 1, MaxWork: 3),
        new("Silver Amulet",        "A small silver disc on a chain, engraved with a warding sigil.",             ItemCategory.Accessory, EquipmentSlot.Accessory,    MinWork: 2, MaxWork: 5),
        new("Wyrd Charm",           "A knotted cord strung with crystalline fragments. Unsettling to hold.",      ItemCategory.Accessory, EquipmentSlot.Accessory,    MinWork: 3, MaxWork: 7),

        // Materials / Reagents (no slot — go straight to inventory)
        new("Iron Ore",             "Rough lumps of iron ore, ready for the smelter.",                           ItemCategory.Component, EquipmentSlot.None,         MinWork: 1, MaxWork: 2),
        new("Beast Hide",           "Thick hide stripped from a slain creature.",                                ItemCategory.Component, EquipmentSlot.None,         MinWork: 1, MaxWork: 2),
        new("Sinew",                "Dried sinew — useful in bowstrings and bindings.",                          ItemCategory.Component, EquipmentSlot.None,         MinWork: 1, MaxWork: 2),
        new("Thornwood Herb",       "A bitter medicinal herb found only in the Thornwood.",                      ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 1, MaxWork: 3),
        new("Dravenite Dust",       "Fine crystalline powder with latent magical resonance. Used in restoration imbuing.", ItemCategory.Reagent, EquipmentSlot.None, MinWork: 2, MaxWork: 4),
        new("Bone Fragment",        "A large bone fragment — useful as a crafting material.",                    ItemCategory.Component, EquipmentSlot.None,         MinWork: 1, MaxWork: 2),
        new("Wyrd Shard",           "A jagged shard of crystallised Wyrd-energy. Handle with care.",             ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 3, MaxWork: 7),

    ];

    // Taper templates — rolled separately at ~15% chance after the main loot roll
    private static readonly LootTemplate[] TaperTemplates =
    [
        new("Fire Shaping Taper",   "A taper that burns with a constant crimson flame. Used to imbue fire resonance.", ItemCategory.Reagent, EquipmentSlot.None, MinWork: 1, MaxWork: 3),
        new("Water Shaping Taper",  "A cool, blue-green taper that hums with tidal energy.",                     ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 1, MaxWork: 3),
        new("Earth Shaping Taper",  "A heavy amber taper infused with stone and root essence.",                  ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 1, MaxWork: 3),
        new("Air Shaping Taper",    "A nearly weightless taper that drifts if not held firm.",                   ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 1, MaxWork: 3),
        new("Fortitude Taper",      "A dense, dark taper that reinforces whatever it imbues.",                   ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 2, MaxWork: 4),
        new("Warding Taper",        "A pale silver taper woven with a protective sigil.",                        ItemCategory.Reagent,   EquipmentSlot.None,         MinWork: 2, MaxWork: 4),
    ];

    public LootService(IItemRepository itemRepository, SalvageService salvageService, ILogger<LootService> logger)
    {
        _itemRepository = itemRepository;
        _salvageService = salvageService;
        _logger = logger;
    }

    /// <summary>
    /// Rolls for a loot drop after a combat victory.
    /// dangerLevel: 1-10 zone danger.
    /// ownerId: the player's ID — item is placed in their inventory (OwnerId set).
    /// currentInventoryCount: used by caller to enforce carry capacity.
    /// Returns LootDropResult with Dropped=false if no drop or inventory full.
    /// </summary>
    public async Task<LootDropResult> RollLootDropAsync(
        int dangerLevel,
        Guid ownerId,
        WorldId originWorld,
        int currentInventoryCount,
        int maxInventorySlots,
        CancellationToken ct = default,
        Player? player = null)
    {
        // Inventory full check
        if (currentInventoryCount >= maxInventorySlots)
            return new LootDropResult(false, null, "Your inventory is full!");

        // Drop chance: 40% at danger 1, up to 80% at danger 10
        var dropChance = 40 + dangerLevel * 4;

        // Separate taper drop: 15% flat chance (independent of main drop)
        if (Random.Shared.Next(100) < 15)
        {
            var taperTemplate = TaperTemplates[Random.Shared.Next(TaperTemplates.Length)];
            var taperWork = Workmanship.Of(Math.Clamp(
                taperTemplate.MinWork + Random.Shared.Next(taperTemplate.MaxWork - taperTemplate.MinWork + 1),
                1, 10));
            var taperItem = Item.Create(taperTemplate.Name, taperTemplate.Description,
                taperTemplate.Category, taperWork, originWorld, slot: taperTemplate.Slot);
            taperItem.SetOwner(ownerId);

            var existingTaper = await _itemRepository.GetByOwnerAndNameAsync(ownerId, taperTemplate.Name, taperTemplate.Category, ct);
            if (existingTaper is not null)
            {
                existingTaper.AddQuantity(1);
                await _itemRepository.UpdateAsync(existingTaper, ct);
                _logger.LogInformation("Taper stack merge for player {PlayerId}: {Name}", ownerId, taperTemplate.Name);
            }
            else
            {
                await _itemRepository.AddAsync(taperItem, ct);
                _logger.LogInformation("Taper drop for player {PlayerId}: {Name}", ownerId, taperTemplate.Name);
            }
        }

        if (Random.Shared.Next(100) >= dropChance)
            return new LootDropResult(false, null, string.Empty);

        // Select a template — higher danger skews toward later (better) templates
        var maxTemplateIndex = Math.Min(Templates.Length - 1, 4 + dangerLevel);
        var template = Templates[Random.Shared.Next(0, maxTemplateIndex + 1)];

        // Workmanship: template range + danger bonus
        var workValue = template.MinWork + (int)(Random.Shared.NextDouble() * (template.MaxWork - template.MinWork + 1));
        workValue = Math.Clamp(workValue, 1, 10);
        var workmanship = Workmanship.Of(workValue);

        var item = Item.Create(template.Name, template.Description, template.Category, workmanship, originWorld,
            slot: template.Slot);
        item.SetOwner(ownerId);

        // Auto-salvage check: if the player has a threshold set and this item qualifies, salvage immediately
        if (player is not null)
        {
            var autoSalvageMessage = await _salvageService.TryAutoSalvageAsync(player, item, ct);
            if (autoSalvageMessage is not null)
            {
                _logger.LogInformation("Auto-salvaged loot for player {PlayerId}: {ItemName} W{Workmanship}",
                    ownerId, item.Name, workValue);
                return new LootDropResult(true, item, autoSalvageMessage, AutoSalvaged: true);
            }
        }

        // For stackable categories (Component/Reagent), merge into an existing stack if one exists
        if (item.IsStackable)
        {
            var existingStack = await _itemRepository.GetByOwnerAndNameAsync(ownerId, template.Name, template.Category, ct);
            if (existingStack is not null)
            {
                existingStack.AddQuantity(1);
                await _itemRepository.UpdateAsync(existingStack, ct);
                var stackMessage = $"You found: {existingStack.Name} (now x{existingStack.Quantity}) [Workmanship {workValue}]!";
                _logger.LogInformation("Loot stack merge for player {PlayerId}: {ItemName}", ownerId, template.Name);
                return new LootDropResult(true, existingStack, stackMessage);
            }
        }

        await _itemRepository.AddAsync(item, ct);

        var message = $"You found: {item.Name} [Workmanship {workValue}]!";
        _logger.LogInformation("Loot drop for player {PlayerId}: {ItemName} W{Workmanship}", ownerId, item.Name, workValue);

        return new LootDropResult(true, item, message);
    }

    private sealed record LootTemplate(
        string Name,
        string Description,
        ItemCategory Category,
        EquipmentSlot Slot,
        int MinWork,
        int MaxWork);
}
