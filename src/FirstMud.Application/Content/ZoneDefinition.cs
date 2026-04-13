using FirstMud.Domain.Enums;

namespace FirstMud.Application.Content;

/// <summary>
/// Hand-crafted grid coordinate for a seeded zone. Drives the world map so
/// the layout is stable across server restarts.
/// </summary>
public sealed record ZoneLayoutDefinition(int X, int Y);

/// <summary>
/// Optional monster spawn hint for a zone. <c>Weight</c> defaults to 1 when
/// not specified. <c>MonsterId</c> references the monster catalog (monsters.json)
/// and is validated cross-file when that catalog is present.
/// </summary>
public sealed record ZoneMonsterSpawnDefinition(string MonsterId, int Weight);

/// <summary>
/// Data-driven zone definition, loaded from content/zones.json.
///
/// Replaces the hardcoded zone list in StartupSeeder.SeedAeldranZonesAsync,
/// the biome switch in BiomeService.GetBiome, and the KnownPositions
/// dictionary in ZoneGridLayout. Identity, metadata, spawn config and grid
/// hints live here; pathfinding, tile rendering and movement math stay in C#.
/// </summary>
public sealed record ZoneDefinition(
    string ZoneId,
    WorldId World,
    int ZoneNumber,
    string Name,
    string Description,
    string AsciiSymbol,
    string Biome,
    int DangerLevel,
    bool IsPortalZone,
    WorldId? PortalDestination,
    bool IsStartingZone,
    ZoneLayoutDefinition Layout,
    ResourceType? PrimaryResource,
    ResourceType? SecondaryResource,
    IReadOnlyList<ZoneMonsterSpawnDefinition> MonsterSpawns);
