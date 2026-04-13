using System.Text.Json;
using System.Text.Json.Serialization;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;
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

    private static readonly HashSet<string> ValidDutyNames =
        new(Enum.GetNames<HomesteadDuty>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidItemCategoryNames =
        new(Enum.GetNames<ItemCategory>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidWorldIdNames =
        new(Enum.GetNames<WorldId>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidTaperTypeNames =
        new(Enum.GetNames<TaperType>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidBiomes =
        new(StringComparer.Ordinal)
        { "mountain", "forest", "desert", "water", "swamp", "plains", "wyrd" };

    private readonly string _contentRoot;
    private readonly ILogger<ContentProvider>? _logger;

    private IReadOnlyList<ConsumableDefinition> _consumables = Array.Empty<ConsumableDefinition>();
    private IReadOnlyList<BuildingDefinition> _buildings = Array.Empty<BuildingDefinition>();
    private Dictionary<BuildingType, BuildingDefinition> _buildingsByType = new();
    private IReadOnlyList<RecipeDefinition> _recipes = Array.Empty<RecipeDefinition>();
    private Dictionary<string, RecipeDefinition> _recipesById = new(StringComparer.Ordinal);
    private IReadOnlyList<MonsterDefinition> _monsters = Array.Empty<MonsterDefinition>();
    private Dictionary<string, MonsterDefinition> _monstersById = new(StringComparer.Ordinal);
    private LootTablesDefinition _lootTables = EmptyLootTables();
    private Dictionary<string, DropPoolDefinition> _poolsById = new(StringComparer.Ordinal);
    private Dictionary<string, MonsterDropDefinition> _monsterDropsById = new(StringComparer.Ordinal);
    private Dictionary<int, TierCurveDefinition> _tierCurvesByTier = new();
    private IReadOnlyList<ZoneDefinition> _zones = Array.Empty<ZoneDefinition>();
    private Dictionary<string, ZoneDefinition> _zonesById = new(StringComparer.Ordinal);
    private Dictionary<string, ZoneDefinition> _zonesByName = new(StringComparer.Ordinal);
    private Dictionary<(WorldId, int), ZoneDefinition> _zonesByWorldNumber = new();
    private IReadOnlyList<FactionDefinition> _factions = Array.Empty<FactionDefinition>();
    private Dictionary<FactionId, FactionDefinition> _factionsById = new();
    private IReadOnlyList<QuestDefinition> _quests = Array.Empty<QuestDefinition>();
    private Dictionary<string, QuestDefinition> _questsById = new(StringComparer.Ordinal);
    private IReadOnlyList<QuestEdgeDefinition> _questEdges = Array.Empty<QuestEdgeDefinition>();
    private CombatCurvesDefinition _combatCurves = DefaultCombatCurves();
    private ProgressionCurvesDefinition _progressionCurves = DefaultProgressionCurves();
    private IReadOnlyList<NpcDefinition> _npcs = Array.Empty<NpcDefinition>();
    private Dictionary<string, NpcDefinition> _npcsById = new(StringComparer.Ordinal);
    private IReadOnlyList<WorldEventDefinition> _events = Array.Empty<WorldEventDefinition>();
    private Dictionary<string, WorldEventDefinition> _eventsById = new(StringComparer.Ordinal);

    private ItemValuesDefinition _itemValues = EmptyItemValues();
    private TradeCurvesDefinition _tradeCurves = DefaultTradeCurves();
    private IReadOnlyList<VendorDefinition> _vendors = Array.Empty<VendorDefinition>();
    private Dictionary<string, VendorDefinition> _vendorsByNpcId = new(StringComparer.Ordinal);

    private static readonly HashSet<string> ValidReputationTierNames =
        new(Enum.GetNames<ReputationTier>(), StringComparer.Ordinal);

    private static readonly HashSet<string> ValidQuestEdgeKinds =
        new(StringComparer.Ordinal) { "unlocks", "requires" };

    private static readonly HashSet<string> ValidNpcRoleNames =
        new(Enum.GetNames<NpcRole>(), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ValidBiomeNames = new(StringComparer.Ordinal)
    {
        "mountain", "forest", "plains", "swamp", "water", "desert", "wyrd",
        "sand", "path", "grassland", "denseForest", "snowMountain"
    };

    private static readonly HashSet<string> ValidFactionIdNames =
        new(Enum.GetNames<FactionId>(), StringComparer.Ordinal);

    public ContentProvider(string contentRoot, ILogger<ContentProvider>? logger = null)
    {
        _contentRoot = contentRoot ?? throw new ArgumentNullException(nameof(contentRoot));
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<ConsumableDefinition> Consumables => _consumables;

    public IReadOnlyList<BuildingDefinition> AllBuildings() => _buildings;

    public BuildingDefinition? GetBuilding(BuildingType type) =>
        _buildingsByType.TryGetValue(type, out var def) ? def : null;

    public int GetHutCapacity(BuildingType type, int tier)
    {
        if (!_buildingsByType.TryGetValue(type, out var def)) return 0;
        if (!def.IsHousing || def.HutCapacityByTier is null || def.HutCapacityByTier.Count == 0)
            return 0;

        // Clamp tier into the defined range (preserves the legacy switch's fallback
        // behaviour where unknown tiers returned the tier-1 value).
        var idx = tier - 1;
        if (idx < 0 || idx >= def.HutCapacityByTier.Count)
            idx = 0;
        return def.HutCapacityByTier[idx];
    }

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

    public IReadOnlyList<RecipeDefinition> AllRecipes() => _recipes;

    public RecipeDefinition? GetRecipe(string recipeId) =>
        !string.IsNullOrEmpty(recipeId) && _recipesById.TryGetValue(recipeId, out var def) ? def : null;

    public IReadOnlyList<MonsterDefinition> AllMonsters() => _monsters;

    public MonsterDefinition? GetMonster(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        return _monstersById.TryGetValue(id, out var def) ? def : null;
    }

    public IReadOnlyList<MonsterDefinition> MonstersByBiome(string biome)
    {
        if (string.IsNullOrWhiteSpace(biome)) return Array.Empty<MonsterDefinition>();
        return _monsters.Where(m =>
            string.Equals(m.Biome, biome, StringComparison.Ordinal)).ToList();
    }

    public IReadOnlyList<MonsterDefinition> MonstersByBiomeAndTier(string biome, int tier)
    {
        if (string.IsNullOrWhiteSpace(biome)) return Array.Empty<MonsterDefinition>();
        return _monsters.Where(m =>
            m.Tier == tier &&
            string.Equals(m.Biome, biome, StringComparison.Ordinal)).ToList();
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

    public IReadOnlyList<ZoneDefinition> AllZones() => _zones;

    public ZoneDefinition? GetZone(string zoneId) =>
        !string.IsNullOrEmpty(zoneId) && _zonesById.TryGetValue(zoneId, out var def) ? def : null;

    public string? GetBiomeForZone(string zoneName) =>
        !string.IsNullOrEmpty(zoneName) && _zonesByName.TryGetValue(zoneName, out var def)
            ? def.Biome
            : null;

    public (int X, int Y)? GetZoneLayoutPosition(WorldId world, int zoneNumber) =>
        _zonesByWorldNumber.TryGetValue((world, zoneNumber), out var def)
            ? (def.Layout.X, def.Layout.Y)
            : null;

    public IReadOnlyList<FactionDefinition> AllFactions() => _factions;

    public FactionDefinition? GetFaction(FactionId id) =>
        _factionsById.TryGetValue(id, out var def) ? def : null;

    public IReadOnlyList<QuestDefinition> AllQuests() => _quests;

    public QuestDefinition? GetQuest(string id) =>
        !string.IsNullOrEmpty(id) && _questsById.TryGetValue(id, out var def) ? def : null;

    public IReadOnlyList<QuestEdgeDefinition> AllQuestEdges() => _questEdges;

    public CombatCurvesDefinition CombatCurves => _combatCurves;

    public ProgressionCurvesDefinition ProgressionCurves => _progressionCurves;

    public IReadOnlyList<NpcDefinition> AllNpcs() => _npcs;

    public NpcDefinition? GetNpc(string id) =>
        string.IsNullOrEmpty(id) ? null
        : _npcsById.TryGetValue(id, out var def) ? def : null;

    public IReadOnlyList<WorldEventDefinition> AllEvents() => _events;

    public WorldEventDefinition? GetEvent(string id) =>
        !string.IsNullOrEmpty(id) && _eventsById.TryGetValue(id, out var def) ? def : null;

    public ItemValuesDefinition ItemValues => _itemValues;
    public TradeCurvesDefinition TradeCurves => _tradeCurves;
    public IReadOnlyList<VendorDefinition> AllVendors() => _vendors;
    public VendorDefinition? GetVendor(string npcId) =>
        string.IsNullOrEmpty(npcId) ? null
        : _vendorsByNpcId.TryGetValue(npcId, out var def) ? def : null;

    public void Reload()
    {
        _consumables = LoadConsumables();
        _buildings = LoadBuildings();
        _buildingsByType = _buildings.ToDictionary(b => b.Type);
        _recipes = LoadRecipes();
        _recipesById = _recipes.ToDictionary(r => r.RecipeId, StringComparer.Ordinal);
        _monsters = LoadMonsters();
        _monstersById = _monsters.ToDictionary(m => m.Id, StringComparer.Ordinal);
        _lootTables = LoadLootTables();
        _poolsById = _lootTables.DropPools.ToDictionary(p => p.Id, StringComparer.Ordinal);
        _monsterDropsById = _lootTables.MonsterDrops.ToDictionary(m => m.MonsterId, StringComparer.Ordinal);
        _tierCurvesByTier = _lootTables.TierCurves.ToDictionary(c => c.Tier);
        _zones = LoadZones();
        _zonesById = _zones.ToDictionary(z => z.ZoneId, StringComparer.Ordinal);
        _zonesByName = _zones.ToDictionary(z => z.Name, StringComparer.Ordinal);
        _zonesByWorldNumber = _zones.ToDictionary(z => (z.World, z.ZoneNumber));
        // Factions load AFTER zones so cross-ref validation (hqZoneId /
        // waypointZoneIds) can run against the authored zones registry.
        _factions = LoadFactions();
        _factionsById = _factions.ToDictionary(f => f.Id);
        _combatCurves = LoadCombatCurves();
        _progressionCurves = LoadProgressionCurves();
        FirstMud.Domain.Configuration.ProgressionCurvesAccessor.Publish(
            _progressionCurves.Workmanship.SkillDivisor,
            _progressionCurves.CompanionLayerThresholds);

        // NPCs load AFTER zones + factions so cross-ref validation
        // (homeZoneId / factionId) can run against the authored registries.
        // Stack NPCs before quests/events so downstream cross-ref checks see
        // the NPC name set.
        _npcs = LoadNpcs();
        _npcsById = _npcs.ToDictionary(n => n.Id, StringComparer.Ordinal);

        // Quests load AFTER zones so startingZoneId cross-ref validation can
        // run against the authored zones registry.
        var (quests, edges) = LoadQuests();
        _quests = quests;
        _questsById = _quests.ToDictionary(q => q.QuestId, StringComparer.Ordinal);
        _questEdges = edges;

        // World-events load AFTER zones / factions / quests so cross-ref
        // validation against those registries can run.
        _events = LoadWorldEvents();
        _eventsById = _events.ToDictionary(e => e.Id, StringComparer.Ordinal);

        // Trade layer — depends on NPCs and zones being loaded so vendor
        // cross-refs + zone-number resolution can validate.
        _itemValues = LoadItemValues();
        _tradeCurves = LoadTradeCurves();
        _vendors = LoadVendors();
        _vendorsByNpcId = _vendors.ToDictionary(v => v.NpcId, StringComparer.Ordinal);

        // Publish the static accessor for the few legacy static call sites
        // (ZoneGridLayout / BiomeService) that cannot easily take DI.
        ContentAccessor.Publish(this);

        _logger?.LogInformation(
            "ContentProvider loaded: {ConsumableCount} consumables, {BuildingCount} buildings, {RecipeCount} recipes, {MonsterCount} monsters, {PoolCount} drop pools, {MonsterDropCount} monster drops, {ZoneCount} zones, {FactionCount} factions, {NpcCount} npcs, {QuestCount} quests, {EdgeCount} quest edges, {EventCount} world events from {Root}",
            _consumables.Count, _buildings.Count, _recipes.Count, _monsters.Count, _lootTables.DropPools.Count, _lootTables.MonsterDrops.Count, _zones.Count, _factions.Count, _npcs.Count, _quests.Count, _questEdges.Count, _events.Count, _contentRoot);
    }

    private IReadOnlyList<ZoneDefinition> LoadZones()
    {
        var path = Path.Combine(_contentRoot, "zones.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ZonesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Zones is null || doc.Zones.Count == 0)
            throw new InvalidDataException($"{path}: no zones defined.");

        // Gather known monster ids once (tolerate absence: monsters.json is
        // not yet merged in this base — spawn-id validation is skipped when
        // the file is missing).
        HashSet<string>? knownMonsterIds = TryLoadKnownMonsterIds();

        var list = new List<ZoneDefinition>(doc.Zones.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var seenWorldNumber = new HashSet<(WorldId, int)>();

        foreach (var raw in doc.Zones)
        {
            if (string.IsNullOrWhiteSpace(raw.ZoneId))
                throw new InvalidDataException($"{path}: zone missing zoneId.");
            if (!seenIds.Add(raw.ZoneId))
                throw new InvalidDataException($"{path}: duplicate zoneId '{raw.ZoneId}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: zone '{raw.ZoneId}' missing name.");
            if (string.IsNullOrWhiteSpace(raw.AsciiSymbol) || raw.AsciiSymbol.Length != 1)
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' asciiSymbol must be exactly 1 character.");

            if (string.IsNullOrWhiteSpace(raw.World) || !ValidWorldIdNames.Contains(raw.World))
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' has invalid world '{raw.World}'.");
            var world = Enum.Parse<WorldId>(raw.World);

            if (raw.ZoneNumber < 1)
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' zoneNumber must be >= 1.");
            if (!seenWorldNumber.Add((world, raw.ZoneNumber)))
                throw new InvalidDataException(
                    $"{path}: duplicate (world, zoneNumber) pair for '{raw.ZoneId}'.");

            if (string.IsNullOrWhiteSpace(raw.Biome) || !ValidBiomeNames.Contains(raw.Biome))
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' has invalid biome '{raw.Biome}'.");

            if (raw.DangerLevel < 1 || raw.DangerLevel > 10)
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' dangerLevel must be between 1 and 10.");

            WorldId? portalDest = null;
            if (!string.IsNullOrWhiteSpace(raw.PortalDestination))
            {
                if (!ValidWorldIdNames.Contains(raw.PortalDestination))
                    throw new InvalidDataException(
                        $"{path}: zone '{raw.ZoneId}' has invalid portalDestination '{raw.PortalDestination}'.");
                portalDest = Enum.Parse<WorldId>(raw.PortalDestination);
            }

            if (raw.Layout is null)
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' missing layout.");
            if (raw.Layout.X < 0 || raw.Layout.Y < 0)
                throw new InvalidDataException(
                    $"{path}: zone '{raw.ZoneId}' layout coordinates must be >= 0.");

            ResourceType? primary = ParseOptionalResource(raw.PrimaryResource, raw.ZoneId, nameof(raw.PrimaryResource), path);
            ResourceType? secondary = ParseOptionalResource(raw.SecondaryResource, raw.ZoneId, nameof(raw.SecondaryResource), path);

            var spawns = new List<ZoneMonsterSpawnDefinition>();
            if (raw.MonsterSpawns is not null)
            {
                foreach (var s in raw.MonsterSpawns)
                {
                    if (string.IsNullOrWhiteSpace(s.MonsterId))
                        throw new InvalidDataException(
                            $"{path}: zone '{raw.ZoneId}' has monsterSpawn with empty monsterId.");
                    if (knownMonsterIds is not null && !knownMonsterIds.Contains(s.MonsterId))
                        throw new InvalidDataException(
                            $"{path}: zone '{raw.ZoneId}' references unknown monsterId '{s.MonsterId}'.");
                    spawns.Add(new ZoneMonsterSpawnDefinition(s.MonsterId, s.Weight <= 0 ? 1 : s.Weight));
                }
            }

            list.Add(new ZoneDefinition(
                ZoneId: raw.ZoneId,
                World: world,
                ZoneNumber: raw.ZoneNumber,
                Name: raw.Name,
                Description: raw.Description ?? "",
                AsciiSymbol: raw.AsciiSymbol,
                Biome: raw.Biome,
                DangerLevel: raw.DangerLevel,
                IsPortalZone: raw.IsPortalZone,
                PortalDestination: portalDest,
                IsStartingZone: raw.IsStartingZone,
                Layout: new ZoneLayoutDefinition(raw.Layout.X, raw.Layout.Y),
                PrimaryResource: primary,
                SecondaryResource: secondary,
                MonsterSpawns: spawns));
        }

        return list;
    }

    private static ResourceType? ParseOptionalResource(string? raw, string zoneId, string fieldName, string path)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!Enum.TryParse<ResourceType>(raw, ignoreCase: false, out var rt))
            throw new InvalidDataException(
                $"{path}: zone '{zoneId}' has invalid {fieldName} '{raw}'.");
        return rt;
    }

    private HashSet<string>? TryLoadKnownMonsterIds()
    {
        var path = Path.Combine(_contentRoot, "monsters.json");
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("monsters", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return null;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var el in arr.EnumerateArray())
            {
                if (el.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                    ids.Add(id.GetString()!);
                else if (el.TryGetProperty("monsterId", out var mid) && mid.ValueKind == JsonValueKind.String)
                    ids.Add(mid.GetString()!);
            }
            return ids;
        }
        catch
        {
            return null;
        }
    }

    private IReadOnlyList<FactionDefinition> LoadFactions()
    {
        var path = Path.Combine(_contentRoot, "factions.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<FactionsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Factions is null || doc.Factions.Count == 0)
            throw new InvalidDataException($"{path}: no factions defined.");

        // Optional cross-ref: if zones.json is present, validate hqZoneId and
        // waypointZoneIds against the authored zone ids. When zones.json is
        // absent (earlier on the migration chain) this check is skipped —
        // factions still load, they just aren't zone-validated.
        HashSet<string>? knownZoneIds = TryLoadKnownZoneIds();

        var list = new List<FactionDefinition>(doc.Factions.Count);
        var seenIds = new HashSet<FactionId>();
        // First pass: parse + structural validation so we can validate
        // hostileTo cross-refs against the full id set in a second pass.
        var raws = new List<(FactionDefinition def, IReadOnlyList<string> hostileRaw)>(doc.Factions.Count);

        foreach (var raw in doc.Factions)
        {
            if (string.IsNullOrWhiteSpace(raw.Id) || !ValidFactionIdNames.Contains(raw.Id))
                throw new InvalidDataException(
                    $"{path}: faction has invalid or missing id '{raw.Id}'. Must match a FactionId enum name.");
            var id = Enum.Parse<FactionId>(raw.Id);
            if (!seenIds.Add(id))
                throw new InvalidDataException($"{path}: duplicate faction id '{raw.Id}'.");

            if (string.IsNullOrWhiteSpace(raw.DisplayName))
                throw new InvalidDataException($"{path}: faction '{raw.Id}' missing displayName.");
            if (string.IsNullOrWhiteSpace(raw.Description))
                throw new InvalidDataException($"{path}: faction '{raw.Id}' missing description.");
            if (string.IsNullOrWhiteSpace(raw.HqZoneId))
                throw new InvalidDataException($"{path}: faction '{raw.Id}' missing hqZoneId.");

            if (knownZoneIds is not null && !knownZoneIds.Contains(raw.HqZoneId))
                throw new InvalidDataException(
                    $"{path}: faction '{raw.Id}' hqZoneId '{raw.HqZoneId}' is not a known zoneId.");

            if (raw.WaypointZoneIds is null || raw.WaypointZoneIds.Count == 0)
                throw new InvalidDataException(
                    $"{path}: faction '{raw.Id}' must declare at least one waypointZoneId.");

            foreach (var wpZone in raw.WaypointZoneIds)
            {
                if (string.IsNullOrWhiteSpace(wpZone))
                    throw new InvalidDataException(
                        $"{path}: faction '{raw.Id}' has empty waypointZoneId entry.");
                if (knownZoneIds is not null && !knownZoneIds.Contains(wpZone))
                    throw new InvalidDataException(
                        $"{path}: faction '{raw.Id}' waypointZoneId '{wpZone}' is not a known zoneId.");
            }

            FactionWaypoint? waypoint = null;
            if (raw.Waypoint is not null)
            {
                if (raw.Waypoint.X < 0 || raw.Waypoint.Y < 0)
                    throw new InvalidDataException(
                        $"{path}: faction '{raw.Id}' waypoint coords must be >= 0.");
                waypoint = new FactionWaypoint(raw.Waypoint.X, raw.Waypoint.Y);
            }

            IReadOnlyList<string> hostileRaw = raw.HostileTo is null
                ? Array.Empty<string>()
                : raw.HostileTo;

            var def = new FactionDefinition(
                Id: id,
                DisplayName: raw.DisplayName,
                Description: raw.Description,
                HqZoneId: raw.HqZoneId,
                HqDisplayName: string.IsNullOrWhiteSpace(raw.HqDisplayName) ? null : raw.HqDisplayName,
                HostileTo: Array.Empty<FactionId>(), // filled in pass 2
                StartingReputation: raw.StartingReputation,
                Hidden: raw.Hidden,
                WaypointZoneIds: raw.WaypointZoneIds.ToList(),
                Waypoint: waypoint);

            raws.Add((def, hostileRaw));
            list.Add(def);
        }

        // Enum-completeness check: every FactionId must have a definition
        // unless we explicitly add an opt-out in the future. Catches the
        // "added a new faction to the enum and forgot to author it" bug.
        foreach (FactionId expected in Enum.GetValues<FactionId>())
        {
            if (!seenIds.Contains(expected))
                throw new InvalidDataException(
                    $"{path}: FactionId.{expected} has no entry in factions.json.");
        }

        // Second pass: resolve hostileTo cross-refs now that the full id set
        // is known. Rebuild the FactionDefinition with the populated list.
        var finalList = new List<FactionDefinition>(list.Count);
        foreach (var (def, hostileRaw) in raws)
        {
            var hostileIds = new List<FactionId>(hostileRaw.Count);
            var seenHostile = new HashSet<FactionId>();
            foreach (var h in hostileRaw)
            {
                if (string.IsNullOrWhiteSpace(h) || !ValidFactionIdNames.Contains(h))
                    throw new InvalidDataException(
                        $"{path}: faction '{def.Id}' hostileTo entry '{h}' is not a valid FactionId.");
                var hid = Enum.Parse<FactionId>(h);
                if (hid == def.Id)
                    throw new InvalidDataException(
                        $"{path}: faction '{def.Id}' cannot be hostile to itself.");
                if (!seenHostile.Add(hid))
                    throw new InvalidDataException(
                        $"{path}: faction '{def.Id}' has duplicate hostileTo entry '{h}'.");
                hostileIds.Add(hid);
            }
            finalList.Add(def with { HostileTo = hostileIds });
        }

        return finalList;
    }

    /// <summary>
    /// Returns the set of zone ids from <c>zones.json</c> if the file exists
    /// at the content root, else null. Lets faction validation cross-check
    /// hqZoneId / waypointZoneIds without hard-requiring zones.json on bases
    /// where the zones migration hasn't landed yet.
    /// </summary>
    private HashSet<string>? TryLoadKnownZoneIds()
    {
        var path = Path.Combine(_contentRoot, "zones.json");
        if (!File.Exists(path)) return null;

        try
        {
            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("zones", out var zonesEl) ||
                zonesEl.ValueKind != JsonValueKind.Array)
                return null;

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var z in zonesEl.EnumerateArray())
            {
                if (z.TryGetProperty("zoneId", out var zid) &&
                    zid.ValueKind == JsonValueKind.String)
                {
                    var s = zid.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) ids.Add(s!);
                }
            }
            return ids.Count == 0 ? null : ids;
        }
        catch
        {
            // Malformed zones.json is a zone-migration concern; don't block
            // faction loading on it.
            return null;
        }
    }

    // ─── Combat Curves ───────────────────────────────────────────────────────

    private static CombatCurvesDefinition DefaultCombatCurves() =>
        new(
            new MonsterScalingCurve(
                HpPerDanger:      0.20,
                PowerPerDanger:   0.15,
                SpeedPerDanger:   0.7,
                BossHpMultiplier: 1.6,
                BossSpeedBonus:   4),
            new PartyScalingCurve(ScalingPerTier: 0.12));

    /// <summary>
    /// Loads <c>content/combat-curves.json</c> if present. The file is
    /// optional — if absent, the historical hardcoded constants are used so
    /// existing test fixtures and partial-content roots keep working. When
    /// the file IS present, every field is required and must fall in a
    /// sensible range (validated against <c>combat-curves.schema.json</c>).
    /// </summary>
    private CombatCurvesDefinition LoadCombatCurves()
    {
        var path = Path.Combine(_contentRoot, "combat-curves.json");
        if (!File.Exists(path))
            return DefaultCombatCurves();

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<CombatCurvesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.MonsterScaling is null)
            throw new InvalidDataException($"{path}: 'monsterScaling' block is required.");

        var ms = doc.MonsterScaling;

        ValidateRange(path, "hpPerDanger",      ms.HpPerDanger,      min: 0, max: 5);
        ValidateRange(path, "powerPerDanger",   ms.PowerPerDanger,   min: 0, max: 5);
        ValidateRange(path, "speedPerDanger",   ms.SpeedPerDanger,   min: 0, max: 10);
        ValidateRange(path, "bossHpMultiplier", ms.BossHpMultiplier, min: 1, max: 10);
        if (ms.BossSpeedBonus < 0 || ms.BossSpeedBonus > 50)
            throw new InvalidDataException(
                $"{path}: monsterScaling.bossSpeedBonus must be in [0, 50] (got {ms.BossSpeedBonus}).");

        // Party scaling block is optional. Default to 0.12 so legacy files
        // without the block still get the symmetric party buff that pairs
        // with post-TPK-fix monster scaling.
        double partyScalingPerTier = 0.12;
        if (doc.PartyScaling is not null)
        {
            ValidateRange(path, "scalingPerTier", doc.PartyScaling.ScalingPerTier, min: 0, max: 1);
            partyScalingPerTier = doc.PartyScaling.ScalingPerTier;
        }

        return new CombatCurvesDefinition(
            new MonsterScalingCurve(
                HpPerDanger:      ms.HpPerDanger,
                PowerPerDanger:   ms.PowerPerDanger,
                SpeedPerDanger:   ms.SpeedPerDanger,
                BossHpMultiplier: ms.BossHpMultiplier,
                BossSpeedBonus:   ms.BossSpeedBonus),
            new PartyScalingCurve(partyScalingPerTier));
    }

    private static void ValidateRange(string path, string field, double value, double min, double max)
    {
        if (value < min || value > max)
            throw new InvalidDataException(
                $"{path}: monsterScaling.{field} must be in [{min}, {max}] (got {value}).");
    }

    // ─── Progression Curves ──────────────────────────────────────────────────

    private static ProgressionCurvesDefinition DefaultProgressionCurves() =>
        new(
            new WorkmanshipCurve(SkillDivisor: FirstMud.Domain.Configuration.ProgressionCurvesAccessor.DefaultSkillDivisor),
            FirstMud.Domain.Configuration.ProgressionCurvesAccessor.DefaultCompanionLayerThresholds);

    /// <summary>
    /// Loads <c>content/progression-curves.json</c> if present. The file is
    /// optional — if absent, the historical hardcoded constants are used so
    /// existing test fixtures and partial-content roots keep working. Mirrors
    /// the <c>combat-curves.json</c> pattern.
    /// </summary>
    private ProgressionCurvesDefinition LoadProgressionCurves()
    {
        var path = Path.Combine(_contentRoot, "progression-curves.json");
        if (!File.Exists(path))
            return DefaultProgressionCurves();

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ProgressionCurvesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        // Workmanship — required when file exists.
        if (doc.Workmanship is null)
            throw new InvalidDataException($"{path}: 'workmanship' block is required.");
        var divisor = doc.Workmanship.SkillDivisor;
        if (divisor < 1 || divisor > 100)
            throw new InvalidDataException(
                $"{path}: workmanship.skillDivisor must be in [1, 100] (got {divisor}).");

        // Companion layer thresholds — required, all 5 enum values must be present.
        if (doc.CompanionLayerThresholds is null || doc.CompanionLayerThresholds.Count == 0)
            throw new InvalidDataException(
                $"{path}: 'companionLayerThresholds' must contain an entry per CompanionType.");

        var thresholds = new Dictionary<CompanionType, IReadOnlyList<int>>();
        foreach (var (typeName, arr) in doc.CompanionLayerThresholds)
        {
            if (!Enum.TryParse<CompanionType>(typeName, ignoreCase: false, out var type))
                throw new InvalidDataException(
                    $"{path}: companionLayerThresholds key '{typeName}' is not a valid CompanionType.");
            if (arr is null || arr.Count != 6)
                throw new InvalidDataException(
                    $"{path}: companionLayerThresholds[{typeName}] must have exactly 6 entries (got {arr?.Count ?? 0}).");
            for (int i = 0; i < arr.Count; i++)
            {
                if (arr[i] < 0)
                    throw new InvalidDataException(
                        $"{path}: companionLayerThresholds[{typeName}][{i}] must be >= 0 (got {arr[i]}).");
                if (i > 0 && arr[i] < arr[i - 1])
                    throw new InvalidDataException(
                        $"{path}: companionLayerThresholds[{typeName}] must be non-decreasing.");
            }
            thresholds[type] = arr;
        }
        foreach (var t in Enum.GetValues<CompanionType>())
        {
            if (!thresholds.ContainsKey(t))
                throw new InvalidDataException(
                    $"{path}: companionLayerThresholds is missing entry for CompanionType '{t}'.");
        }

        return new ProgressionCurvesDefinition(
            new WorkmanshipCurve(divisor),
            thresholds);
    }

    private sealed record ProgressionCurvesFile(
        [property: JsonPropertyName("workmanship")] WorkmanshipRaw? Workmanship,
        [property: JsonPropertyName("companionLayerThresholds")] Dictionary<string, List<int>>? CompanionLayerThresholds);

    private sealed record WorkmanshipRaw(
        [property: JsonPropertyName("skillDivisor")] int SkillDivisor);

    private sealed record CombatCurvesFile(
        [property: JsonPropertyName("monsterScaling")] MonsterScalingRaw? MonsterScaling,
        [property: JsonPropertyName("partyScaling")]   PartyScalingRaw?   PartyScaling);

    private sealed record PartyScalingRaw(
        [property: JsonPropertyName("scalingPerTier")] double ScalingPerTier);

    private sealed record MonsterScalingRaw(
        [property: JsonPropertyName("hpPerDanger")]      double HpPerDanger,
        [property: JsonPropertyName("powerPerDanger")]   double PowerPerDanger,
        [property: JsonPropertyName("speedPerDanger")]   double SpeedPerDanger,
        [property: JsonPropertyName("bossHpMultiplier")] double BossHpMultiplier,
        [property: JsonPropertyName("bossSpeedBonus")]   int    BossSpeedBonus);

    private sealed record FactionsFile(
        [property: JsonPropertyName("factions")] List<FactionRaw>? Factions);

    private sealed record FactionRaw(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("hqZoneId")] string? HqZoneId,
        [property: JsonPropertyName("hqDisplayName")] string? HqDisplayName,
        [property: JsonPropertyName("hostileTo")] List<string>? HostileTo,
        [property: JsonPropertyName("startingReputation")] int StartingReputation,
        [property: JsonPropertyName("hidden")] bool Hidden,
        [property: JsonPropertyName("waypointZoneIds")] List<string>? WaypointZoneIds,
        [property: JsonPropertyName("waypoint")] FactionWaypointRaw? Waypoint);

    private sealed record FactionWaypointRaw(
        [property: JsonPropertyName("x")] int X,
        [property: JsonPropertyName("y")] int Y);

    private IReadOnlyList<NpcDefinition> LoadNpcs()
    {
        var path = Path.Combine(_contentRoot, "npcs.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<NpcsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Npcs is null || doc.Npcs.Count == 0)
            throw new InvalidDataException($"{path}: no npcs defined.");

        var knownZoneIds = new HashSet<string>(_zones.Select(z => z.ZoneId), StringComparer.Ordinal);
        var knownFactionIds = new HashSet<FactionId>(_factions.Select(f => f.Id));

        var list = new List<NpcDefinition>(doc.Npcs.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Npcs)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: npc missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate npc id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.DisplayName))
                throw new InvalidDataException($"{path}: npc '{raw.Id}' missing displayName.");
            if (string.IsNullOrWhiteSpace(raw.HomeZoneId))
                throw new InvalidDataException($"{path}: npc '{raw.Id}' missing homeZoneId.");
            if (!knownZoneIds.Contains(raw.HomeZoneId))
                throw new InvalidDataException(
                    $"{path}: npc '{raw.Id}' homeZoneId '{raw.HomeZoneId}' is not a known zoneId.");
            if (string.IsNullOrWhiteSpace(raw.Role) || !ValidNpcRoleNames.Contains(raw.Role))
                throw new InvalidDataException(
                    $"{path}: npc '{raw.Id}' has invalid role '{raw.Role}'. Must be one of: {string.Join(", ", Enum.GetNames<NpcRole>())}.");
            var role = Enum.Parse<NpcRole>(raw.Role, ignoreCase: true);
            if (string.IsNullOrWhiteSpace(raw.ShortDescription))
                throw new InvalidDataException($"{path}: npc '{raw.Id}' missing shortDescription.");

            FactionId? factionId = null;
            if (!string.IsNullOrWhiteSpace(raw.FactionId))
            {
                if (!ValidFactionIdNames.Contains(raw.FactionId))
                    throw new InvalidDataException(
                        $"{path}: npc '{raw.Id}' has invalid factionId '{raw.FactionId}'.");
                var fid = Enum.Parse<FactionId>(raw.FactionId);
                if (!knownFactionIds.Contains(fid))
                    throw new InvalidDataException(
                        $"{path}: npc '{raw.Id}' factionId '{raw.FactionId}' is not present in factions.json.");
                factionId = fid;
            }

            if (raw.VoiceTells is null || raw.VoiceTells.Count < 3 || raw.VoiceTells.Count > 5)
                throw new InvalidDataException(
                    $"{path}: npc '{raw.Id}' must declare between 3 and 5 voiceTells (got {raw.VoiceTells?.Count ?? 0}).");
            foreach (var tell in raw.VoiceTells)
            {
                if (string.IsNullOrWhiteSpace(tell))
                    throw new InvalidDataException(
                        $"{path}: npc '{raw.Id}' has empty voiceTell entry.");
            }

            // dialogueRootId is optional; the schema constrains it to a single
            // string so "at most one per NPC" is structurally enforced. We
            // additionally trim/validate non-empty when present.
            string? dialogueRoot = null;
            if (raw.DialogueRootId is not null)
            {
                if (string.IsNullOrWhiteSpace(raw.DialogueRootId))
                    throw new InvalidDataException(
                        $"{path}: npc '{raw.Id}' has empty dialogueRootId.");
                dialogueRoot = raw.DialogueRootId;
            }

            list.Add(new NpcDefinition(
                Id: raw.Id,
                DisplayName: raw.DisplayName,
                FactionId: factionId,
                HomeZoneId: raw.HomeZoneId,
                Role: role,
                ShortDescription: raw.ShortDescription,
                VoiceTells: raw.VoiceTells.ToList(),
                DialogueRootId: dialogueRoot,
                StartingReputation: raw.StartingReputation));
        }

        return list;
    }

    private sealed record NpcsFile(
        [property: JsonPropertyName("npcs")] List<NpcRaw>? Npcs);

    private sealed record NpcRaw(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("displayName")] string? DisplayName,
        [property: JsonPropertyName("factionId")] string? FactionId,
        [property: JsonPropertyName("homeZoneId")] string? HomeZoneId,
        [property: JsonPropertyName("role")] string? Role,
        [property: JsonPropertyName("shortDescription")] string? ShortDescription,
        [property: JsonPropertyName("voiceTells")] List<string>? VoiceTells,
        [property: JsonPropertyName("dialogueRootId")] string? DialogueRootId,
        [property: JsonPropertyName("startingReputation")] int? StartingReputation);

    private IReadOnlyList<RecipeDefinition> LoadRecipes()
    {
        var path = Path.Combine(_contentRoot, "recipes.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<RecipesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Recipes is null || doc.Recipes.Count == 0)
            throw new InvalidDataException($"{path}: no recipes defined.");

        var list = new List<RecipeDefinition>(doc.Recipes.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Recipes)
        {
            if (string.IsNullOrWhiteSpace(raw.RecipeId))
                throw new InvalidDataException($"{path}: recipe missing recipeId.");
            if (!seenIds.Add(raw.RecipeId))
                throw new InvalidDataException($"{path}: duplicate recipeId '{raw.RecipeId}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: recipe '{raw.RecipeId}' missing name.");
            if (string.IsNullOrWhiteSpace(raw.ResultItemName))
                throw new InvalidDataException($"{path}: recipe '{raw.RecipeId}' missing resultItemName.");

            if (string.IsNullOrWhiteSpace(raw.ResultCategory) || !ValidItemCategoryNames.Contains(raw.ResultCategory))
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' has invalid resultCategory '{raw.ResultCategory}'.");
            var resultCategory = Enum.Parse<ItemCategory>(raw.ResultCategory);

            if (string.IsNullOrWhiteSpace(raw.RequiredWorld) || !ValidWorldIdNames.Contains(raw.RequiredWorld))
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' has invalid requiredWorld '{raw.RequiredWorld}'.");
            var requiredWorld = Enum.Parse<WorldId>(raw.RequiredWorld);

            TaperType? requiredTaperType = null;
            if (!string.IsNullOrWhiteSpace(raw.RequiredTaperType))
            {
                if (!ValidTaperTypeNames.Contains(raw.RequiredTaperType))
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' has invalid requiredTaperType '{raw.RequiredTaperType}'.");
                requiredTaperType = Enum.Parse<TaperType>(raw.RequiredTaperType);
            }

            if (raw.BaseWorkmanshipMin < 1 || raw.BaseWorkmanshipMin > 10)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' baseWorkmanshipMin must be between 1 and 10.");
            if (raw.BaseWorkmanshipMax < 1 || raw.BaseWorkmanshipMax > 10)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' baseWorkmanshipMax must be between 1 and 10.");
            if (raw.BaseWorkmanshipMin > raw.BaseWorkmanshipMax)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' baseWorkmanshipMin must not exceed baseWorkmanshipMax.");

            if (raw.RequiredCraftingSkill < 0)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' requiredCraftingSkill must be >= 0.");

            if (raw.Ingredients is null || raw.Ingredients.Count == 0)
                throw new InvalidDataException(
                    $"{path}: recipe '{raw.RecipeId}' has no ingredients.");

            var ingredients = new List<RecipeIngredientDefinition>(raw.Ingredients.Count);
            foreach (var ing in raw.Ingredients)
            {
                if (string.IsNullOrWhiteSpace(ing.Name))
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' has ingredient with empty name.");
                if (string.IsNullOrWhiteSpace(ing.Category) || !ValidItemCategoryNames.Contains(ing.Category))
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' ingredient '{ing.Name}' has invalid category '{ing.Category}'.");
                if (ing.BaseQuantity <= 0)
                    throw new InvalidDataException(
                        $"{path}: recipe '{raw.RecipeId}' ingredient '{ing.Name}' baseQuantity must be > 0.");
                ingredients.Add(new RecipeIngredientDefinition(
                    Enum.Parse<ItemCategory>(ing.Category), ing.Name, ing.BaseQuantity));
            }

            list.Add(new RecipeDefinition(
                RecipeId: raw.RecipeId,
                Name: raw.Name,
                ResultItemName: raw.ResultItemName,
                ResultCategory: resultCategory,
                RequiredCraftingSkill: raw.RequiredCraftingSkill,
                RequiredWorld: requiredWorld,
                RequiredTaperType: requiredTaperType,
                BaseWorkmanshipMin: raw.BaseWorkmanshipMin,
                BaseWorkmanshipMax: raw.BaseWorkmanshipMax,
                IsDiscoverable: raw.IsDiscoverable,
                Ingredients: ingredients));
        }

        return list;
    }

    private IReadOnlyList<BuildingDefinition> LoadBuildings()
    {
        var path = Path.Combine(_contentRoot, "buildings.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<BuildingsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Buildings is null || doc.Buildings.Count == 0)
            throw new InvalidDataException($"{path}: no buildings defined.");

        var list = new List<BuildingDefinition>(doc.Buildings.Count);
        var seenTypes = new HashSet<BuildingType>();

        foreach (var raw in doc.Buildings)
        {
            if (string.IsNullOrWhiteSpace(raw.Type))
                throw new InvalidDataException($"{path}: building missing type.");

            if (!Enum.TryParse<BuildingType>(raw.Type, ignoreCase: false, out var type))
                throw new InvalidDataException(
                    $"{path}: unknown building type '{raw.Type}'.");

            if (!seenTypes.Add(type))
                throw new InvalidDataException($"{path}: duplicate building type '{raw.Type}'.");

            if (raw.ConstructionCost is null || raw.ConstructionCost.Count == 0)
                throw new InvalidDataException(
                    $"{path}: building '{raw.Type}' has no constructionCost entries.");

            var costs = new List<BuildingCost>(raw.ConstructionCost.Count);
            foreach (var c in raw.ConstructionCost)
            {
                if (string.IsNullOrWhiteSpace(c.Material))
                    throw new InvalidDataException(
                        $"{path}: building '{raw.Type}' has a cost entry with no material.");
                if (c.Quantity <= 0)
                    throw new InvalidDataException(
                        $"{path}: building '{raw.Type}' cost '{c.Material}' must have quantity > 0.");
                costs.Add(new BuildingCost(c.Material, c.Quantity));
            }

            HomesteadDuty? duty = null;
            if (!string.IsNullOrWhiteSpace(raw.Duty))
            {
                if (!ValidDutyNames.Contains(raw.Duty))
                    throw new InvalidDataException(
                        $"{path}: building '{raw.Type}' has invalid duty '{raw.Duty}'. " +
                        $"Must be one of: {string.Join(", ", ValidDutyNames)}.");
                duty = Enum.Parse<HomesteadDuty>(raw.Duty);
            }

            if (raw.WorkerCapacity < 0)
                throw new InvalidDataException(
                    $"{path}: building '{raw.Type}' workerCapacity must be >= 0.");

            IReadOnlyList<int>? hutCapacity = null;
            if (raw.IsHousing)
            {
                if (raw.HutCapacityByTier is null || raw.HutCapacityByTier.Count == 0)
                    throw new InvalidDataException(
                        $"{path}: housing building '{raw.Type}' requires a non-empty hutCapacityByTier array.");
                foreach (var cap in raw.HutCapacityByTier)
                {
                    if (cap <= 0)
                        throw new InvalidDataException(
                            $"{path}: housing building '{raw.Type}' has invalid hutCapacityByTier entry {cap} (must be > 0).");
                }
                hutCapacity = raw.HutCapacityByTier.ToList();

                if (duty is not null)
                    throw new InvalidDataException(
                        $"{path}: housing building '{raw.Type}' must not specify a duty.");
                if (raw.WorkerCapacity != 0)
                    throw new InvalidDataException(
                        $"{path}: housing building '{raw.Type}' must have workerCapacity = 0.");
            }
            else
            {
                if (duty is null)
                    throw new InvalidDataException(
                        $"{path}: production building '{raw.Type}' must specify a duty.");
                if (raw.HutCapacityByTier is not null)
                    throw new InvalidDataException(
                        $"{path}: non-housing building '{raw.Type}' must not specify hutCapacityByTier.");
            }

            list.Add(new BuildingDefinition(
                Type: type,
                ConstructionCost: costs,
                Duty: duty,
                WorkerCapacity: raw.WorkerCapacity,
                IsHousing: raw.IsHousing,
                HutCapacityByTier: hutCapacity));
        }

        // Every BuildingType enum value must be present — this is the whole
        // point of startup validation: misconfigured content cannot reach
        // production silently.
        foreach (var enumValue in Enum.GetValues<BuildingType>())
        {
            if (!seenTypes.Contains(enumValue))
                throw new InvalidDataException(
                    $"{path}: missing definition for BuildingType.{enumValue}.");
        }

        return list;
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

    private sealed class BuildingsFile
    {
        [JsonPropertyName("buildings")]
        public List<RawBuilding>? Buildings { get; set; }
    }

    private sealed class RawBuilding
    {
        public string Type { get; set; } = "";
        public List<RawBuildingCost>? ConstructionCost { get; set; }
        public string? Duty { get; set; }
        public int WorkerCapacity { get; set; }
        public bool IsHousing { get; set; }
        public List<int>? HutCapacityByTier { get; set; }
    }

    private sealed class RawBuildingCost
    {
        public string Material { get; set; } = "";
        public int Quantity { get; set; }
    }

    private sealed class RecipesFile
    {
        [JsonPropertyName("recipes")]
        public List<RawRecipe>? Recipes { get; set; }
    }

    private sealed class RawRecipe
    {
        public string RecipeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string ResultItemName { get; set; } = "";
        public string ResultCategory { get; set; } = "";
        public int RequiredCraftingSkill { get; set; }
        public string RequiredWorld { get; set; } = "";
        public string? RequiredTaperType { get; set; }
        public int BaseWorkmanshipMin { get; set; }
        public int BaseWorkmanshipMax { get; set; }
        public bool IsDiscoverable { get; set; }
        public List<RawRecipeIngredient>? Ingredients { get; set; }
    }

    private sealed class RawRecipeIngredient
    {
        public string Category { get; set; } = "";
        public string Name { get; set; } = "";
        public int BaseQuantity { get; set; }
    }

    // ─── Monsters ────────────────────────────────────────────────────────────

    private IReadOnlyList<MonsterDefinition> LoadMonsters()
    {
        var path = Path.Combine(_contentRoot, "monsters.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<MonstersFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Abilities is null || doc.Abilities.Count == 0)
            throw new InvalidDataException($"{path}: no abilities defined.");
        if (doc.Monsters is null || doc.Monsters.Count == 0)
            throw new InvalidDataException($"{path}: no monsters defined.");

        // Build ability table first — monsters reference abilities by id.
        var abilityTable = new Dictionary<string, CombatAbility>(StringComparer.Ordinal);
        foreach (var raw in doc.Abilities)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: ability missing id.");
            if (abilityTable.ContainsKey(raw.Id))
                throw new InvalidDataException($"{path}: duplicate ability id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: ability '{raw.Id}' missing name.");
            if (raw.BasePower < 0)
                throw new InvalidDataException($"{path}: ability '{raw.Id}' has negative basePower.");
            if (raw.WeaveCost < 0)
                throw new InvalidDataException($"{path}: ability '{raw.Id}' has negative weaveCost.");
            if (!Enum.TryParse<MagicElement>(raw.Element, ignoreCase: false, out var element))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid element '{raw.Element}'.");
            if (!Enum.TryParse<AbilityTargetType>(raw.TargetType, ignoreCase: false, out var target))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid targetType '{raw.TargetType}'.");
            if (!Enum.TryParse<AbilityCategory>(raw.Category, ignoreCase: false, out var category))
                throw new InvalidDataException(
                    $"{path}: ability '{raw.Id}' has invalid category '{raw.Category}'.");

            abilityTable[raw.Id] = new CombatAbility(
                raw.Name, raw.BasePower, raw.WeaveCost,
                element, target, category,
                raw.LifestealPower, raw.WyrdProcChance);
        }

        var list = new List<MonsterDefinition>(doc.Monsters.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Monsters)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: monster missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate monster id '{raw.Id}'.");
            if (string.IsNullOrWhiteSpace(raw.Name))
                throw new InvalidDataException($"{path}: monster '{raw.Id}' missing name.");
            if (string.IsNullOrWhiteSpace(raw.Biome) || !ValidBiomes.Contains(raw.Biome))
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has invalid biome '{raw.Biome}'. " +
                    $"Must be one of: {string.Join(", ", ValidBiomes)}.");
            if (raw.Tier < 0 || raw.Tier > 3)
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has tier {raw.Tier} outside range 0-3.");
            if (raw.Hp < 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has negative hp.");
            if (raw.Speed < 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has negative speed.");
            if (raw.Level < 1)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has level < 1.");
            if (!Enum.TryParse<MagicElement>(raw.Element, ignoreCase: false, out var element))
                throw new InvalidDataException(
                    $"{path}: monster '{raw.Id}' has invalid element '{raw.Element}'.");
            if (raw.Abilities is null || raw.Abilities.Count == 0)
                throw new InvalidDataException($"{path}: monster '{raw.Id}' has no abilities.");

            var abilities = new List<CombatAbility>(raw.Abilities.Count);
            foreach (var abilityId in raw.Abilities)
            {
                if (!abilityTable.TryGetValue(abilityId, out var ability))
                    throw new InvalidDataException(
                        $"{path}: monster '{raw.Id}' references unknown ability '{abilityId}'.");
                abilities.Add(ability);
            }

            list.Add(new MonsterDefinition(
                Id: raw.Id,
                Name: raw.Name,
                Biome: raw.Biome,
                Tier: raw.Tier,
                Hp: raw.Hp,
                Speed: raw.Speed,
                Level: raw.Level,
                Element: element,
                Abilities: abilities));
        }

        return list;
    }

    private sealed class MonstersFile
    {
        [JsonPropertyName("abilities")]
        public List<RawAbility>? Abilities { get; set; }

        [JsonPropertyName("monsters")]
        public List<RawMonster>? Monsters { get; set; }
    }

    private sealed class RawAbility
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int BasePower { get; set; }
        public int WeaveCost { get; set; }
        public string Element { get; set; } = "";
        public string TargetType { get; set; } = "";
        public string Category { get; set; } = "";
        public float LifestealPower { get; set; }
        public float WyrdProcChance { get; set; }
    }

    private sealed class RawMonster
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Biome { get; set; } = "";
        public int Tier { get; set; }
        public int Hp { get; set; }
        public int Speed { get; set; }
        public int Level { get; set; }
        public string Element { get; set; } = "";
        public List<string>? Abilities { get; set; }
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

    private sealed class ZonesFile
    {
        [JsonPropertyName("zones")]
        public List<RawZone>? Zones { get; set; }
    }

    private sealed class RawZone
    {
        public string ZoneId { get; set; } = "";
        public string World { get; set; } = "";
        public int ZoneNumber { get; set; }
        public string Name { get; set; } = "";
        public string? Description { get; set; }
        public string AsciiSymbol { get; set; } = "";
        public string Biome { get; set; } = "";
        public int DangerLevel { get; set; }
        public bool IsPortalZone { get; set; }
        public string? PortalDestination { get; set; }
        public bool IsStartingZone { get; set; }
        public RawZoneLayout? Layout { get; set; }
        public string? PrimaryResource { get; set; }
        public string? SecondaryResource { get; set; }
        public List<RawZoneMonsterSpawn>? MonsterSpawns { get; set; }
    }

    private sealed class RawZoneLayout
    {
        public int X { get; set; }
        public int Y { get; set; }
    }

    private sealed class RawZoneMonsterSpawn
    {
        public string MonsterId { get; set; } = "";
        public int Weight { get; set; }
    }

    // ─── Quest loader ────────────────────────────────────────────────────────

    /// <summary>
    /// Loads content/quests.json and validates it. Validation is strict:
    /// unique quest ids, node ids unique within a quest, every edge refers
    /// to valid quest ids, faction + world + tier enums parse, optional
    /// startingZoneId resolves against zones.json, at least one possibleOutcome,
    /// and no unreachable intra-quest nodes. Item-name references in rewards
    /// are tolerated (warned only) since the item catalog is not yet in JSON.
    /// </summary>
    private (IReadOnlyList<QuestDefinition> Quests, IReadOnlyList<QuestEdgeDefinition> Edges) LoadQuests()
    {
        var path = Path.Combine(_contentRoot, "quests.json");
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"Required content file not found: {path}", path);

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<QuestsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Quests is null || doc.Quests.Count == 0)
            throw new InvalidDataException($"{path}: no quests defined.");

        var zoneIds = new HashSet<string>(_zonesById.Keys, StringComparer.Ordinal);
        // Optional faction cross-ref — if factions.json was loaded in a future
        // merge, validate against it; otherwise fall back to the enum names.
        // Today we always validate against the enum (factions.json is in a
        // parallel migration).

        var defs = new List<QuestDefinition>(doc.Quests.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Quests)
        {
            if (string.IsNullOrWhiteSpace(raw.QuestId))
                throw new InvalidDataException($"{path}: quest missing questId.");
            if (!seenIds.Add(raw.QuestId))
                throw new InvalidDataException($"{path}: duplicate questId '{raw.QuestId}'.");

            if (string.IsNullOrWhiteSpace(raw.Title))
                throw new InvalidDataException($"{path}: quest '{raw.QuestId}' missing title.");
            if (string.IsNullOrWhiteSpace(raw.Description))
                throw new InvalidDataException($"{path}: quest '{raw.QuestId}' missing description.");

            // Prefer the loaded factions.json name set (authoritative) when present;
            // fall back to the FactionId enum names if factions haven't loaded yet.
            var factionNameSet = _factions.Count > 0
                ? new HashSet<string>(_factions.Select(f => f.Id.ToString()), StringComparer.Ordinal)
                : ValidFactionIdNames;
            if (string.IsNullOrWhiteSpace(raw.FactionId) || !factionNameSet.Contains(raw.FactionId))
                throw new InvalidDataException(
                    $"{path}: quest '{raw.QuestId}' has invalid factionId '{raw.FactionId}'.");
            var faction = Enum.Parse<FactionId>(raw.FactionId);

            if (string.IsNullOrWhiteSpace(raw.RequiredTier) || !ValidReputationTierNames.Contains(raw.RequiredTier))
                throw new InvalidDataException(
                    $"{path}: quest '{raw.QuestId}' has invalid requiredTier '{raw.RequiredTier}'.");
            var tier = Enum.Parse<ReputationTier>(raw.RequiredTier);

            if (string.IsNullOrWhiteSpace(raw.RequiredWorld) || !ValidWorldIdNames.Contains(raw.RequiredWorld))
                throw new InvalidDataException(
                    $"{path}: quest '{raw.QuestId}' has invalid requiredWorld '{raw.RequiredWorld}'.");
            var world = Enum.Parse<WorldId>(raw.RequiredWorld);

            if (raw.ReputationReward < 0)
                throw new InvalidDataException(
                    $"{path}: quest '{raw.QuestId}' reputationReward must be >= 0.");

            if (raw.PossibleOutcomes is null || raw.PossibleOutcomes.Count == 0)
                throw new InvalidDataException(
                    $"{path}: quest '{raw.QuestId}' must declare at least one possibleOutcome.");

            foreach (var outcome in raw.PossibleOutcomes)
            {
                if (string.IsNullOrWhiteSpace(outcome))
                    throw new InvalidDataException(
                        $"{path}: quest '{raw.QuestId}' has empty possibleOutcome entry.");
            }

            if (!string.IsNullOrWhiteSpace(raw.StartingZoneId) &&
                zoneIds.Count > 0 &&
                !zoneIds.Contains(raw.StartingZoneId))
            {
                throw new InvalidDataException(
                    $"{path}: quest '{raw.QuestId}' startingZoneId '{raw.StartingZoneId}' is not a known zoneId.");
            }

            // Nodes: optional. If present, ids unique within the quest.
            var nodes = new List<QuestNodeDefinition>();
            var seenNodeIds = new HashSet<string>(StringComparer.Ordinal);
            if (raw.Nodes is not null)
            {
                foreach (var n in raw.Nodes)
                {
                    if (string.IsNullOrWhiteSpace(n.NodeId))
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' has node with empty nodeId.");
                    if (!seenNodeIds.Add(n.NodeId))
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' has duplicate nodeId '{n.NodeId}'.");
                    if (string.IsNullOrWhiteSpace(n.Type))
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' node '{n.NodeId}' missing type.");

                    nodes.Add(new QuestNodeDefinition(
                        n.NodeId, n.Type,
                        n.Content ?? "",
                        n.RequiredFlags ?? new List<string>()));
                }
            }

            // Internal edges: optional. If present, every endpoint must be
            // a declared node id within THIS quest. Flags orphan nodes too.
            var internalEdges = new List<QuestEdgeDefinition>();
            if (raw.InternalEdges is not null)
            {
                foreach (var e in raw.InternalEdges)
                {
                    if (string.IsNullOrWhiteSpace(e.From) || !seenNodeIds.Contains(e.From))
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' internal edge has unknown 'from' nodeId '{e.From}'.");
                    if (string.IsNullOrWhiteSpace(e.To) || !seenNodeIds.Contains(e.To))
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' internal edge has unknown 'to' nodeId '{e.To}'.");
                    internalEdges.Add(new QuestEdgeDefinition(
                        Kind: "internal",
                        FromQuestId: e.From,
                        ToQuestId: e.To,
                        Outcome: string.IsNullOrWhiteSpace(e.Outcome) ? null : e.Outcome));
                }

                // Orphan node check: every declared node must be reachable from
                // the first node OR be the first node itself. Since the corpus
                // is flat (no nodes), this only activates for quests that opt
                // into the node list.
                if (nodes.Count > 0)
                {
                    var reachable = new HashSet<string>(StringComparer.Ordinal) { nodes[0].NodeId };
                    bool changed;
                    do
                    {
                        changed = false;
                        foreach (var e in internalEdges)
                        {
                            if (reachable.Contains(e.FromQuestId) && reachable.Add(e.ToQuestId))
                                changed = true;
                        }
                    } while (changed);

                    foreach (var n in nodes)
                    {
                        if (!reachable.Contains(n.NodeId))
                            throw new InvalidDataException(
                                $"{path}: quest '{raw.QuestId}' has orphan/unreachable node '{n.NodeId}'.");
                    }
                }
            }

            // Rewards: forgiving. Validate structural shape only — item-name
            // cross-ref is warned, not thrown, per migration spec.
            var rewards = new List<QuestRewardDefinition>();
            if (raw.Rewards is not null)
            {
                foreach (var r in raw.Rewards)
                {
                    if (string.IsNullOrWhiteSpace(r.Kind))
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' has reward with missing kind.");
                    if (r.Quantity < 0)
                        throw new InvalidDataException(
                            $"{path}: quest '{raw.QuestId}' reward quantity must be >= 0.");
                    rewards.Add(new QuestRewardDefinition(
                        r.Kind,
                        string.IsNullOrWhiteSpace(r.ItemName) ? null : r.ItemName,
                        r.Quantity));
                }
            }

            defs.Add(new QuestDefinition(
                QuestId: raw.QuestId,
                Title: raw.Title,
                Description: raw.Description,
                FactionId: faction,
                RequiredTier: tier,
                RequiredWorld: world,
                ReputationReward: raw.ReputationReward,
                PossibleOutcomes: raw.PossibleOutcomes.ToList(),
                IsWyrdQuest: raw.IsWyrdQuest,
                StartingZoneId: string.IsNullOrWhiteSpace(raw.StartingZoneId) ? null : raw.StartingZoneId,
                Prerequisites: raw.Prerequisites is null
                    ? Array.Empty<string>()
                    : raw.Prerequisites.ToList(),
                Rewards: rewards,
                Nodes: nodes,
                InternalEdges: internalEdges));
        }

        // Second pass: prerequisites must reference declared quest ids.
        foreach (var q in defs)
        {
            foreach (var p in q.Prerequisites)
            {
                if (!seenIds.Contains(p))
                    throw new InvalidDataException(
                        $"{path}: quest '{q.QuestId}' lists unknown prerequisite questId '{p}'.");
            }
        }

        // Cross-quest edges.
        var edges = new List<QuestEdgeDefinition>();
        if (doc.Edges is not null)
        {
            var edgeSeen = new HashSet<(string kind, string from, string to, string? outcome)>();
            foreach (var e in doc.Edges)
            {
                if (string.IsNullOrWhiteSpace(e.Kind) || !ValidQuestEdgeKinds.Contains(e.Kind))
                    throw new InvalidDataException(
                        $"{path}: edge has invalid kind '{e.Kind}' (expected 'unlocks' or 'requires').");

                if (string.IsNullOrWhiteSpace(e.From) || !seenIds.Contains(e.From))
                    throw new InvalidDataException(
                        $"{path}: edge references unknown 'from' questId '{e.From}'.");
                if (string.IsNullOrWhiteSpace(e.To) || !seenIds.Contains(e.To))
                    throw new InvalidDataException(
                        $"{path}: edge references unknown 'to' questId '{e.To}'.");

                string? outcome = string.IsNullOrWhiteSpace(e.Outcome) ? null : e.Outcome;

                if (string.Equals(e.Kind, "unlocks", StringComparison.Ordinal))
                {
                    if (outcome is null)
                        throw new InvalidDataException(
                            $"{path}: 'unlocks' edge from '{e.From}' to '{e.To}' requires an outcome.");

                    // Outcome must be declared by the source quest.
                    var fromDef = defs.Single(q => q.QuestId == e.From);
                    if (!fromDef.PossibleOutcomes.Contains(outcome, StringComparer.Ordinal))
                        throw new InvalidDataException(
                            $"{path}: 'unlocks' edge from '{e.From}' uses outcome '{outcome}' " +
                            $"which is not in that quest's possibleOutcomes.");
                }

                var key = (e.Kind, e.From, e.To, outcome);
                if (!edgeSeen.Add(key))
                    throw new InvalidDataException(
                        $"{path}: duplicate edge {e.Kind} {e.From} -> {e.To} (outcome={outcome ?? "<none>"}).");

                edges.Add(new QuestEdgeDefinition(
                    Kind: e.Kind,
                    FromQuestId: e.From,
                    ToQuestId: e.To,
                    Outcome: outcome));
            }
        }

        return (defs, edges);
    }

    private sealed class QuestsFile
    {
        public List<RawQuest>? Quests { get; set; }
        public List<RawQuestEdge>? Edges { get; set; }
    }

    private sealed class RawQuest
    {
        public string QuestId { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string FactionId { get; set; } = "";
        public string RequiredTier { get; set; } = "";
        public string RequiredWorld { get; set; } = "";
        public int ReputationReward { get; set; }
        public List<string>? PossibleOutcomes { get; set; }
        public bool IsWyrdQuest { get; set; }
        public string? StartingZoneId { get; set; }
        public List<string>? Prerequisites { get; set; }
        public List<RawQuestReward>? Rewards { get; set; }
        public List<RawQuestNode>? Nodes { get; set; }
        public List<RawQuestInternalEdge>? InternalEdges { get; set; }
    }

    private sealed class RawQuestReward
    {
        public string Kind { get; set; } = "";
        public string? ItemName { get; set; }
        public int Quantity { get; set; }
    }

    private sealed class RawQuestNode
    {
        public string NodeId { get; set; } = "";
        public string Type { get; set; } = "";
        public string? Content { get; set; }
        public List<string>? RequiredFlags { get; set; }
    }

    private sealed class RawQuestInternalEdge
    {
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string? Outcome { get; set; }
    }

    private sealed class RawQuestEdge
    {
        public string Kind { get; set; } = "";
        public string From { get; set; } = "";
        public string To { get; set; } = "";
        public string? Outcome { get; set; }
    }

    // ─── World-event loader ──────────────────────────────────────────────────

    private static readonly HashSet<string> ValidEventFamilies =
        new(StringComparer.Ordinal)
        { "economic", "political", "faction-long-arc", "encounter", "weather" };

    private static readonly HashSet<string> ValidEventTriggerKinds =
        new(StringComparer.Ordinal)
        { "unconditional", "gameTick", "seasonalDay", "lunarCycle",
          "repThreshold", "questCompleted", "complex" };

    private static readonly HashSet<string> ValidWorldEventEffectTypes =
        new(StringComparer.Ordinal)
        {
            "zoneAmbient", "npcAvailability", "npcDialogueLine",
            "shopPriceShift", "shopStockShift", "encounterRateShift",
            "reputationDrift", "spawnNode", "unlockDialogue", "setFlag",
        };

    /// <summary>
    /// Effect types whose runtime application mutates durable world state and
    /// therefore REQUIRE a matching <c>onExpire</c> inverse to restore the
    /// baseline when the event ends. A symmetric-cleanup warning fires if a
    /// transient (non-permanent, non-oneTime) event applies one of these
    /// without an inverse on expire.
    /// </summary>
    private static readonly HashSet<string> EffectTypesRequiringCleanup =
        new(StringComparer.Ordinal)
        {
            "zoneAmbient", "npcAvailability", "shopPriceShift",
            "shopStockShift", "encounterRateShift",
        };

    /// <summary>
    /// Loads content/world-events.json and validates it. Validation is
    /// strict-enough-to-catch-typos:
    /// unique event ids, trigger.kind + shape consistency, effects[] non-empty
    /// and every type known, cross-ref every zoneId / factionId / questId /
    /// npcId against the authored registries, onExpire[] presence check warned
    /// (not thrown) when transient effects have no inverse.
    /// </summary>
    private IReadOnlyList<WorldEventDefinition> LoadWorldEvents()
    {
        var path = Path.Combine(_contentRoot, "world-events.json");
        if (!File.Exists(path))
        {
            // Optional file: earlier bases on the migration chain don't ship it.
            _logger?.LogInformation("ContentProvider: no world-events.json at {Path} — event catalog will be empty.", path);
            return Array.Empty<WorldEventDefinition>();
        }

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<WorldEventsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Events is null || doc.Events.Count == 0)
            throw new InvalidDataException($"{path}: no events defined.");

        var zoneIds = new HashSet<string>(_zonesById.Keys, StringComparer.Ordinal);
        var questIds = new HashSet<string>(_questsById.Keys, StringComparer.Ordinal);

        var list = new List<WorldEventDefinition>(doc.Events.Count);
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var raw in doc.Events)
        {
            if (string.IsNullOrWhiteSpace(raw.Id))
                throw new InvalidDataException($"{path}: event missing id.");
            if (!seenIds.Add(raw.Id))
                throw new InvalidDataException($"{path}: duplicate event id '{raw.Id}'.");

            if (string.IsNullOrWhiteSpace(raw.DisplayName))
                throw new InvalidDataException($"{path}: event '{raw.Id}' missing displayName.");
            if (string.IsNullOrWhiteSpace(raw.Description))
                throw new InvalidDataException($"{path}: event '{raw.Id}' missing description.");

            if (string.IsNullOrWhiteSpace(raw.Family) || !ValidEventFamilies.Contains(raw.Family))
                throw new InvalidDataException(
                    $"{path}: event '{raw.Id}' has invalid family '{raw.Family}'.");

            if (raw.Priority < 1 || raw.Priority > 5)
                throw new InvalidDataException(
                    $"{path}: event '{raw.Id}' priority must be 1..5 (got {raw.Priority}).");

            var trigger = ValidateTrigger(raw.Trigger, raw.Id, path, questIds);
            var duration = ValidateDuration(raw.Duration, raw.Id, path);

            if (raw.Effects is null || raw.Effects.Count == 0)
                throw new InvalidDataException(
                    $"{path}: event '{raw.Id}' must declare at least one effect.");

            var effects = raw.Effects
                .Select(e => ValidateEffect(e, raw.Id, "effects", path, zoneIds))
                .ToList();
            var onExpire = (raw.OnExpire ?? new List<RawWorldEventEffect>())
                .Select(e => ValidateEffect(e, raw.Id, "onExpire", path, zoneIds))
                .ToList();

            // Symmetric-cleanup warning (not a throw): transient events that
            // apply a cleanup-requiring effect type but ship no onExpire entry
            // are almost certainly missing their inverse. Permanent / oneTime
            // events are exempt — their whole point is to change the world.
            var isPermanent = duration.Permanent;
            var isOneTime = raw.OneTime;
            if (!isPermanent && !isOneTime)
            {
                var needsCleanup = effects.Any(e => EffectTypesRequiringCleanup.Contains(e.Type));
                if (needsCleanup && onExpire.Count == 0)
                {
                    _logger?.LogWarning(
                        "world-events.json: event '{EventId}' applies a cleanup-requiring effect but declares no onExpire inverse.",
                        raw.Id);
                }
            }

            // Faction tag cross-ref
            var factionTags = new List<FactionId>();
            if (raw.FactionTags is not null)
            {
                foreach (var ft in raw.FactionTags)
                {
                    if (string.IsNullOrWhiteSpace(ft) || !ValidFactionIdNames.Contains(ft))
                        throw new InvalidDataException(
                            $"{path}: event '{raw.Id}' factionTag '{ft}' is not a valid FactionId.");
                    factionTags.Add(Enum.Parse<FactionId>(ft));
                }
            }

            // Zone tag cross-ref
            var zoneTags = new List<string>();
            if (raw.ZoneTags is not null)
            {
                foreach (var zt in raw.ZoneTags)
                {
                    if (string.IsNullOrWhiteSpace(zt))
                        throw new InvalidDataException(
                            $"{path}: event '{raw.Id}' has empty zoneTag entry.");
                    if (zoneIds.Count > 0 && !zoneIds.Contains(zt))
                        throw new InvalidDataException(
                            $"{path}: event '{raw.Id}' zoneTag '{zt}' is not a known zoneId.");
                    zoneTags.Add(zt);
                }
            }

            // Quest tag cross-ref
            var questTags = new List<string>();
            if (raw.QuestTags is not null)
            {
                foreach (var qt in raw.QuestTags)
                {
                    if (string.IsNullOrWhiteSpace(qt))
                        throw new InvalidDataException(
                            $"{path}: event '{raw.Id}' has empty questTag entry.");
                    if (questIds.Count > 0 && !questIds.Contains(qt))
                        throw new InvalidDataException(
                            $"{path}: event '{raw.Id}' questTag '{qt}' is not a known questId.");
                    questTags.Add(qt);
                }
            }

            list.Add(new WorldEventDefinition(
                Id: raw.Id,
                DisplayName: raw.DisplayName,
                Family: raw.Family,
                Priority: raw.Priority,
                Description: raw.Description,
                Trigger: trigger,
                Duration: duration,
                Effects: effects,
                OnExpire: onExpire,
                FactionTags: factionTags,
                ZoneTags: zoneTags,
                QuestTags: questTags,
                WorldStateFlag: string.IsNullOrWhiteSpace(raw.WorldStateFlag) ? null : raw.WorldStateFlag,
                OneTime: raw.OneTime));
        }

        return list;
    }

    private WorldEventTrigger ValidateTrigger(RawWorldEventTrigger? raw, string eventId, string path, HashSet<string> questIds)
    {
        if (raw is null)
            throw new InvalidDataException($"{path}: event '{eventId}' missing trigger.");

        if (string.IsNullOrWhiteSpace(raw.Kind) || !ValidEventTriggerKinds.Contains(raw.Kind))
            throw new InvalidDataException(
                $"{path}: event '{eventId}' has invalid trigger.kind '{raw.Kind}'.");

        FactionId? faction = null;
        ReputationTier? tier = null;

        switch (raw.Kind)
        {
            case "gameTick":
            case "lunarCycle":
                if ((raw.EveryDays is null || raw.EveryDays <= 0) &&
                    (raw.EveryMinutes is null || raw.EveryMinutes <= 0))
                    throw new InvalidDataException(
                        $"{path}: event '{eventId}' trigger.kind='{raw.Kind}' requires everyDays or everyMinutes > 0.");
                break;
            case "seasonalDay":
                if (raw.Day is null || raw.Day < 0)
                    throw new InvalidDataException(
                        $"{path}: event '{eventId}' trigger.kind='seasonalDay' requires day >= 0.");
                break;
            case "repThreshold":
                if (string.IsNullOrWhiteSpace(raw.FactionId) || !ValidFactionIdNames.Contains(raw.FactionId))
                    throw new InvalidDataException(
                        $"{path}: event '{eventId}' trigger.kind='repThreshold' has invalid factionId '{raw.FactionId}'.");
                faction = Enum.Parse<FactionId>(raw.FactionId);
                if (!string.IsNullOrWhiteSpace(raw.MinTier))
                {
                    if (!ValidReputationTierNames.Contains(raw.MinTier))
                        throw new InvalidDataException(
                            $"{path}: event '{eventId}' trigger.kind='repThreshold' has invalid minTier '{raw.MinTier}'.");
                    tier = Enum.Parse<ReputationTier>(raw.MinTier);
                }
                if (tier is null && raw.Min is null)
                    throw new InvalidDataException(
                        $"{path}: event '{eventId}' trigger.kind='repThreshold' requires minTier or min.");
                break;
            case "questCompleted":
                if (string.IsNullOrWhiteSpace(raw.QuestId))
                    throw new InvalidDataException(
                        $"{path}: event '{eventId}' trigger.kind='questCompleted' requires questId.");
                if (questIds.Count > 0 && !questIds.Contains(raw.QuestId))
                    throw new InvalidDataException(
                        $"{path}: event '{eventId}' trigger.questId '{raw.QuestId}' is not a known questId.");
                break;
            case "unconditional":
            case "complex":
                // No structural requirement beyond kind. Prose is expected but
                // not enforced — keeps authoring friction low.
                break;
        }

        return new WorldEventTrigger(
            Kind: raw.Kind,
            EveryDays: raw.EveryDays,
            EveryMinutes: raw.EveryMinutes,
            Day: raw.Day,
            FactionId: faction,
            MinTier: tier,
            Min: raw.Min,
            QuestId: string.IsNullOrWhiteSpace(raw.QuestId) ? null : raw.QuestId,
            Outcome: string.IsNullOrWhiteSpace(raw.Outcome) ? null : raw.Outcome,
            Prose: string.IsNullOrWhiteSpace(raw.Prose) ? null : raw.Prose);
    }

    private static WorldEventDuration ValidateDuration(RawWorldEventDuration? raw, string eventId, string path)
    {
        if (raw is null)
            throw new InvalidDataException($"{path}: event '{eventId}' missing duration.");

        var setCount = (raw.Days is not null ? 1 : 0)
                     + (raw.Minutes is not null ? 1 : 0)
                     + (raw.Permanent ? 1 : 0);
        if (setCount == 0)
            throw new InvalidDataException(
                $"{path}: event '{eventId}' duration must set exactly one of days/minutes/permanent.");
        if (setCount > 1)
            throw new InvalidDataException(
                $"{path}: event '{eventId}' duration must set only one of days/minutes/permanent.");

        if (raw.Days is not null && raw.Days <= 0)
            throw new InvalidDataException(
                $"{path}: event '{eventId}' duration.days must be > 0.");
        if (raw.Minutes is not null && raw.Minutes <= 0)
            throw new InvalidDataException(
                $"{path}: event '{eventId}' duration.minutes must be > 0.");

        return new WorldEventDuration(raw.Days, raw.Minutes, raw.Permanent);
    }

    private WorldEventEffect ValidateEffect(RawWorldEventEffect raw, string eventId, string section, string path, HashSet<string> zoneIds)
    {
        if (string.IsNullOrWhiteSpace(raw.Type) || !ValidWorldEventEffectTypes.Contains(raw.Type))
            throw new InvalidDataException(
                $"{path}: event '{eventId}' {section} has invalid effect type '{raw.Type}'.");

        if (!string.IsNullOrWhiteSpace(raw.ZoneId) && zoneIds.Count > 0 && !zoneIds.Contains(raw.ZoneId))
            throw new InvalidDataException(
                $"{path}: event '{eventId}' {section} references unknown zoneId '{raw.ZoneId}'.");

        FactionId? faction = null;
        if (!string.IsNullOrWhiteSpace(raw.FactionId))
        {
            if (!ValidFactionIdNames.Contains(raw.FactionId))
                throw new InvalidDataException(
                    $"{path}: event '{eventId}' {section} references invalid factionId '{raw.FactionId}'.");
            faction = Enum.Parse<FactionId>(raw.FactionId);
        }

        if (!string.IsNullOrWhiteSpace(raw.QuestId) && _questsById.Count > 0 && !_questsById.ContainsKey(raw.QuestId))
            throw new InvalidDataException(
                $"{path}: event '{eventId}' {section} references unknown questId '{raw.QuestId}'.");

        if (!string.IsNullOrWhiteSpace(raw.NpcId) && _npcsById.Count > 0 && !_npcsById.ContainsKey(raw.NpcId))
            throw new InvalidDataException(
                $"{path}: event '{eventId}' {section} references unknown npcId '{raw.NpcId}'.");

        return new WorldEventEffect(
            Type: raw.Type,
            ZoneId: string.IsNullOrWhiteSpace(raw.ZoneId) ? null : raw.ZoneId,
            NpcId: string.IsNullOrWhiteSpace(raw.NpcId) ? null : raw.NpcId,
            NpcName: string.IsNullOrWhiteSpace(raw.NpcName) ? null : raw.NpcName,
            FactionId: faction,
            Item: string.IsNullOrWhiteSpace(raw.Item) ? null : raw.Item,
            Multiplier: raw.Multiplier,
            Delta: raw.Delta,
            Flag: string.IsNullOrWhiteSpace(raw.Flag) ? null : raw.Flag,
            Value: raw.Value?.ToString(),
            Text: string.IsNullOrWhiteSpace(raw.Text) ? null : raw.Text,
            Available: raw.Available,
            QuestId: string.IsNullOrWhiteSpace(raw.QuestId) ? null : raw.QuestId);
    }

    private sealed class WorldEventsFile
    {
        public List<RawWorldEvent>? Events { get; set; }
    }

    private sealed class RawWorldEvent
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Family { get; set; } = "";
        public int Priority { get; set; }
        public string Description { get; set; } = "";
        public RawWorldEventTrigger? Trigger { get; set; }
        public RawWorldEventDuration? Duration { get; set; }
        public List<RawWorldEventEffect>? Effects { get; set; }
        public List<RawWorldEventEffect>? OnExpire { get; set; }
        public List<string>? FactionTags { get; set; }
        public List<string>? ZoneTags { get; set; }
        public List<string>? QuestTags { get; set; }
        public string? WorldStateFlag { get; set; }
        public bool OneTime { get; set; }
    }

    private sealed class RawWorldEventTrigger
    {
        public string Kind { get; set; } = "";
        public int? EveryDays { get; set; }
        public int? EveryMinutes { get; set; }
        public int? Day { get; set; }
        public string? FactionId { get; set; }
        public string? MinTier { get; set; }
        public int? Min { get; set; }
        public string? QuestId { get; set; }
        public string? Outcome { get; set; }
        public string? Prose { get; set; }
    }

    private sealed class RawWorldEventDuration
    {
        public int? Days { get; set; }
        public int? Minutes { get; set; }
        public bool Permanent { get; set; }
    }

    // ─── Trade loaders ─────────────────────────────────────────────────────

    private static ItemValuesDefinition EmptyItemValues() =>
        new(new Dictionary<ItemCategory, int>(), 0.25, new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase));

    private static TradeCurvesDefinition DefaultTradeCurves() =>
        new(1.5, 0.5, 1, 1, new GoldCurveDefinition(15, 0.25, 45, 3, 4));

    private ItemValuesDefinition LoadItemValues()
    {
        var path = Path.Combine(_contentRoot, "item-values.json");
        if (!File.Exists(path))
            return EmptyItemValues();

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<ItemValuesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        if (doc.Defaults is null)
            throw new InvalidDataException($"{path}: missing defaults.");

        var byCategory = new Dictionary<ItemCategory, int>();
        if (doc.Defaults.ByCategory is not null)
        {
            foreach (var kv in doc.Defaults.ByCategory)
            {
                if (!Enum.TryParse<ItemCategory>(kv.Key, ignoreCase: false, out var cat))
                    throw new InvalidDataException($"{path}: defaults.byCategory has invalid category '{kv.Key}'.");
                if (kv.Value < 0)
                    throw new InvalidDataException($"{path}: defaults.byCategory[{kv.Key}] must be >= 0.");
                byCategory[cat] = kv.Value;
            }
        }

        var wMult = doc.Defaults.WorkmanshipMultiplier ?? 0.25;
        if (wMult < 0) throw new InvalidDataException($"{path}: workmanshipMultiplier must be >= 0.");

        var byName = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (doc.Items is not null)
        {
            foreach (var entry in doc.Items)
            {
                if (string.IsNullOrWhiteSpace(entry.Name))
                    throw new InvalidDataException($"{path}: item entry missing name.");
                if (entry.BaseValue < 0)
                    throw new InvalidDataException($"{path}: item '{entry.Name}' baseValue must be >= 0.");
                byName[entry.Name] = entry.BaseValue;
            }
        }

        return new ItemValuesDefinition(byCategory, wMult, byName);
    }

    private TradeCurvesDefinition LoadTradeCurves()
    {
        var path = Path.Combine(_contentRoot, "trade-curves.json");
        if (!File.Exists(path))
            return DefaultTradeCurves();

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<TradeCurvesFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        var buy = doc.BuyMultiplier ?? 1.5;
        var sell = doc.SellMultiplier ?? 0.5;
        if (buy < 1.0) throw new InvalidDataException($"{path}: buyMultiplier must be >= 1.0.");
        if (sell < 0.0 || sell > 1.0) throw new InvalidDataException($"{path}: sellMultiplier must be in [0,1].");

        var g = doc.Gold ?? new GoldRaw();
        var gold = new GoldCurveDefinition(
            g.QuestRewardBase ?? 15,
            g.QuestRewardPerReputationPoint ?? 0.25,
            g.LootDropChancePercent ?? 45,
            g.LootDropBase ?? 3,
            g.LootDropPerDangerLevel ?? 4);

        return new TradeCurvesDefinition(buy, sell, doc.MinBuyPrice ?? 1, doc.MinSellPrice ?? 1, gold);
    }

    private IReadOnlyList<VendorDefinition> LoadVendors()
    {
        var path = Path.Combine(_contentRoot, "vendors.json");
        if (!File.Exists(path))
            return Array.Empty<VendorDefinition>();

        using var stream = File.OpenRead(path);
        var doc = JsonSerializer.Deserialize<VendorsFile>(stream, JsonOptions)
                  ?? throw new InvalidDataException($"{path}: empty or unreadable.");

        var list = new List<VendorDefinition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (doc.Vendors is null) return list;

        foreach (var raw in doc.Vendors)
        {
            if (string.IsNullOrWhiteSpace(raw.NpcId))
                throw new InvalidDataException($"{path}: vendor entry missing npcId.");
            if (!seen.Add(raw.NpcId))
                throw new InvalidDataException($"{path}: duplicate vendor npcId '{raw.NpcId}'.");

            if (!_npcsById.TryGetValue(raw.NpcId, out var npc))
                throw new InvalidDataException($"{path}: vendor npcId '{raw.NpcId}' is not a known NPC.");
            if (npc.Role != NpcRole.Shopkeeper)
                throw new InvalidDataException($"{path}: vendor npcId '{raw.NpcId}' has role {npc.Role}; shopkeeper required.");
            if (!_zonesById.TryGetValue(npc.HomeZoneId, out var zone))
                throw new InvalidDataException($"{path}: vendor '{raw.NpcId}' homeZoneId '{npc.HomeZoneId}' not found in zones.");

            var buys = new List<ItemCategory>();
            if (raw.BuysCategories is not null)
            {
                foreach (var cat in raw.BuysCategories)
                {
                    if (!Enum.TryParse<ItemCategory>(cat, ignoreCase: false, out var parsed))
                        throw new InvalidDataException($"{path}: vendor '{raw.NpcId}' invalid buysCategories entry '{cat}'.");
                    buys.Add(parsed);
                }
            }

            var stock = new List<VendorStockEntry>();
            if (raw.Stock is not null)
            {
                foreach (var s in raw.Stock)
                {
                    if (string.IsNullOrWhiteSpace(s.ItemName))
                        throw new InvalidDataException($"{path}: vendor '{raw.NpcId}' stock entry missing itemName.");
                    if (!Enum.TryParse<ItemCategory>(s.Category, ignoreCase: false, out var scat))
                        throw new InvalidDataException($"{path}: vendor '{raw.NpcId}' stock '{s.ItemName}' invalid category '{s.Category}'.");
                    if (s.Quantity < 0)
                        throw new InvalidDataException($"{path}: vendor '{raw.NpcId}' stock '{s.ItemName}' quantity < 0.");
                    var work = s.Workmanship ?? 1;
                    if (work < 1 || work > 10)
                        throw new InvalidDataException($"{path}: vendor '{raw.NpcId}' stock '{s.ItemName}' workmanship must be 1-10.");
                    stock.Add(new VendorStockEntry(s.ItemName, scat, s.Quantity, work));
                }
            }

            list.Add(new VendorDefinition(
                NpcId: raw.NpcId,
                DisplayName: string.IsNullOrWhiteSpace(raw.DisplayName) ? npc.DisplayName : raw.DisplayName!,
                World: zone.World,
                ZoneNumber: zone.ZoneNumber,
                BuysCategories: buys,
                Stock: stock));
        }

        return list;
    }

    private sealed class ItemValuesFile
    {
        [JsonPropertyName("defaults")] public ItemValueDefaultsRaw? Defaults { get; set; }
        [JsonPropertyName("items")] public List<ItemValueEntryRaw>? Items { get; set; }
    }
    private sealed class ItemValueDefaultsRaw
    {
        [JsonPropertyName("byCategory")] public Dictionary<string, int>? ByCategory { get; set; }
        [JsonPropertyName("workmanshipMultiplier")] public double? WorkmanshipMultiplier { get; set; }
    }
    private sealed class ItemValueEntryRaw
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("baseValue")] public int BaseValue { get; set; }
    }

    private sealed class TradeCurvesFile
    {
        [JsonPropertyName("buyMultiplier")] public double? BuyMultiplier { get; set; }
        [JsonPropertyName("sellMultiplier")] public double? SellMultiplier { get; set; }
        [JsonPropertyName("minBuyPrice")] public int? MinBuyPrice { get; set; }
        [JsonPropertyName("minSellPrice")] public int? MinSellPrice { get; set; }
        [JsonPropertyName("gold")] public GoldRaw? Gold { get; set; }
    }
    private sealed class GoldRaw
    {
        [JsonPropertyName("questRewardBase")] public double? QuestRewardBase { get; set; }
        [JsonPropertyName("questRewardPerReputationPoint")] public double? QuestRewardPerReputationPoint { get; set; }
        [JsonPropertyName("lootDropChancePercent")] public int? LootDropChancePercent { get; set; }
        [JsonPropertyName("lootDropBase")] public int? LootDropBase { get; set; }
        [JsonPropertyName("lootDropPerDangerLevel")] public int? LootDropPerDangerLevel { get; set; }
    }

    private sealed class VendorsFile
    {
        [JsonPropertyName("vendors")] public List<VendorRaw>? Vendors { get; set; }
    }
    private sealed class VendorRaw
    {
        [JsonPropertyName("npcId")] public string? NpcId { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("buysCategories")] public List<string>? BuysCategories { get; set; }
        [JsonPropertyName("stock")] public List<VendorStockRaw>? Stock { get; set; }
    }
    private sealed class VendorStockRaw
    {
        [JsonPropertyName("itemName")] public string ItemName { get; set; } = "";
        [JsonPropertyName("category")] public string Category { get; set; } = "Component";
        [JsonPropertyName("quantity")] public int Quantity { get; set; }
        [JsonPropertyName("workmanship")] public int? Workmanship { get; set; }
    }

    private sealed class RawWorldEventEffect
    {
        public string Type { get; set; } = "";
        public string? ZoneId { get; set; }
        public string? NpcId { get; set; }
        public string? NpcName { get; set; }
        public string? FactionId { get; set; }
        public string? Item { get; set; }
        public double? Multiplier { get; set; }
        public int? Delta { get; set; }
        public string? Flag { get; set; }
        public object? Value { get; set; }
        public string? Text { get; set; }
        public bool? Available { get; set; }
        public string? QuestId { get; set; }
    }
}
