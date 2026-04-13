using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// A single item template that can roll out of a pool. Covers both equipment
/// (Slot != None) and materials/reagents/consumables (Slot == None).
/// This is the authored data — procedural logic (random pick, workmanship
/// roll, imbue seed) stays in LootService.
/// </summary>
public sealed record LootTemplateDefinition(
    string Name,
    string Description,
    ItemCategory Category,
    EquipmentSlot Slot,
    int MinWorkmanship,
    int MaxWorkmanship);

/// <summary>
/// Weighted entry inside a named drop pool. MinQty/MaxQty default to 1/1 when
/// the source data omits them (quantity is handled at stack-merge time today,
/// not per-entry). <c>Guaranteed</c> is reserved for future special-case drops
/// that must always roll alongside the random pick.
/// </summary>
public sealed record DropPoolEntryDefinition(
    string ItemName,
    string Description,
    ItemCategory Category,
    int MinWorkmanship,
    int MaxWorkmanship,
    int Weight,
    int MinQty,
    int MaxQty,
    bool Guaranteed);

/// <summary>
/// A named, weighted pool of drop entries. Referenced by biome map or by the
/// flat-chance <c>independentRolls</c> (tapers, consumables).
/// </summary>
public sealed record DropPoolDefinition(
    string Id,
    IReadOnlyList<DropPoolEntryDefinition> Entries);

/// <summary>
/// Maps a monster (or, in first-mud today, a zone/biome) to the pools it draws
/// from and how many rolls each kill gets. The current loot system is
/// biome-keyed, not monster-keyed — this shape exists to fulfil the
/// content-layer contract and will naturally accept real monster ids once
/// MonsterDefinition stabilises.
/// </summary>
public sealed record MonsterDropDefinition(
    string MonsterId,
    IReadOnlyList<string> Pools,
    int RollsMin,
    int RollsMax);

/// <summary>
/// Workmanship curve for a given tier (danger level). Currently informational —
/// the runtime formula (<c>dangerBonus = dangerLevel / 2</c>, clamped 1..10)
/// lives in LootService. Kept in data so designers can review the tuning
/// without reading C# and so a future switch to table-lookup is a one-line
/// refactor.
/// </summary>
public sealed record TierCurveDefinition(
    int Tier,
    int MinWorkmanship,
    int MaxWorkmanship,
    int DangerBonus);

/// <summary>
/// Rare-slot boost rule applied to the main equipment pool above a danger
/// threshold (e.g. Focus and Accessory get extra weight at danger 7+).
/// </summary>
public sealed record RareSlotBoostDefinition(
    int DangerThreshold,
    IReadOnlyList<EquipmentSlot> Slots,
    int ExtraWeight);

/// <summary>
/// Flat-chance independent roll (tapers at 15%, consumables at 10%).
/// </summary>
public sealed record IndependentRollDefinition(
    string Id,
    string PoolId,
    int ChancePercent);

/// <summary>
/// Drop-chance formula parameters.
/// </summary>
public sealed record DropChanceConfig(
    int Base,
    int PerDangerLevel,
    double AutoFarmMultiplier);

/// <summary>
/// Tunables for biome-material pool selection.
/// </summary>
public sealed record BiomeMaterialConfig(
    int CommonMaterialChancePercent,
    int DangerSuppressCommonAt,
    int DangerTier2MinAt,
    int BaseWeight,
    int RarityWeightPerTier);

/// <summary>
/// Pre-imbue tuning (applied to equipment drops at high danger).
/// </summary>
public sealed record PreImbueConfig(
    int DangerThreshold,
    int ChancePercent,
    float ImbueStrength);

/// <summary>
/// Complete aggregate of all loot-table data loaded from
/// <c>content/loot-tables.json</c>. LootService resolves against the fields
/// here rather than hardcoded arrays.
/// </summary>
public sealed record LootTablesDefinition(
    IReadOnlyList<LootTemplateDefinition> EquipmentTemplates,
    RareSlotBoostDefinition? RareSlotBoost,
    IReadOnlyList<DropPoolDefinition> DropPools,
    IReadOnlyDictionary<string, string> BiomeByZoneName,          // zone → biome
    IReadOnlyDictionary<string, ImbueType> ImbueByBiome,          // biome → imbue type
    IReadOnlyDictionary<string, string> PoolByBiome,              // biome → pool id
    IReadOnlyList<IndependentRollDefinition> IndependentRolls,
    DropChanceConfig DropChance,
    BiomeMaterialConfig BiomeMaterial,
    IReadOnlyList<TierCurveDefinition> TierCurves,
    PreImbueConfig PreImbue,
    IReadOnlyList<MonsterDropDefinition> MonsterDrops);
