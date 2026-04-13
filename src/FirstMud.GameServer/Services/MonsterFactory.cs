using FirstMud.Application;
using FirstMud.Application.Content;
using FirstMud.Domain.Enums;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Responsible for building encounter-ready monster packs from biome + danger data.
///
/// Base stats (HP, speed, element, abilities, name) are data-driven via
/// <see cref="IContentProvider"/> and loaded from <c>content/monsters.json</c>.
/// Procedural logic — tier selection, pack-size curves, HP/speed/power
/// scaling, boss composition, random variance — remains here in C#.
/// </summary>
public sealed class MonsterFactory
{
    private static readonly string[] KnownBiomes =
        ["mountain", "forest", "desert", "water", "swamp", "wyrd", "plains"];

    private readonly IContentProvider _content;

    public MonsterFactory(IContentProvider content)
    {
        _content = content ?? throw new ArgumentNullException(nameof(content));
    }

    public List<MonsterTemplate> BuildMonsterPack(int dangerLevel, int playerLevel = 1, string biome = "plains", int partySize = 1)
    {
        // Unknown biomes fall back to plains (preserves original switch default).
        var resolvedBiome = Array.IndexOf(KnownBiomes, biome) >= 0 ? biome : "plains";

        var tierIndex = dangerLevel switch
        {
            <= 2 => 0,
            <= 4 => 1,
            <= 7 => 2,
            _    => 3,
        };

        // Pack size scales with party size + danger (Fix C — softened from /3
        // to /4 so mid-danger fights aren't brutally outnumbered):
        // danger 1-3:   party + 0  (fair fight)
        // danger 4-7:   party + 1  (slightly outnumbered)
        // danger 8-10:  party + 2  (outnumbered)
        // Pre-fix (/3) combined with +40% HP + +30% power per danger was
        // producing three-TPK-in-a-row play sessions at danger 5–7.
        int maxEnemies = partySize + (dangerLevel / 4);

        int packSize = dangerLevel switch
        {
            <= 2 => Math.Min(maxEnemies, Random.Shared.Next(1, 3)),
            <= 4 => Math.Min(maxEnemies, Random.Shared.Next(2, 4)),
            <= 6 => Math.Min(maxEnemies, Random.Shared.Next(3, maxEnemies + 1)),
            <= 8 => Math.Min(maxEnemies, Random.Shared.Next(4, maxEnemies + 1)),
            _    => maxEnemies, // danger 9-10: maximum enemies
        };

        packSize = Math.Max(1, packSize);

        var pool = _content.MonstersByBiomeAndTier(resolvedBiome, tierIndex);
        if (pool.Count == 0)
            throw new InvalidOperationException(
                $"No monsters defined for biome '{resolvedBiome}' tier {tierIndex}.");

        var pack = new List<MonsterTemplate>();

        // First monster is the primary (boss at danger 10)
        var primary = pool[Random.Shared.Next(pool.Count)];
        pack.Add(ScaleMonster(ToTemplate(primary), dangerLevel, playerLevel, isBoss: dangerLevel >= 10));

        // Fill remaining pack slots with monsters from the tier below
        while (pack.Count < packSize)
        {
            var weakTier = Math.Max(0, tierIndex - 1);
            var weakPool = _content.MonstersByBiomeAndTier(resolvedBiome, weakTier);
            if (weakPool.Count == 0)
                weakPool = pool;
            var extra = weakPool[Random.Shared.Next(weakPool.Count)];
            pack.Add(ScaleMonster(ToTemplate(extra), dangerLevel, playerLevel, isBoss: false));
        }

        return pack;
    }

    private static MonsterTemplate ToTemplate(MonsterDefinition def) =>
        new(def.Name, def.Hp, def.Speed, def.Level, def.Element, def.Abilities);

    private MonsterTemplate ScaleMonster(MonsterTemplate template, int dangerLevel, int playerLevel, bool isBoss = false)
    {
        var variance = Random.Shared.Next(-1, 2); // -1, 0, or 1
        var monsterLevel = Math.Max(1, dangerLevel + variance);

        if (playerLevel > dangerLevel * 2)
            monsterLevel = Math.Max(monsterLevel, playerLevel - 2);

        // Curve-based HP / power / speed / boss scaling lives in the shared
        // MonsterScaling helper so the design-tool encounter-sim runs use
        // identical math (one source of truth via content/combat-curves.json).
        var scaled = MonsterScaling.Apply(template, dangerLevel, _content.CombatCurves.MonsterScaling, isBoss);

        return scaled with { Level = monsterLevel };
    }
}
