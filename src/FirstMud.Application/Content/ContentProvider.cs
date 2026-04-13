using System.Text.Json;
using System.Text.Json.Serialization;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace FirstMud.Application.Content;

/// <summary>
/// JSON-backed implementation of <see cref="IContentProvider"/>.
///
/// Loads and validates content files at construction time. Validation is
/// strict-enough-to-catch-typos: required fields, enum values, cross-refs,
/// and invariants (unique ids, positive weights, sensible ranges). Anything
/// invalid throws on startup so misconfigured content cannot reach
/// production silently.
/// </summary>
public sealed class ContentProvider : IContentProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly HashSet<string> ValidEffectTypes =
        new(StringComparer.Ordinal) { "Heal", "RestoreWeave", "Buff" };

    private static readonly HashSet<string> ValidBuffKeys =
        new(StringComparer.Ordinal) { "MaxHpBonus", "SpeedBonus", "StrikeDamageBonus" };

    private readonly string _contentRoot;
    private readonly ILogger<ContentProvider>? _logger;

    private IReadOnlyList<ConsumableDefinition> _consumables = Array.Empty<ConsumableDefinition>();
    private LootTablesDefinition _lootTables = EmptyLootTables();
    private Dictionary<string, DropPoolDefinition> _poolsById = new(StringComparer.Ordinal);
    private Dictionary<string, MonsterDropDefinition> _monsterDropsById = new(StringComparer.Ordinal);
    private Dictionary<int, TierCurveDefinition> _tierCurvesByTier = new();

    public ContentProvider(string contentRoot, ILogger<ContentProvider>? logger = null)
    {
        _contentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<ConsumableDefinition> Consumables => _consumables;

    public LootTablesDefinition LootTables => _lootTables;

    public ConsumableDefinition? ResolveConsumable(string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;
        var lower = itemName.ToLowerInvariant();
        foreach (var def in _consumables)
        {
            if (lower.Contains(def.MatchToken))
                return def;
        }
        return null;
    }

    public IReadOnlyList<ConsumableDefinition> ConsumablesByGroup(string priorityGroup)
    {
        return _consumables
            .Where(c => string.Equals(c.PriorityGroup, priorityGroup, StringComparison.Ordinal))
            .OrderBy(c => c.PriorityRank)
            .ToList();
    }

    public DropPoolDefinition? GetDropPool(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _poolsById.TryGetValue(id, out var pool) ? pool : null;
    }

    public IReadOnlyList<DropPoolDefinition> AllDropPools() => _lootTables.DropPools;

    public MonsterDropDefinition? GetMonsterDrop(string monsterId)
    {
        if (string.IsNullOrWhiteSpace(monsterId)) return null;
        return _monsterDropsById.TryGetValue(monsterId, out var md) ? md : null;
    }

    public TierCurveDefinition? GetTierCurve(int tier)
        => _tierCurvesByTier.TryGetValue(tier, out var c) ? c : null;

    public void Reload()
    {
        _consumables = LoadConsumables();
        _lootTables = LoadLootTables();
        _poolsById = _lootTables.DropPools.ToDictionary(p => p.Id, StringComparer.Ordinal);
        _monsterDropsById = _lootTables.MonsterDrops.ToDictionary(m => m.MonsterId, StringComparer.Ordinal);
        _tierCurvesByTier = _lootTables.TierCurves.ToDictionary(c => c.Tier);

        _logger?.LogInformation(
            "ContentProvider loaded: {ConsumableCount} consumables, {PoolCount} drop pools, {MonsterDropCount} monster drops from {Root}",
            _consumables.Count, _lootTables.DropPools.Count, _lootTables.MonsterDrops.Count, _contentRoot);
    }

    // ─── Consumables ──────────────────────────────────────────────────────

    private IReadOnlyList<ConsumableDefinition> LoadConsumables()
    {
        var path = Path.Combine(_contentRoot, "consumables.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ConsumablesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Consumables is null || doc.Consumables.Count == 0)
            throw new InvalidDataException($"{path}: no consumables defined.");

        var list = new List<ConsumableDefinition>(doc.Consumables.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Consumables)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: consumable missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate consumable id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.MatchToken))
                throw new InvalidDataException($"{path}: consumable '{raw.Id}' missing matchToken.");
            if (raw.MatchToken != raw.MatchToken.ToLowerInvariant())
                throw new InvalidDataException($"{path}: consumable '{raw.Id}' matchToken must be lowercase.");
            if (string.IsNullOrWhiteSpace(raw.EffectType) || !ValidEffectTypes.Contains(raw.EffectType))
                throw new InvalidDataException(
                    $"{path}: consumable '{raw.Id}' has invalid effectType '{raw.EffectType}'. " +
                    $"Must be one of: {string.Join(", ", ValidEffectTypes)}.");

            if (raw.EffectType == "Buff")
            {
                if (string.IsNullOrWhiteSpace(raw.BuffKey) || !ValidBuffKeys.Contains(raw.BuffKey))
                    throw new InvalidDataException(
                        $"{path}: Buff consumable '{raw.Id}' has invalid buffKey '{raw.BuffKey}'.");
            }

            list.Add(new ConsumableDefinition(
                Id: raw.Id,
                MatchToken: raw.MatchToken,
                EffectType: raw.EffectType,
                Amount: raw.Amount,
                BuffKey: raw.BuffKey,
                BuffValue: raw.BuffValue,
                PriorityGroup: raw.PriorityGroup,
                PriorityRank: raw.PriorityRank));
        }

        return list;
    }

    // ─── Loot tables ──────────────────────────────────────────────────────

    private LootTablesDefinition LoadLootTables()
    {
        var path = Path.Combine(_contentRoot, "loot-tables.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<LootTablesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.EquipmentTemplates is null || doc.EquipmentTemplates.Count == 0)
            throw new InvalidDataException($"{path}: equipmentTemplates is empty.");
        if (doc.DropPools is null || doc.DropPools.Count == 0)
            throw new InvalidDataException($"{path}: dropPools is empty.");
        if (doc.DropChance is null)
            throw new InvalidDataException($"{path}: dropChance missing.");
        if (doc.BiomeMaterialConfig is null)
            throw new InvalidDataException($"{path}: biomeMaterialConfig missing.");
        if (doc.PreImbue is null)
            throw new InvalidDataException($"{path}: preImbue missing.");

        // Equipment templates
        var equipment = new List<LootTemplateDefinition>(doc.EquipmentTemplates.Count);
        foreach (var t in doc.EquipmentTemplates)
            equipment.Add(ParseTemplate(path, t, "equipmentTemplates"));

        // Drop pools
        var pools = new List<DropPoolDefinition>(doc.DropPools.Count);
        var seenPoolIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawPool in doc.DropPools)
        {
            if (string.IsNullOrWhiteSpace(rawPool.Id))
                throw new InvalidDataException($"{path}: drop pool missing id.");
            if (!seenPoolIds.Add(rawPool.Id))
                throw new InvalidDataException($"{path}: duplicate drop pool id '{rawPool.Id}'.");
            if (rawPool.Entries is null || rawPool.Entries.Count == 0)
                throw new InvalidDataException($"{path}: drop pool '{rawPool.Id}' has no entries.");

            var entries = new List<DropPoolEntryDefinition>(rawPool.Entries.Count);
            foreach (var e in rawPool.Entries)
            {
                if (string.IsNullOrWhiteSpace(e.ItemName))
                    throw new InvalidDataException($"{path}: pool '{rawPool.Id}' entry missing itemName.");
                if (!Enum.TryParse<ItemCategory>(e.Category, ignoreCase: false, out var cat))
                    throw new InvalidDataException($"{path}: pool '{rawPool.Id}' entry '{e.ItemName}' has invalid category '{e.Category}'.");
                if (e.Weight <= 0)
                    throw new InvalidDataException($"{path}: pool '{rawPool.Id}' entry '{e.ItemName}' weight must be > 0 (got {e.Weight}).");
                if (e.MinWorkmanship < 1 || e.MaxWorkmanship < e.MinWorkmanship)
                    throw new InvalidDataException($"{path}: pool '{rawPool.Id}' entry '{e.ItemName}' workmanship range invalid ({e.MinWorkmanship}..{e.MaxWorkmanship}).");
                var minQty = e.MinQty <= 0 ? 1 : e.MinQty;
                var maxQty = e.MaxQty <= 0 ? minQty : e.MaxQty;
                if (maxQty < minQty)
                    throw new InvalidDataException($"{path}: pool '{rawPool.Id}' entry '{e.ItemName}' quantity range invalid ({minQty}..{maxQty}).");

                entries.Add(new DropPoolEntryDefinition(
                    ItemName: e.ItemName,
                    Description: e.Description ?? "",
                    Category: cat,
                    MinWorkmanship: e.MinWorkmanship,
                    MaxWorkmanship: e.MaxWorkmanship,
                    Weight: e.Weight,
                    MinQty: minQty,
                    MaxQty: maxQty,
                    Guaranteed: e.Guaranteed));
            }

            pools.Add(new DropPoolDefinition(rawPool.Id, entries));
        }

        // Biome zones → biome map
        var biomeByZone = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var bz in doc.BiomeZones ?? new())
        {
            if (string.IsNullOrWhiteSpace(bz.Biome) || bz.ZoneNames is null) continue;
            foreach (var zn in bz.ZoneNames)
            {
                if (biomeByZone.ContainsKey(zn))
                    throw new InvalidDataException($"{path}: zone '{zn}' mapped to multiple biomes.");
                biomeByZone[zn] = bz.Biome;
            }
        }

        // Biome imbue types
        var imbueByBiome = new Dictionary<string, ImbueType>(StringComparer.Ordinal);
        foreach (var row in doc.BiomeImbueTypes ?? new())
        {
            if (string.IsNullOrWhiteSpace(row.Biome) || string.IsNullOrWhiteSpace(row.ImbueType)) continue;
            if (!Enum.TryParse<ImbueType>(row.ImbueType, ignoreCase: false, out var it))
                throw new InvalidDataException($"{path}: biomeImbueTypes has invalid imbueType '{row.ImbueType}' for biome '{row.Biome}'.");
            if (imbueByBiome.ContainsKey(row.Biome))
                throw new InvalidDataException($"{path}: biome '{row.Biome}' has duplicate imbueType mapping.");
            imbueByBiome[row.Biome] = it;
        }

        // Biome → pool map
        var poolByBiome = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in doc.BiomeDropPoolMap ?? new())
        {
            if (string.IsNullOrWhiteSpace(row.Biome) || string.IsNullOrWhiteSpace(row.PoolId)) continue;
            if (!seenPoolIds.Contains(row.PoolId))
                throw new InvalidDataException($"{path}: biomeDropPoolMap references unknown poolId '{row.PoolId}' for biome '{row.Biome}'.");
            if (poolByBiome.ContainsKey(row.Biome))
                throw new InvalidDataException($"{path}: biome '{row.Biome}' mapped to multiple pools.");
            poolByBiome[row.Biome] = row.PoolId;
        }

        // Independent rolls
        var indy = new List<IndependentRollDefinition>();
        var seenIndyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in doc.IndependentRolls ?? new())
        {
            if (string.IsNullOrWhiteSpace(row.Id))
                throw new InvalidDataException($"{path}: independentRoll missing id.");
            if (!seenIndyIds.Add(row.Id))
                throw new InvalidDataException($"{path}: duplicate independentRoll id '{row.Id}'.");
            if (!seenPoolIds.Contains(row.PoolId))
                throw new InvalidDataException($"{path}: independentRoll '{row.Id}' references unknown poolId '{row.PoolId}'.");
            if (row.ChancePercent < 0 || row.ChancePercent > 100)
                throw new InvalidDataException($"{path}: independentRoll '{row.Id}' chancePercent out of range (0-100).");
            indy.Add(new IndependentRollDefinition(row.Id, row.PoolId, row.ChancePercent));
        }

        // Rare slot boost
        RareSlotBoostDefinition? boost = null;
        if (doc.RareSlotBoost is not null)
        {
            var slots = new List<EquipmentSlot>();
            foreach (var s in doc.RareSlotBoost.Slots ?? new())
            {
                if (!Enum.TryParse<EquipmentSlot>(s, ignoreCase: false, out var slot))
                    throw new InvalidDataException($"{path}: rareSlotBoost has invalid slot '{s}'.");
                slots.Add(slot);
            }
            boost = new RareSlotBoostDefinition(
                doc.RareSlotBoost.DangerThreshold,
                slots,
                doc.RareSlotBoost.ExtraWeight);
        }

        // Tier curves
        var curves = new List<TierCurveDefinition>();
        var seenTiers = new HashSet<int>();
        foreach (var row in doc.TierWorkmanshipCurves ?? new())
        {
            if (!seenTiers.Add(row.Tier))
                throw new InvalidDataException($"{path}: duplicate tier '{row.Tier}' in tierWorkmanshipCurves.");
            if (row.MinWorkmanship < 1 || row.MaxWorkmanship < row.MinWorkmanship)
                throw new InvalidDataException($"{path}: tier '{row.Tier}' workmanship range invalid ({row.MinWorkmanship}..{row.MaxWorkmanship}).");
            curves.Add(new TierCurveDefinition(row.Tier, row.MinWorkmanship, row.MaxWorkmanship, row.DangerBonus));
        }

        // Monster drops (optional; empty by default today)
        var monsterDrops = new List<MonsterDropDefinition>();
        var seenMonsterIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in doc.MonsterDrops ?? new())
        {
            if (string.IsNullOrWhiteSpace(row.MonsterId))
                throw new InvalidDataException($"{path}: monsterDrop missing monsterId.");
            if (!seenMonsterIds.Add(row.MonsterId))
                throw new InvalidDataException($"{path}: duplicate monsterDrop for '{row.MonsterId}'.");
            var mdPools = row.Pools ?? new List<string>();
            foreach (var pid in mdPools)
                if (!seenPoolIds.Contains(pid))
                    throw new InvalidDataException($"{path}: monsterDrop '{row.MonsterId}' references unknown poolId '{pid}'.");
            var rollsMin = row.RollsMin <= 0 ? 1 : row.RollsMin;
            var rollsMax = row.RollsMax <= 0 ? rollsMin : row.RollsMax;
            if (rollsMax < rollsMin)
                throw new InvalidDataException($"{path}: monsterDrop '{row.MonsterId}' roll range invalid ({rollsMin}..{rollsMax}).");
            monsterDrops.Add(new MonsterDropDefinition(row.MonsterId, mdPools, rollsMin, rollsMax));
        }

        var dropChance = new DropChanceConfig(
            doc.DropChance.Base,
            doc.DropChance.PerDangerLevel,
            doc.DropChance.AutoFarmMultiplier);

        var biomeMat = new BiomeMaterialConfig(
            doc.BiomeMaterialConfig.CommonMaterialChancePercent,
            doc.BiomeMaterialConfig.DangerSuppressCommonAt,
            doc.BiomeMaterialConfig.DangerTier2MinAt,
            doc.BiomeMaterialConfig.BaseWeight,
            doc.BiomeMaterialConfig.RarityWeightPerTier);

        var preImbue = new PreImbueConfig(
            doc.PreImbue.DangerThreshold,
            doc.PreImbue.ChancePercent,
            doc.PreImbue.ImbueStrength);

        return new LootTablesDefinition(
            EquipmentTemplates: equipment,
            RareSlotBoost: boost,
            DropPools: pools,
            BiomeByZoneName: biomeByZone,
            ImbueByBiome: imbueByBiome,
            PoolByBiome: poolByBiome,
            IndependentRolls: indy,
            DropChance: dropChance,
            BiomeMaterial: biomeMat,
            TierCurves: curves,
            PreImbue: preImbue,
            MonsterDrops: monsterDrops);
    }

    private static LootTemplateDefinition ParseTemplate(string path, RawTemplate t, string where)
    {
        if (string.IsNullOrWhiteSpace(t.Name))
            throw new InvalidDataException($"{path}: {where} template missing name.");
        if (!Enum.TryParse<ItemCategory>(t.Category, ignoreCase: false, out var cat))
            throw new InvalidDataException($"{path}: {where} template '{t.Name}' has invalid category '{t.Category}'.");
        if (!Enum.TryParse<EquipmentSlot>(t.Slot, ignoreCase: false, out var slot))
            throw new InvalidDataException($"{path}: {where} template '{t.Name}' has invalid slot '{t.Slot}'.");
        if (t.MinWorkmanship < 1 || t.MaxWorkmanship < t.MinWorkmanship)
            throw new InvalidDataException($"{path}: {where} template '{t.Name}' workmanship range invalid ({t.MinWorkmanship}..{t.MaxWorkmanship}).");
        return new LootTemplateDefinition(
            t.Name, t.Description ?? "", cat, slot, t.MinWorkmanship, t.MaxWorkmanship);
    }

    private static LootTablesDefinition EmptyLootTables() => new(
        EquipmentTemplates: Array.Empty<LootTemplateDefinition>(),
        RareSlotBoost: null,
        DropPools: Array.Empty<DropPoolDefinition>(),
        BiomeByZoneName: new Dictionary<string, string>(),
        ImbueByBiome: new Dictionary<string, ImbueType>(),
        PoolByBiome: new Dictionary<string, string>(),
        IndependentRolls: Array.Empty<IndependentRollDefinition>(),
        DropChance: new DropChanceConfig(40, 4, 0.60),
        BiomeMaterial: new BiomeMaterialConfig(30, 8, 8, 10, 2),
        TierCurves: Array.Empty<TierCurveDefinition>(),
        PreImbue: new PreImbueConfig(8, 10, 0.2f),
        MonsterDrops: Array.Empty<MonsterDropDefinition>());

    // ─── JSON DTOs ────────────────────────────────────────────────────────

    private sealed class ConsumablesFile
    {
        [JsonPropertyName("consumables")]
        public List<RawConsumable>? Consumables { get; set; }
    }

    private sealed class RawConsumable
    {
        public string Id { get; set; } = "";
        public string MatchToken { get; set; } = "";
        public string EffectType { get; set; } = "";
        public int Amount { get; set; }
        public string? BuffKey { get; set; }
        public float BuffValue { get; set; }
        public string? PriorityGroup { get; set; }
        public int PriorityRank { get; set; }
    }

    private sealed class LootTablesFile
    {
        public List<RawTemplate>? EquipmentTemplates { get; set; }
        public RawRareSlotBoost? RareSlotBoost { get; set; }
        public List<RawPool>? DropPools { get; set; }
        public List<RawBiomeZones>? BiomeZones { get; set; }
        public List<RawBiomeImbue>? BiomeImbueTypes { get; set; }
        public List<RawBiomePool>? BiomeDropPoolMap { get; set; }
        public List<RawIndependentRoll>? IndependentRolls { get; set; }
        public RawDropChance? DropChance { get; set; }
        public RawBiomeMaterial? BiomeMaterialConfig { get; set; }
        public List<RawTierCurve>? TierWorkmanshipCurves { get; set; }
        public RawPreImbue? PreImbue { get; set; }
        public List<RawMonsterDrop>? MonsterDrops { get; set; }
    }

    private sealed class RawTemplate
    {
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string Category { get; set; } = "";
        public string Slot { get; set; } = "";
        public int MinWorkmanship { get; set; }
        public int MaxWorkmanship { get; set; }
    }

    private sealed class RawRareSlotBoost
    {
        public int DangerThreshold { get; set; }
        public List<string>? Slots { get; set; }
        public int ExtraWeight { get; set; }
    }

    private sealed class RawPool
    {
        public string Id { get; set; } = "";
        public List<RawPoolEntry>? Entries { get; set; }
    }

    private sealed class RawPoolEntry
    {
        public string ItemName { get; set; } = "";
        public string? Description { get; set; }
        public string Category { get; set; } = "";
        public int MinWorkmanship { get; set; }
        public int MaxWorkmanship { get; set; }
        public int Weight { get; set; }
        public int MinQty { get; set; }
        public int MaxQty { get; set; }
        public bool Guaranteed { get; set; }
    }

    private sealed class RawBiomeZones
    {
        public string Biome { get; set; } = "";
        public List<string>? ZoneNames { get; set; }
    }

    private sealed class RawBiomeImbue
    {
        public string Biome { get; set; } = "";
        public string ImbueType { get; set; } = "";
    }

    private sealed class RawBiomePool
    {
        public string Biome { get; set; } = "";
        public string PoolId { get; set; } = "";
    }

    private sealed class RawIndependentRoll
    {
        public string Id { get; set; } = "";
        public string PoolId { get; set; } = "";
        public int ChancePercent { get; set; }
    }

    private sealed class RawDropChance
    {
        public int Base { get; set; }
        public int PerDangerLevel { get; set; }
        public double AutoFarmMultiplier { get; set; }
    }

    private sealed class RawBiomeMaterial
    {
        public int CommonMaterialChancePercent { get; set; }
        public int DangerSuppressCommonAt { get; set; }
        public int DangerTier2MinAt { get; set; }
        public int BaseWeight { get; set; }
        public int RarityWeightPerTier { get; set; }
    }

    private sealed class RawTierCurve
    {
        public int Tier { get; set; }
        public int MinWorkmanship { get; set; }
        public int MaxWorkmanship { get; set; }
        public int DangerBonus { get; set; }
    }

    private sealed class RawPreImbue
    {
        public int DangerThreshold { get; set; }
        public int ChancePercent { get; set; }
        public float ImbueStrength { get; set; }
    }

    private sealed class RawMonsterDrop
    {
        public string MonsterId { get; set; } = "";
        public List<string>? Pools { get; set; }
        public int RollsMin { get; set; }
        public int RollsMax { get; set; }
    }
}
