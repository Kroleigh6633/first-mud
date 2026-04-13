using FirstMud.Application;
using FirstMud.Domain.Entities;
using FirstMud.Domain.Enums;
using FirstMud.Domain.ValueObjects;

namespace FirstMud.GameServer.Services;

/// <summary>
/// Responsible for building encounter-ready monster packs from biome + danger data.
///
/// Previously embedded as a ~290-line static block inside CombatHelpers, making
/// CombatHelpers a god class. Extracted here as a pure static utility that owns
/// the monster template catalogue and pack-building strategy.
/// </summary>
public static class MonsterFactory
{
    // -------------------------------------------------------------------------
    // Shared ability definitions
    // -------------------------------------------------------------------------

    // Generic
    private static readonly CombatAbility Claw      = new("Claw",          6,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Bite      = new("Bite",          8,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

    // Mountain abilities
    private static readonly CombatAbility Headbutt       = new("Headbutt",         7,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility RockThrow      = new("Rock Throw",       9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility BoulderThrow   = new("Boulder Throw",   14,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Regenerate     = new("Regenerate",      12,  0, MagicElement.Earth, AbilityTargetType.Self,        AbilityCategory.Heal);
    private static readonly CombatAbility DiveAttack     = new("Dive Attack",     16,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility WindGust       = new("Wind Gust",        8,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility BreathWeapon   = new("Breath Weapon",   14,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility FrostSlam      = new("Frost Slam",      16,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility GlacialRoar    = new("Glacial Roar",     8,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility EarthShatter   = new("Earth Shatter",   12,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);

    // Forest abilities
    private static readonly CombatAbility Pounce         = new("Pounce",           9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Charge         = new("Charge",          10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Web            = new("Web",              5,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility VenomBite      = new("Venom Bite",      10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility QuickSlash     = new("Quick Slash",      9,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Maul           = new("Maul",            15,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Roar           = new("Roar",             5,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility BranchSwipe    = new("Branch Swipe",    14,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility RootStrike     = new("Root Strike",     10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility WyldGore       = new("Wyld Gore",       18,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility ThornBarrage   = new("Thorn Barrage",   14,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);

    // Desert abilities
    private static readonly CombatAbility StingStrike    = new("Sting Strike",     8,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility AcidSpit       = new("Acid Spit",        9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility FireLash       = new("Fire Lash",       10,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Constrict      = new("Constrict",        9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility Burrow         = new("Burrow",           5,  0, MagicElement.Earth, AbilityTargetType.Self,        AbilityCategory.Buff);
    private static readonly CombatAbility Eruption       = new("Eruption",        12,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility Embersweep     = new("Ember Sweep",     12,  0, MagicElement.Fire,  AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility AshSurge       = new("Ash Surge",        9,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility ReviveFlame    = new("Revive Flame",    14,  0, MagicElement.Fire,  AbilityTargetType.Self,        AbilityCategory.Heal);
    private static readonly CombatAbility InfernoBreath  = new("Inferno Breath",  16,  0, MagicElement.Fire,  AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

    // Water abilities
    private static readonly CombatAbility Pinch          = new("Pinch",            7,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility WaterJet       = new("Water Jet",       10,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility TidalSurge     = new("Tidal Surge",     11,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility SnapJaw        = new("Snap Jaw",        12,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility TentacleLash   = new("Tentacle Lash",   13,  0, MagicElement.Water, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility InkCloud       = new("Ink Cloud",        6,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility CrushingDepths = new("Crushing Depths",  18, 0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility DrainTouch     = new("Drain Touch",     12,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);
    private static readonly CombatAbility VoidPulse      = new("Void Pulse",      15,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

    // Swamp abilities
    private static readonly CombatAbility Gnaw           = new("Gnaw",             7,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility LeechDrain     = new("Leech Drain",      8,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);
    private static readonly CombatAbility SpectralTouch  = new("Spectral Touch",  10,  0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility MistVeil       = new("Mist Veil",        5,  0, MagicElement.Water, AbilityTargetType.Self,        AbilityCategory.Buff);
    private static readonly CombatAbility MudSlap        = new("Mud Slap",         9,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility ShadowStep     = new("Shadow Step",     11,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility PhantomStrike  = new("Phantom Strike",   9,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility ToxicSpray     = new("Toxic Spray",     10,  0, MagicElement.Water, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility DebilitatingCroak = new("Debilitating Croak", 6, 0, MagicElement.Water, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility MultiStrike    = new("Multi Strike",    12,  0, MagicElement.Water, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility GraveDrain     = new("Grave Drain",     14,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);

    // Plains abilities
    private static readonly CombatAbility ScratchScrape  = new("Scratch",          6,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Snarl          = new("Snarl",            7,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility DirtyBlow      = new("Dirty Blow",       9,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility TrickSlash     = new("Trick Slash",      8,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility KickHooves     = new("Hoof Kick",       10,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Stampede       = new("Stampede",        11,  0, MagicElement.Air,   AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility ShieldBash     = new("Shield Bash",     12,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility ArmorBreak     = new("Armor Break",      8,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility Howl           = new("Howl",             6,  0, MagicElement.Earth, AbilityTargetType.Self,        AbilityCategory.Buff);
    private static readonly CombatAbility PackHunter     = new("Pack Hunter",     10,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility BrutalSmash    = new("Brutal Smash",    16,  0, MagicElement.Earth, AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility GroundPound    = new("Ground Pound",    12,  0, MagicElement.Earth, AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility CavalryCharge  = new("Cavalry Charge",  14,  0, MagicElement.Air,   AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility SwiftBlow      = new("Swift Blow",      10,  0, MagicElement.Air,   AbilityTargetType.SingleEnemy, AbilityCategory.Attack);

    // Wyrd abilities
    private static readonly CombatAbility WyrdNip        = new("Wyrd Nip",         7,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility ShadowPounce   = new("Shadow Pounce",    9,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility PhaseStrike    = new("Phase Strike",    11,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility Blink          = new("Blink",            5,  0, MagicElement.Aether,AbilityTargetType.Self,        AbilityCategory.Buff);
    private static readonly CombatAbility WraitheTouch   = new("Wraith Touch",    10,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility NullField      = new("Null Field",       8,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Debuff);
    private static readonly CombatAbility VoidTear       = new("Void Tear",       13,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility RealityShred   = new("Reality Shred",   14,  0, MagicElement.Aether,AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility WyrdBurst      = new("Wyrd Burst",      18,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Attack);
    private static readonly CombatAbility WeaveRend      = new("Weave Rend",      15,  0, MagicElement.Aether,AbilityTargetType.AllEnemies,  AbilityCategory.Attack);
    private static readonly CombatAbility AetherDrain    = new("Aether Drain",    12,  0, MagicElement.Aether,AbilityTargetType.SingleEnemy, AbilityCategory.Lifesteal);

    // -------------------------------------------------------------------------
    // Per-biome tier pools (tiers 0-3: dangerLevel <=2, <=4, <=7, 8+)
    // -------------------------------------------------------------------------

    private static readonly MonsterTemplate[][] MountainPools =
    [
        [
            new("Mountain Goat",  22, 7, 1, MagicElement.Earth, [Headbutt, Charge]),
            new("Rock Beetle",    26, 4, 1, MagicElement.Earth, [Claw, RockThrow]),
        ],
        [
            new("Stone Troll",    55, 4, 2, MagicElement.Earth, [BoulderThrow, Regenerate, EarthShatter]),
            new("Mountain Lion",  38, 9, 2, MagicElement.Air,   [Pounce, QuickSlash]),
        ],
        [
            new("Wyvern",         60, 8, 3, MagicElement.Air,   [DiveAttack, WindGust, RockThrow]),
            new("Rock Golem",     80, 3, 3, MagicElement.Earth, [BoulderThrow, EarthShatter, Regenerate]),
        ],
        [
            new("Dragon Whelp",   85, 7, 4, MagicElement.Fire,  [BreathWeapon, DiveAttack, Embersweep]),
            new("Frost Giant",    90, 3, 4, MagicElement.Water,  [FrostSlam, GlacialRoar, EarthShatter]),
        ],
    ];

    private static readonly MonsterTemplate[][] ForestPools =
    [
        [
            new("Timber Wolf",    28, 8, 1, MagicElement.Earth, [Bite, Pounce]),
            new("Wild Boar",      32, 6, 1, MagicElement.Earth, [Charge, Headbutt]),
        ],
        [
            new("Thornweaver Spider", 35, 7, 2, MagicElement.Earth, [Web, VenomBite, Claw]),
            new("Forest Bandit",      30, 9, 2, MagicElement.Air,   [QuickSlash, TrickSlash]),
        ],
        [
            new("Dire Bear",      65, 5, 3, MagicElement.Earth, [Maul, Roar, Charge]),
            new("Treant",         75, 3, 3, MagicElement.Earth, [BranchSwipe, RootStrike, EarthShatter]),
        ],
        [
            new("Elder Stag",     75, 7, 4, MagicElement.Aether, [WyldGore, ShadowStep, DiveAttack]),
            new("Thornwood Guardian", 95, 4, 4, MagicElement.Earth, [ThornBarrage, Maul, Regenerate]),
        ],
    ];

    private static readonly MonsterTemplate[][] DesertPools =
    [
        [
            new("Sand Scorpion",  24, 7, 1, MagicElement.Fire,  [StingStrike, Claw]),
            new("Dust Viper",     22, 8, 1, MagicElement.Earth, [Bite, Constrict]),
        ],
        [
            new("Fire Lizard",    36, 7, 2, MagicElement.Fire,  [FireLash, AcidSpit]),
            new("Giant Centipede",34, 6, 2, MagicElement.Earth, [Constrict, Bite, Claw]),
        ],
        [
            new("Sand Wurm",      65, 4, 3, MagicElement.Earth, [Burrow, Eruption, Bite]),
            new("Ash Golem",      70, 3, 3, MagicElement.Fire,  [AshSurge, Embersweep, Regenerate]),
        ],
        [
            new("Phoenix Hatchling", 70, 8, 4, MagicElement.Fire, [InfernoBreath, ReviveFlame, DiveAttack]),
            new("Ember Drake",    85, 7, 4, MagicElement.Fire,  [InfernoBreath, Embersweep, BreathWeapon]),
        ],
    ];

    private static readonly MonsterTemplate[][] WaterPools =
    [
        [
            new("Giant Crab",     26, 5, 1, MagicElement.Water, [Pinch, Claw]),
            new("Mud Skipper",    22, 8, 1, MagicElement.Water, [WaterJet, Bite]),
        ],
        [
            new("Tide Lurker",    35, 7, 2, MagicElement.Water, [TidalSurge, WaterJet]),
            new("Reef Shark",     32, 9, 2, MagicElement.Water, [SnapJaw, Charge]),
        ],
        [
            new("Sea Serpent",    60, 6, 3, MagicElement.Water, [TentacleLash, TidalSurge, Bite]),
            new("Kraken Spawn",   65, 5, 3, MagicElement.Water, [TentacleLash, InkCloud, CrushingDepths]),
        ],
        [
            new("Deep Horror",    80, 5, 4, MagicElement.Aether, [VoidPulse, CrushingDepths, DrainTouch]),
            new("Drowned Revenant",75, 6, 4, MagicElement.Water, [DrainTouch, TidalSurge, VoidPulse]),
        ],
    ];

    private static readonly MonsterTemplate[][] SwampPools =
    [
        [
            new("Swamp Rat",      22, 7, 1, MagicElement.Earth, [Gnaw, ScratchScrape]),
            new("Leech Swarm",    20, 6, 1, MagicElement.Water, [LeechDrain, Claw]),
        ],
        [
            new("Bog Wraith",     32, 7, 2, MagicElement.Water, [SpectralTouch, MistVeil, WaterJet]),
            new("Marsh Crawler",  36, 6, 2, MagicElement.Earth, [MudSlap, Claw, Constrict]),
        ],
        [
            new("Moor Stalker",   55, 8, 3, MagicElement.Aether, [ShadowStep, PhantomStrike, NullField]),
            new("Poison Toad",    52, 5, 3, MagicElement.Water,  [ToxicSpray, DebilitatingCroak, Bite]),
        ],
        [
            new("Swamp Hydra",    90, 5, 4, MagicElement.Water,  [MultiStrike, TidalSurge, TentacleLash]),
            new("Grave Wight",    78, 6, 4, MagicElement.Aether, [GraveDrain, PhantomStrike, NullField]),
        ],
    ];

    private static readonly MonsterTemplate[][] PlainsPools =
    [
        [
            new("Cave Rat",       24, 7, 1, MagicElement.Earth, [ScratchScrape, Gnaw]),
            new("Stray Dog",      26, 8, 1, MagicElement.Earth, [Snarl, Bite]),
        ],
        [
            new("Highway Bandit", 34, 8, 2, MagicElement.Air,   [DirtyBlow, TrickSlash]),
            new("Wild Horse",     36, 9, 2, MagicElement.Air,   [KickHooves, Stampede]),
        ],
        [
            new("Rogue Knight",   60, 6, 3, MagicElement.Earth, [ShieldBash, ArmorBreak, BrutalSmash]),
            new("Pack Alpha Wolf",55, 7, 3, MagicElement.Earth, [Howl, PackHunter, Maul]),
        ],
        [
            new("Wandering Ogre", 88, 4, 4, MagicElement.Earth, [BrutalSmash, GroundPound, Roar]),
            new("Mounted Raider", 75, 9, 4, MagicElement.Air,   [CavalryCharge, SwiftBlow, TrickSlash]),
        ],
    ];

    private static readonly MonsterTemplate[][] WyrdPools =
    [
        [
            new("Wyrd Hound",     25, 8, 1, MagicElement.Aether, [WyrdNip, PhantomStrike]),
            new("Shadow Cat",     22, 9, 1, MagicElement.Aether, [ShadowPounce, Claw]),
        ],
        [
            new("Phase Spider",   32, 8, 2, MagicElement.Aether, [PhaseStrike, Blink, Web]),
            new("Wyrd Wraith",    30, 7, 2, MagicElement.Aether, [WraitheTouch, NullField]),
        ],
        [
            new("Void Stalker",   58, 7, 3, MagicElement.Aether, [VoidTear, ShadowStep, NullField]),
            new("Reality Shredder",55, 6, 3, MagicElement.Aether, [RealityShred, PhaseStrike, VoidPulse]),
        ],
        [
            new("Wyrd Abomination", 92, 5, 4, MagicElement.Aether, [WyrdBurst, RealityShred, AetherDrain]),
            new("Tear in the Weave", 80, 6, 4, MagicElement.Aether, [WeaveRend, VoidPulse, NullField]),
        ],
    ];

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    public static List<MonsterTemplate> BuildMonsterPack(int dangerLevel, int playerLevel = 1, string biome = "plains")
    {
        var biomePools = biome switch
        {
            "mountain" => MountainPools,
            "forest"   => ForestPools,
            "desert"   => DesertPools,
            "water"    => WaterPools,
            "swamp"    => SwampPools,
            "wyrd"     => WyrdPools,
            _          => PlainsPools,
        };

        var tierIndex = dangerLevel switch
        {
            <= 2 => 0,
            <= 4 => 1,
            <= 7 => 2,
            _    => 3,
        };

        // Danger-scaled pack size — higher danger means more enemies
        int packSize = dangerLevel switch
        {
            <= 2 => 1,
            <= 4 => Random.Shared.Next(1, 3),  // 1-2
            <= 6 => Random.Shared.Next(2, 4),  // 2-3
            <= 8 => Random.Shared.Next(2, 5),  // 2-4
            _    => Random.Shared.Next(3, 5),  // 3-4 at danger 9-10
        };

        var pool = biomePools[tierIndex];
        var pack = new List<MonsterTemplate>();

        // First monster is the primary (boss at danger 10)
        var primary = pool[Random.Shared.Next(pool.Length)];
        pack.Add(ScaleMonster(primary, dangerLevel, playerLevel, isBoss: dangerLevel >= 10));

        // Fill remaining pack slots with monsters from the tier below
        while (pack.Count < packSize)
        {
            var weakPool = biomePools[Math.Max(0, tierIndex - 1)];
            var extra = weakPool[Random.Shared.Next(weakPool.Length)];
            pack.Add(ScaleMonster(extra, dangerLevel, playerLevel, isBoss: false));
        }

        return pack;
    }

    private static MonsterTemplate ScaleMonster(MonsterTemplate template, int dangerLevel, int playerLevel, bool isBoss = false)
    {
        var variance = Random.Shared.Next(-1, 2); // -1, 0, or 1
        var monsterLevel = Math.Max(1, dangerLevel + variance);

        if (playerLevel > dangerLevel * 2)
            monsterLevel = Math.Max(monsterLevel, playerLevel - 2);

        // HP scales aggressively with danger: danger 10 = 5x base HP
        double hpMultiplier = 1.0 + dangerLevel * 0.4;
        int scaledHp = (int)(template.Hp * hpMultiplier);

        // Speed scales with danger so high-danger monsters act first more often
        int scaledSpeed = template.Speed + dangerLevel;

        // Ability damage scales with danger: danger 10 = 4x base power
        double powerMultiplier = 1.0 + dangerLevel * 0.3;
        var scaledAbilities = template.Abilities
            .Select(a => a with { BasePower = (int)(a.BasePower * powerMultiplier) })
            .ToArray();

        // Boss at danger 10: double HP and +5 speed on top of normal scaling
        if (isBoss)
        {
            scaledHp   *= 2;
            scaledSpeed += 5;
        }

        return template with
        {
            Level     = monsterLevel,
            Hp        = scaledHp,
            Speed     = scaledSpeed,
            Abilities = scaledAbilities,
        };
    }
}
