using FirstMud.Application.Content;
using FirstMud.Domain.Entities;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Single source of truth for biome detection, wilderness danger calculation,
/// and biome-based encounter narration.
///
/// Previously these methods lived as static helpers on CombatHelpers, causing
/// duplication across MoveCommandHandler, HarvestCommandHandler,
/// AutoFarmCommandHandler, and CombatCommandHandler.
/// </summary>
public static class BiomeService
{
    // -------------------------------------------------------------------------
    // Zone → biome mapping
    // -------------------------------------------------------------------------

    public static string GetBiome(Zone? zone)
    {
        if (zone is null) return "plains";
        return ContentAccessor.Current.GetBiomeForZone(zone.Name) ?? "plains";
    }

    /// <summary>
    /// Returns the biome string for a given position.
    /// If a named zone is nearby, defers to <see cref="GetBiome(Zone?)"/>.
    /// Otherwise approximates from proximity to known zone centres.
    /// </summary>
    public static string GetBiomeForPosition(Zone? nearbyZone, int x, int y)
    {
        if (nearbyZone != null) return GetBiome(nearbyZone);
        return GuessWildernessBiome(x, y);
    }

    /// <summary>
    /// Approximates a biome for a wilderness tile.
    ///
    /// Strategy (two-pass):
    ///   1. If the tile is within 4 Manhattan tiles of a known zone centre it
    ///      inherits that zone's biome — the player is on the doorstep of the
    ///      named location.
    ///   2. Otherwise the biome is derived from the tile's coordinates via a
    ///      deterministic hash that broadly replicates the client's noise-based
    ///      terrain distribution (mostly forest/plains, with water/swamp/mountain
    ///      and occasional desert/wyrd pockets).
    ///
    /// The previous implementation used radii of 8–10, which caused the entire
    /// region between zones to be claimed by whichever named zone happened to be
    /// closest — leading to water-biome monsters spawning in forested wilderness
    /// far from any coastline.
    /// </summary>
    public static string GuessWildernessBiome(int x, int y)
    {
        // Only inherit a zone's biome when the tile is very close to that zone.
        const int ZoneInfluenceRadius = 4;

        // Zone proximity centres + biomes now come from content/zones.json.
        var zones = ContentAccessor.Current.AllZones();

        // Pass 1: zone proximity (tight radius).
        string closestBiome = "";
        int closestDist = int.MaxValue;

        foreach (var zone in zones)
        {
            int dist = Math.Abs(x - zone.Layout.X) + Math.Abs(y - zone.Layout.Y);
            if (dist < ZoneInfluenceRadius && dist < closestDist)
            {
                closestDist = dist;
                closestBiome = zone.Biome;
            }
        }

        if (closestBiome != "")
            return closestBiome;

        // Pass 2: position-based terrain for tiles not near any named zone.
        // Uses a cheap deterministic hash that mirrors the client's distribution:
        //   ~25 % forest, ~25 % plains, ~15 % mountain, ~15 % swamp,
        //   ~10 % water,  ~5 % desert,  ~5 % wyrd.
        int hash = ((x * 374761 + y * 668265) & 0x7FFF_FFFF) % 100;

        return hash switch
        {
            < 10 => "water",
            < 25 => "mountain",
            < 50 => "forest",
            < 75 => "plains",
            < 83 => "swamp",
            < 88 => "sand",
            < 93 => "desert",
            _    => "wyrd",
        };
    }

    /// <summary>
    /// Computes the danger level for a wilderness tile (no named zone nearby).
    /// Combines Manhattan distance from the world centre (20, 10) with a biome
    /// modifier. Returns a value in [0, 10].
    /// </summary>
    public static int GetWildernessDanger(int x, int y, string biome)
    {
        const int worldCentreX = 20;
        const int worldCentreY = 10;
        int distFromCenter = Math.Abs(x - worldCentreX) + Math.Abs(y - worldCentreY);
        int wildernessBaseDanger = distFromCenter / 5;

        int biomeDanger = biome switch
        {
            "forest"      => 2,
            "denseForest" => 3,
            "mountain"    => 3,
            "snowMountain"=> 5,
            "swamp"       => 3,
            "water"       => 2,
            "desert"      => 3,
            "wyrd"        => 5,
            "plains" or "grassland" => 1,
            "path"        => 0,
            _             => 1,
        };

        return Math.Clamp(wildernessBaseDanger + biomeDanger, 0, 10);
    }

    public static string GetBiomeNarration(string biome, string monsterNames) => biome switch
    {
        "mountain" => $"A {monsterNames} emerges from behind a boulder!",
        "forest"   => $"{monsterNames} burst from the undergrowth!",
        "water"    => $"Something rises from the depths... {monsterNames}!",
        "desert"   => $"A {monsterNames} scuttles from beneath the dunes!",
        "swamp"    => $"A {monsterNames} materializes from the mist!",
        "wyrd"     => $"Reality tears open. A {monsterNames} steps through!",
        _          => $"Hostile creatures emerge! You face: {monsterNames}.",
    };
}
