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

        // Publish the static accessor for the few legacy static call sites
        // (ZoneGridLayout / BiomeService) that cannot easily take DI.
        ContentAccessor.Publish(this);

        _logger?.LogInformation(
            "ContentProvider loaded: {ConsumableCount} consumables, {BuildingCount} buildings, {RecipeCount} recipes, {MonsterCount} monsters, {PoolCount} drop pools, {MonsterDropCount} monster drops, {ZoneCount} zones, {FactionCount} factions from {Root}",
            _consumables.Count, _buildings.Count, _recipes.Count, _monsters.Count, _lootTables.DropPools.Count, _lootTables.MonsterDrops.Count, _zones.Count, _factions.Count, _contentRoot);
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
}
