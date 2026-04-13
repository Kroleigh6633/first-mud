using FirstMud.Application.Content;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.Interfaces;
using FirstMud.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Services;

public record LootDropResult(bool Dropped, Item? Item, string Message, bool AutoSalvaged = false);

/// <summary>
/// Rolls post-combat loot drops.
///
/// Authoring (what can drop, in which biome, with what workmanship range,
/// at what weight) lives in <c>content/loot-tables.json</c> behind
/// <see cref="IContentProvider"/>. This service owns the procedural layer:
/// random picks, drop-chance formula, workmanship rolls, biome pool
/// selection, pre-imbue rolls, inventory/stack merging, and auto-salvage.
/// </summary>
public class LootService
{
    private readonly IItemRepository _itemRepository;
    private readonly SalvageService _salvageService;
    private readonly IContentProvider _content;
    private readonly ILogger<LootService> _logger;

    public LootService(
        IItemRepository itemRepository,
        SalvageService salvageService,
        IContentProvider content,
        ILogger<LootService> logger)
    {
        _itemRepository = itemRepository;
        _salvageService = salvageService;
        _content = content;
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

        var tables = _content.LootTables;

        // Drop chance formula (data-driven): base + perDanger * danger, auto-farm multiplier.
        var dropChance = tables.DropChance.Base + dangerLevel * tables.DropChance.PerDangerLevel;
        if (isAutoFarm)
            dropChance = (int)(dropChance * tables.DropChance.AutoFarmMultiplier);

        // Independent flat-chance rolls (tapers, consumables). Each rolls once
        // on its own percentage regardless of the main drop outcome.
        foreach (var indy in tables.IndependentRolls)
        {
            if (Random.Shared.Next(100) >= indy.ChancePercent) continue;
            var pool = _content.GetDropPool(indy.PoolId);
            if (pool is null || pool.Entries.Count == 0) continue;

            var entry = PickWeighted(pool.Entries);
            var work = Workmanship.Of(Math.Clamp(
                entry.MinWorkmanship + Random.Shared.Next(entry.MaxWorkmanship - entry.MinWorkmanship + 1),
                1, 10));
            var item = Item.Create(entry.ItemName, entry.Description, entry.Category, work, originWorld,
                slot: EquipmentSlot.None);
            item.SetOwner(ownerId);

            // Stack-merge for stackable categories (Tapers are Reagents — stackable).
            if (item.IsStackable)
            {
                var existing = await _itemRepository.GetByOwnerAndNameAsync(ownerId, entry.ItemName, entry.Category, ct);
                if (existing is not null)
                {
                    existing.AddQuantity(1);
                    await _itemRepository.UpdateAsync(existing, ct);
                    _logger.LogInformation("{RollId} stack merge for player {PlayerId}: {Name}", indy.Id, ownerId, entry.ItemName);
                    continue;
                }
            }

            await _itemRepository.AddAsync(item, ct);
            _logger.LogInformation("{RollId} drop for player {PlayerId}: {Name}", indy.Id, ownerId, entry.ItemName);
        }

        if (Random.Shared.Next(100) >= dropChance)
            return new LootDropResult(false, null, string.Empty);

        // Select a template — at danger >= rareSlotBoost.Threshold, boost rare
        // slots by appending extra copies (increases weight).
        LootTemplateDefinition template;
        var boost = tables.RareSlotBoost;
        if (boost is not null && dangerLevel >= boost.DangerThreshold && boost.ExtraWeight > 0)
        {
            var weightedPool = new List<LootTemplateDefinition>(tables.EquipmentTemplates);
            foreach (var t in tables.EquipmentTemplates)
            {
                if (boost.Slots.Contains(t.Slot))
                {
                    for (var i = 0; i < boost.ExtraWeight; i++)
                        weightedPool.Add(t);
                }
            }
            template = weightedPool[Random.Shared.Next(weightedPool.Count)];
        }
        else
        {
            var eq = tables.EquipmentTemplates;
            template = eq[Random.Shared.Next(eq.Count)];
        }

        // For material categories, replace with a biome-appropriate material.
        string itemName;
        string itemDescription;
        ItemCategory itemCategory;
        EquipmentSlot itemSlot;
        int minWork;
        int maxWork;

        if (template.Category is ItemCategory.Component or ItemCategory.Reagent)
        {
            var mat = PickBiomeMaterial(zoneName, dangerLevel);
            itemName = mat.ItemName;
            itemDescription = mat.Description;
            itemCategory = mat.Category;
            itemSlot = EquipmentSlot.None;
            minWork = mat.MinWorkmanship;
            maxWork = mat.MaxWorkmanship;
        }
        else
        {
            itemName = template.Name;
            itemDescription = template.Description;
            itemCategory = template.Category;
            itemSlot = template.Slot;
            minWork = template.MinWorkmanship;
            maxWork = template.MaxWorkmanship;
        }

        // Workmanship: template range + danger bonus (danger / 2 => d6=+3, d10=+5).
        int dangerBonus = dangerLevel / 2;
        int range = maxWork - minWork;
        var workValue = minWork + dangerBonus + (range > 0 ? Random.Shared.Next(range + 1) : 0);
        workValue = Math.Clamp(workValue, 1, 10);
        if (isAutoFarm)
            workValue = Math.Max(1, workValue - 2);
        var workmanship = Workmanship.Of(workValue);

        var rolledItem = Item.Create(itemName, itemDescription, itemCategory, workmanship, originWorld, slot: itemSlot);
        rolledItem.SetOwner(ownerId);

        // Pre-imbued loot: high danger gives a configured chance of arriving
        // imbued with a biome-matching element (equipment only).
        bool wasPreImbued = false;
        var pre = tables.PreImbue;
        if (dangerLevel >= pre.DangerThreshold
            && rolledItem.Slot != EquipmentSlot.None
            && !rolledItem.IsStackable
            && Random.Shared.Next(100) < pre.ChancePercent)
        {
            var imbueType = GetBiomeImbueType(zoneName);
            rolledItem.ApplyImbue(imbueType, pre.ImbueStrength);
            wasPreImbued = true;
        }

        // Auto-salvage.
        if (player is not null)
        {
            var autoSalvageMessage = await _salvageService.TryAutoSalvageAsync(player, rolledItem, ct);
            if (autoSalvageMessage is not null)
            {
                _logger.LogInformation("Auto-salvaged loot for player {PlayerId}: {ItemName} W{Workmanship}",
                    ownerId, rolledItem.Name, workValue);
                return new LootDropResult(true, rolledItem, autoSalvageMessage, AutoSalvaged: true);
            }
        }

        // Stack-merge.
        if (rolledItem.IsStackable)
        {
            var existingStack = await _itemRepository.GetByOwnerAndNameAsync(ownerId, itemName, itemCategory, ct);
            if (existingStack is not null)
            {
                existingStack.AddQuantity(1);
                await _itemRepository.UpdateAsync(existingStack, ct);
                var dangerContext = dangerLevel >= 4 ? $" (danger {dangerLevel} bonus)" : string.Empty;
                var stackMessage = $"You found: {existingStack.Name} (now x{existingStack.Quantity}) [Workmanship {workValue}]{dangerContext}!";
                _logger.LogInformation("Loot stack merge for player {PlayerId}: {ItemName}", ownerId, itemName);
                return new LootDropResult(true, existingStack, stackMessage);
            }
        }

        await _itemRepository.AddAsync(rolledItem, ct);

        var dangerSuffix = dangerLevel >= 4 ? $" (danger {dangerLevel} bonus)" : string.Empty;
        var imbueSuffix  = wasPreImbued ? " [pre-imbued!]" : string.Empty;
        var message = $"You found: {rolledItem.DisplayName} W{workValue}{dangerSuffix}{imbueSuffix}!";
        _logger.LogInformation(
            "Loot drop for player {PlayerId}: {ItemName} W{Workmanship} Slot={Slot} Category={Category} DangerBonus={DangerBonus} PreImbued={PreImbued}",
            ownerId, rolledItem.Name, workValue, rolledItem.Slot, rolledItem.Category, dangerBonus, wasPreImbued);

        return new LootDropResult(true, rolledItem, message);
    }

