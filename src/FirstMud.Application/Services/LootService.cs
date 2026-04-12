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

        // Consumables — healing
        new("Minor Healing Draught", "A small vial of copper-coloured tonic. Restores 30 HP when consumed.",      ItemCategory.Consumable, EquipmentSlot.None,        MinWork: 1, MaxWork: 2),
        new("Healing Potion",        "A corked flask of luminous green liquid. Restores 60 HP when consumed.",    ItemCategory.Consumable, EquipmentSlot.None,        MinWork: 2, MaxWork: 3),
        new("Greater Healing Elixir","A heavy bottle of deep-crimson elixir. Restores 100 HP when consumed.",    ItemCategory.Consumable, EquipmentSlot.None,        MinWork: 3, MaxWork: 5),

        // Consumables — weave
        new("Weave Tincture",        "A small vial of shimmering blue tincture. Restores 20 Weave when consumed.", ItemCategory.Consumable, EquipmentSlot.None,       MinWork: 1, MaxWork: 2),
        new("Weave Elixir",          "A flask of swirling violet liquid. Restores 50 Weave when consumed.",       ItemCategory.Consumable, EquipmentSlot.None,        MinWork: 2, MaxWork: 4),

        // Consumables — buffs
        new("Fortitude Brew",        "A dark amber brew that hardens the body. +10% max HP for next combat.",     ItemCategory.Consumable, EquipmentSlot.None,        MinWork: 2, MaxWork: 3),
        new("Speed Draught",         "A clear, fizzing draught that quickens the limbs. +20% speed for next combat.", ItemCategory.Consumable, EquipmentSlot.None,    MinWork: 2, MaxWork: 3),
        new("Strength Tonic",        "A thick red tonic with a sharp bite. +15% strike damage for next combat.",  ItemCategory.Consumable, EquipmentSlot.None,        MinWork: 2, MaxWork: 3),
    ];

    // -------------------------------------------------------------------------
    // Common materials — dropped in every biome
    // -------------------------------------------------------------------------
    private static readonly LootTemplate[] CommonMaterials =
    [
        new("Iron Ore",             "Rough lumps of iron ore, ready for the smelter.",                           ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Stone",                "A chunk of rough stone. Basic building and crafting material.",              ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Wood",                 "A length of raw timber, cut and dried for crafting.",                       ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Leather",              "Tanned hide suitable for armor and bindings.",                               ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Sinew",                "Dried sinew — useful in bowstrings and bindings.",                          ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Bone Fragment",        "A large bone fragment — useful as a crafting material.",                    ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
    ];

    // -------------------------------------------------------------------------
    // Biome-specific material pools
    // -------------------------------------------------------------------------
    private static readonly Dictionary<string, LootTemplate[]> BiomeMaterialTemplates = new()
    {
        ["mountain"] =
        [
            new("Mithril Ore",          "A rare, lightweight ore with a silver-blue sheen. Prized by armorers.",     ItemCategory.Component, EquipmentSlot.None, MinWork: 4, MaxWork: 7),
            new("Diamond Shard",        "A faceted shard of raw diamond. Used in advanced enchanting.",               ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 5, MaxWork: 8),
            new("Mountain Herb",        "A hardy alpine herb with potent healing properties.",                        ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 2, MaxWork: 4),
            new("Granite Block",        "A heavy block of dense grey granite. Durable construction material.",        ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 3),
            new("Eagle Feather",        "A large primary feather from a mountain eagle. Prized for fletching.",       ItemCategory.Component, EquipmentSlot.None, MinWork: 2, MaxWork: 4),
        ],
        ["forest"] =
        [
            new("Thornwood Heartwood",  "Dense heartwood from a thornwood tree. Superior to common timber.",          ItemCategory.Component, EquipmentSlot.None, MinWork: 3, MaxWork: 5),
            new("Beast Leather",        "Thick, supple leather stripped from a large forest creature.",               ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 3),
            new("Amber Resin",          "Golden tree resin with mild magical adhesive properties.",                   ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 2, MaxWork: 4),
            new("Moonbloom Petal",      "A translucent petal from the night-blooming moonbloom flower. Potent reagent.", ItemCategory.Reagent, EquipmentSlot.None, MinWork: 4, MaxWork: 6),
            new("Spider Silk",          "Fine, strong thread spun by giant forest spiders. Used in light armor.",     ItemCategory.Component, EquipmentSlot.None, MinWork: 2, MaxWork: 4),
        ],
        ["desert"] =
        [
            new("Obsidian Shard",       "A razor-sharp shard of volcanic glass. Holds an edge better than iron.",     ItemCategory.Component, EquipmentSlot.None, MinWork: 3, MaxWork: 5),
            new("Fire Crystal",         "A deep-red crystal radiating concentrated fire magic.",                      ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 4, MaxWork: 7),
            new("Scorched Bone",        "Bone bleached and hardened by desert heat. Still serviceable.",              ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 3),
            new("Cactus Fiber",         "Coarse fiber stripped from desert cactus. Basic but plentiful.",             ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
            new("Ashite Dust",          "Fine grey powder imbued with residual magic from the Ardweld collapse.",     ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 5, MaxWork: 7),
        ],
        ["water"] =
        [
            new("Sea Scale",            "A large iridescent scale from an aquatic creature.",                         ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 3),
            new("Coral Fragment",       "A rough chunk of sea coral. Used in underwater-themed crafting.",             ItemCategory.Component, EquipmentSlot.None, MinWork: 2, MaxWork: 4),
            new("Deep Ink",             "Thick black ink harvested from a deep-sea cephalopod. Used for scrollwork.", ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 2, MaxWork: 4),
            new("Pearl",                "A lustrous pearl from a coastal mollusk. Valuable and magically receptive.", ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 4, MaxWork: 7),
            new("Driftwood",            "Salt-treated wood washed ashore. Rot-resistant and light.",                  ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        ],
        ["swamp"] =
        [
            new("Bog Iron",             "Iron ore smelted from swamp deposits. Crude but plentiful.",                 ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 3),
            new("Toad Venom",           "A vial of milky toxin harvested from a marsh toad. Potent poison reagent.",  ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 2, MaxWork: 4),
            new("Peat Moss",            "Dark, spongy peat harvested from the bog. Used as fuel and insulation.",     ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
            new("Marsh Gas Crystal",    "A fragile crystal formed around a pocket of volatile marsh gas.",             ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 4, MaxWork: 6),
            new("Leech Extract",        "A thick, dark fluid drained from marsh leeches. Prized by healers.",         ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 2, MaxWork: 3),
        ],
        ["plains"] =
        [
            new("Cotton Fiber",         "Soft white fiber from plains cotton plants. Basic cloth material.",          ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
            new("Horse Hair",           "Coarse hair from a plains horse. Used in bowstrings and rope.",              ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
            new("Flint",                "A piece of flint knapped to a sharp edge. Essential for tool making.",       ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
            new("Wheat Sheaf",          "A bundle of harvested wheat. Ingredient in future food crafting.",           ItemCategory.Component, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
            new("Copper Nugget",        "A small nugget of soft copper ore. Used in basic metalworking.",             ItemCategory.Component, EquipmentSlot.None, MinWork: 2, MaxWork: 3),
        ],
        ["wyrd"] =
        [
            new("Wyrd Shard",           "A jagged shard of crystallised fate-energy. Handle with extreme care.",      ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 5, MaxWork: 8),
            new("Void Essence",         "A swirling mote of energy drawn from a tear in reality. Extremely rare.",   ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 6, MaxWork: 9),
            new("Dravenite Dust",       "Fine crystalline powder with latent magical resonance. Used in imbuing.",    ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 4, MaxWork: 7),
            new("Tear Fragment",        "A sliver of broken reality. Hums faintly and distorts nearby shadows.",      ItemCategory.Reagent,   EquipmentSlot.None, MinWork: 5, MaxWork: 7),
            new("Phase Thread",         "A gossamer thread that phases between planes. Used to weave enchantments.",  ItemCategory.Component, EquipmentSlot.None, MinWork: 3, MaxWork: 5),
        ],
    };

    // Consumable templates — rolled separately at ~10% chance after the main loot roll
    private static readonly LootTemplate[] ConsumableTemplates =
    [
        new("Minor Healing Draught", "A small vial of copper-coloured tonic. Restores 30 HP when consumed.",      ItemCategory.Consumable, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Healing Potion",        "A corked flask of luminous green liquid. Restores 60 HP when consumed.",    ItemCategory.Consumable, EquipmentSlot.None, MinWork: 2, MaxWork: 3),
        new("Greater Healing Elixir","A heavy bottle of deep-crimson elixir. Restores 100 HP when consumed.",    ItemCategory.Consumable, EquipmentSlot.None, MinWork: 3, MaxWork: 5),
        new("Weave Tincture",        "A small vial of shimmering blue tincture. Restores 20 Weave when consumed.", ItemCategory.Consumable, EquipmentSlot.None, MinWork: 1, MaxWork: 2),
        new("Weave Elixir",          "A flask of swirling violet liquid. Restores 50 Weave when consumed.",       ItemCategory.Consumable, EquipmentSlot.None, MinWork: 2, MaxWork: 4),
        new("Fortitude Brew",        "A dark amber brew that hardens the body. +10% max HP for next combat.",     ItemCategory.Consumable, EquipmentSlot.None, MinWork: 2, MaxWork: 3),
        new("Speed Draught",         "A clear, fizzing draught that quickens the limbs. +20% speed for next combat.", ItemCategory.Consumable, EquipmentSlot.None, MinWork: 2, MaxWork: 3),
        new("Strength Tonic",        "A thick red tonic with a sharp bite. +15% strike damage for next combat.",  ItemCategory.Consumable, EquipmentSlot.None, MinWork: 2, MaxWork: 3),
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
    /// isAutoFarm: if true, applies degraded rewards (−40% drop chance, −2 workmanship).
    /// Returns LootDropResult with Dropped=false if no drop or inventory full.
    /// </summary>
    public async Task<LootDropResult> RollLootDropAsync(
        int dangerLevel,
        Guid ownerId,
        WorldId originWorld,
        int currentInventoryCount,
        int maxInventorySlots,
        CancellationToken ct = default,
        Player? player = null,
        string? zoneName = null,
        bool isAutoFarm = false)
    {
        // Inventory full check
        if (currentInventoryCount >= maxInventorySlots)
            return new LootDropResult(false, null, "Your inventory is full!");

        // Drop chance: 40% at danger 1, up to 80% at danger 10
        // Auto-farm reduces drop chance by 40% (e.g. 48% → ~29%)
        var dropChance = 40 + dangerLevel * 4;
        if (isAutoFarm)
            dropChance = (int)(dropChance * 0.60);

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

        // Separate consumable drop: 10% flat chance (independent of main drop)
        if (Random.Shared.Next(100) < 10)
        {
            var consumableTemplate = ConsumableTemplates[Random.Shared.Next(ConsumableTemplates.Length)];
            var consumableWork = Workmanship.Of(Math.Clamp(
                consumableTemplate.MinWork + Random.Shared.Next(consumableTemplate.MaxWork - consumableTemplate.MinWork + 1),
                1, 10));
            var consumableItem = Item.Create(consumableTemplate.Name, consumableTemplate.Description,
                consumableTemplate.Category, consumableWork, originWorld, slot: consumableTemplate.Slot);
            consumableItem.SetOwner(ownerId);
            await _itemRepository.AddAsync(consumableItem, ct);
            _logger.LogInformation("Consumable drop for player {PlayerId}: {Name}", ownerId, consumableTemplate.Name);
        }

        if (Random.Shared.Next(100) >= dropChance)
            return new LootDropResult(false, null, string.Empty);

        // Select a template — pick from ALL templates at all danger levels.
        // Previously this index-capped selection caused low-danger loot to only draw
        // from the first few templates (mostly melee weapons), so legs/hands/feet armor
        // never dropped at danger 1-3. Now we pick uniformly across every slot type;
        // workmanship is already scaled by the danger bonus below.
        var template = Templates[Random.Shared.Next(Templates.Length)];

        // For material categories, replace with a biome-appropriate material
        if (template.Category is ItemCategory.Component or ItemCategory.Reagent)
        {
            template = PickBiomeMaterial(zoneName, dangerLevel);
        }

        // Workmanship: template range + danger bonus
        var workValue = template.MinWork + (int)(Random.Shared.NextDouble() * (template.MaxWork - template.MinWork + 1));
        workValue = Math.Clamp(workValue, 1, 10);
        // Auto-farm penalty: −2 workmanship (minimum W1)
        if (isAutoFarm)
            workValue = Math.Max(1, workValue - 2);
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

    // -------------------------------------------------------------------------
    // Biome helpers
    // -------------------------------------------------------------------------

    private static string GetBiome(string? zoneName) => zoneName switch
    {
        "Caervorn Highlands" or "Gravenhold" => "mountain",
        "The Thornwood"                       => "forest",
        "Portmere (Compact)" or "Starting Road" => "plains",
        "Gravenmarsh"                         => "swamp",
        "The Drowned Coast"                   => "water",
        "The Ashen Reach"                     => "desert",
        "The Maw Borderlands"                 => "wyrd",
        _                                     => "plains",
    };

    /// <summary>
    /// Picks a material from the biome pool.
    /// 70% chance: biome-specific material (filtered by danger level for min workmanship).
    /// 30% chance: common material.
    /// </summary>
    private static LootTemplate PickBiomeMaterial(string? zoneName, int dangerLevel)
    {
        var biome = GetBiome(zoneName);

        // 30% chance: common material regardless of biome
        if (Random.Shared.Next(100) < 30)
            return CommonMaterials[Random.Shared.Next(CommonMaterials.Length)];

        if (!BiomeMaterialTemplates.TryGetValue(biome, out var pool))
            pool = BiomeMaterialTemplates["plains"];

        // Higher danger gives access to rarer (higher MinWork) materials
        // Danger 1-3: all materials accessible; danger 4-6: rarer items more likely; 7+: rarest accessible
        var maxMinWork = 1 + dangerLevel;  // danger 1 → max MinWork 2; danger 9 → max MinWork 10
        var eligible = pool.Where(t => t.MinWork <= maxMinWork).ToArray();
        if (eligible.Length == 0) eligible = pool;

        // Weight toward rarer items at higher danger: use weighted selection
        // Weight = dangerLevel bonus for items with higher MinWork
        var totalWeight = 0;
        var weights = new int[eligible.Length];
        for (var i = 0; i < eligible.Length; i++)
        {
            // Base weight 10; rarer items get +2*dangerLevel bonus per point of MinWork above 1
            weights[i] = 10 + (eligible[i].MinWork - 1) * dangerLevel;
            totalWeight += weights[i];
        }

        var roll = Random.Shared.Next(totalWeight);
        var cumulative = 0;
        for (var i = 0; i < eligible.Length; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return eligible[i];
        }

        return eligible[^1];
    }

    private sealed record LootTemplate(
        string Name,
        string Description,
        ItemCategory Category,
        EquipmentSlot Slot,
        int MinWork,
        int MaxWork);
}
