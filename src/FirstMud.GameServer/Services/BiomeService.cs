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

    public static string GetBiome(Zone? zone) => zone?.Name switch
    {
        "Caervorn Highlands" => "mountain",
        "The Thornwood"      => "forest",
        "Portmere (Compact)" => "plains",
        "Gravenmarsh"        => "swamp",
        "The Drowned Coast"  => "water",
        "The Ashen Reach"    => "desert",
        "Starting Road"      => "plains",
        "Gravenhold"         => "mountain",
        "The Maw Borderlands"=> "wyrd",
        _                    => "plains",
    };

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
    /// Approximates a biome for a wilderness tile based on proximity to named
    /// zone centres (matching ZoneGridLayout.cs).
    /// </summary>
    public static string GuessWildernessBiome(int x, int y)
    {
        // (zoneCentreX, zoneCentreY, biome, influenceRadius)
        (int cx, int cy, string biome, int radius)[] zoneThemes =
        [
            (8,  3,  "mountain", 10),   // Caervorn Highlands
            (13, 5,  "forest",   10),   // The Thornwood
            (24, 13, "plains",    8),   // Portmere Compact
            (28, 9,  "swamp",    10),   // Gravenmarsh
            (32, 15, "water",    10),   // The Drowned Coast
            (34, 4,  "desert",   10),   // The Ashen Reach
            (20, 10, "plains",    6),   // Starting Road
            (26, 7,  "mountain",  8),   // Gravenhold
            (36, 18, "wyrd",     10),   // The Maw Borderlands
        ];

        string closestBiome = "plains";
        int closestDist = int.MaxValue;

        foreach (var (cx, cy, biome, radius) in zoneThemes)
        {
            int dist = Math.Abs(x - cx) + Math.Abs(y - cy);
            if (dist < radius && dist < closestDist)
            {
                closestDist = dist;
                closestBiome = biome;
            }
        }

        return closestBiome;
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