    // ─── Biome helpers ────────────────────────────────────────────────────

    private string GetBiome(string? zoneName)
    {
        if (!string.IsNullOrWhiteSpace(zoneName)
            && _content.LootTables.BiomeByZoneName.TryGetValue(zoneName, out var biome))
            return biome;
        return "plains";
    }

    /// <summary>
    /// Picks a material from the biome pool.
    /// commonMaterialChancePercent: biome-specific vs. common roll.
    /// At danger>=dangerSuppressCommonAt, common pool is suppressed entirely.
    /// At danger>=dangerTier2MinAt, only Tier 2+ (MinW>=2) biome entries qualify.
    /// Higher danger weights rarer (higher MinW) entries more heavily.
    /// </summary>
    private DropPoolEntryDefinition PickBiomeMaterial(string? zoneName, int dangerLevel)
    {
        var cfg = _content.LootTables.BiomeMaterial;
        var biome = GetBiome(zoneName);

        // Common-pool branch.
        if (dangerLevel < cfg.DangerSuppressCommonAt
            && Random.Shared.Next(100) < cfg.CommonMaterialChancePercent)
        {
            var common = _content.GetDropPool("common-materials");
            if (common is not null && common.Entries.Count > 0)
                return common.Entries[Random.Shared.Next(common.Entries.Count)];
        }

        // Biome-specific branch.
        if (!_content.LootTables.PoolByBiome.TryGetValue(biome, out var poolId))
            _content.LootTables.PoolByBiome.TryGetValue("plains", out poolId);

        var pool = poolId is not null ? _content.GetDropPool(poolId) : null;
        if (pool is null || pool.Entries.Count == 0)
        {
            // Absolute fallback: the common pool. Should never happen with a
            // valid loot-tables.json (content validation enforces map refs).
            var common = _content.GetDropPool("common-materials");
            if (common is not null && common.Entries.Count > 0)
                return common.Entries[Random.Shared.Next(common.Entries.Count)];
            throw new InvalidOperationException($"No drop pool available for biome '{biome}'.");
        }

        // Danger-gated eligibility filter.
        IReadOnlyList<DropPoolEntryDefinition> eligible;
        if (dangerLevel >= cfg.DangerTier2MinAt)
        {
            var t2 = pool.Entries.Where(e => e.MinWorkmanship >= 2).ToList();
            eligible = t2.Count == 0 ? pool.Entries : t2;
        }
        else
        {
            var maxMinWork = 1 + dangerLevel;
            var filtered = pool.Entries.Where(e => e.MinWorkmanship <= maxMinWork).ToList();
            eligible = filtered.Count == 0 ? pool.Entries : filtered;
        }

        // Weighted selection: base + perTier * (MinW-1) * dangerLevel.
        var totalWeight = 0;
        var weights = new int[eligible.Count];
        for (var i = 0; i < eligible.Count; i++)
        {
            weights[i] = cfg.BaseWeight + (eligible[i].MinWorkmanship - 1) * dangerLevel;
            totalWeight += weights[i];
        }

        var roll = Random.Shared.Next(totalWeight);
        var cumulative = 0;
        for (var i = 0; i < eligible.Count; i++)
        {
            cumulative += weights[i];
            if (roll < cumulative)
                return eligible[i];
        }
        return eligible[^1];
    }

    private ImbueType GetBiomeImbueType(string? zoneName)
    {
        var biome = GetBiome(zoneName);
        return _content.LootTables.ImbueByBiome.TryGetValue(biome, out var it) ? it : ImbueType.Air;
    }

    private static DropPoolEntryDefinition PickWeighted(IReadOnlyList<DropPoolEntryDefinition> entries)
    {
        var total = 0;
        for (var i = 0; i < entries.Count; i++) total += entries[i].Weight;
        if (total <= 0) return entries[Random.Shared.Next(entries.Count)];
        var roll = Random.Shared.Next(total);
        var cumulative = 0;
        for (var i = 0; i < entries.Count; i++)
        {
            cumulative += entries[i].Weight;
            if (roll < cumulative) return entries[i];
        }
        return entries[^1];
    }
}
