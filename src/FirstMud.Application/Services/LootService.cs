using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public record LootDropResult(bool Dropped, Item? Item, string Message);

public class LootService
{
    private readonly IItemRepository _itemRepository;
    private readonly ILogger<LootService> _logger;

    // -------------------------------------------------------------------------
    // Static loot templates — 15 entries covering weapons, armor, materials
    // -------------------------------------------------------------------------
    private static readonly LootTemplate[] Templates =
    [
        new("Iron Dagger",      "A short blade honed from iron. Reliable in close quarters.",                ItemCategory.Weapon,    MinWork: 1, MaxWork: 3),
        new("Iron Sword",       "A straight iron sword, workhorse of the Aeldran roads.",                    ItemCategory.Weapon,    MinWork: 2, MaxWork: 5),
        new("Thornwood Bow",    "A recurve bow carved from resilient thornwood.",                             ItemCategory.Weapon,    MinWork: 2, MaxWork: 4),
        new("Stone Axe",        "A heavy axe with a hand-knapped stone head. Brutal and primitive.",          ItemCategory.Weapon,    MinWork: 1, MaxWork: 3),
        new("Leather Cap",      "Cured hide shaped into a simple helm.",                                     ItemCategory.Armor,     MinWork: 1, MaxWork: 3),
        new("Leather Vest",     "A padded leather body vest offering moderate protection.",                   ItemCategory.Armor,     MinWork: 2, MaxWork: 4),
        new("Iron Buckler",     "A small round shield banded with iron.",                                    ItemCategory.Armor,     MinWork: 2, MaxWork: 5),
        new("Scale Coif",       "Overlapping iron scales form a tight hood.",                                ItemCategory.Armor,     MinWork: 3, MaxWork: 6),
        new("Iron Ore",         "Rough lumps of iron ore, ready for the smelter.",                           ItemCategory.Component, MinWork: 1, MaxWork: 2),
        new("Beast Hide",       "Thick hide stripped from a slain creature.",                                ItemCategory.Component, MinWork: 1, MaxWork: 2),
        new("Sinew",            "Dried sinew — useful in bowstrings and bindings.",                          ItemCategory.Component, MinWork: 1, MaxWork: 2),
        new("Thornwood Herb",   "A bitter medicinal herb found only in the Thornwood.",                      ItemCategory.Reagent,   MinWork: 1, MaxWork: 3),
        new("Dravenite Dust",   "Fine crystalline powder with latent magical resonance.",                    ItemCategory.Component, MinWork: 2, MaxWork: 4),
        new("Bone Fragment",    "A large bone fragment — useful as a crafting material.",                    ItemCategory.Component, MinWork: 1, MaxWork: 2),
        new("Wyrd Shard",       "A jagged shard of crystallised Wyrd-energy. Handle with care.",             ItemCategory.Reagent,   MinWork: 3, MaxWork: 7),
    ];

    public LootService(IItemRepository itemRepository, ILogger<LootService> logger)
    {
        _itemRepository = itemRepository;
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
        CancellationToken ct = default)
    {
        // Inventory full check
        if (currentInventoryCount >= maxInventorySlots)
            return new LootDropResult(false, null, "Your inventory is full!");

        // Drop chance: 40% at danger 1, up to 80% at danger 10
        var dropChance = 40 + dangerLevel * 4;
        if (Random.Shared.Next(100) >= dropChance)
            return new LootDropResult(false, null, string.Empty);

        // Select a template — higher danger skews toward later (better) templates
        var maxTemplateIndex = Math.Min(Templates.Length - 1, 4 + dangerLevel);
        var template = Templates[Random.Shared.Next(0, maxTemplateIndex + 1)];

        // Workmanship: template range + danger bonus
        var workValue = template.MinWork + (int)(Random.Shared.NextDouble() * (template.MaxWork - template.MinWork + 1));
        workValue = Math.Clamp(workValue, 1, 10);
        var workmanship = Workmanship.Of(workValue);

        var item = Item.Create(template.Name, template.Description, template.Category, workmanship, originWorld);
        item.SetOwner(ownerId);

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
        int MinWork,
        int MaxWork);
}
